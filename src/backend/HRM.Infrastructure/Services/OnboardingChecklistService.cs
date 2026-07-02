using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Onboarding checklist assignment service (US-ONB-002). All queries are tenant-scoped via ITenantContext
/// + the EF global query filter (NFR-2). The tenant_id is stamped from the session context, never user
/// input (FR-7). Notification dispatch follows the outbox pattern (NFR-3): one
/// <see cref="OnboardingNotificationOutbox"/> intent row per recipient is written in the SAME SaveChanges
/// as the assignment, then a Hangfire job (<see cref="IOnboardingNotificationDispatchJob"/>) delivers
/// them via <see cref="INotificationDispatcher"/>. Audit columns (FR-8) are stamped by AuditInterceptor.
/// </summary>
public sealed class OnboardingChecklistService : IOnboardingChecklistService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _fileStorage;
    private readonly IVirusScanner _malwareScanner;
    private readonly IBackgroundJobClient? _backgroundJobs;
    private readonly ILogger<OnboardingChecklistService> _logger;

    /// <summary>Notification kind written to the outbox on assignment (the dispatcher branches on this).</summary>
    private const string AssignedNotificationType = "onboarding.checklist.assigned";

    /// <summary>US-ONB-003 AC-3/FR-5: notification kind written when the employee completes a task.</summary>
    private const string TaskCompletedNotificationType = "onboarding.task.completed";

    /// <summary>US-ONB-003 AC-5/FR-6: notification kind written by the daily overdue sweep (BR-4).</summary>
    private const string TaskOverdueNotificationType = "onboarding.task.overdue";

    /// <summary>Role name resolved as "IT" for FR-3. Not a seeded built-in role; matched by name if present.</summary>
    private const string ItRoleName = "IT";

    /// <summary>US-ONB-003 NFR-6: file uploads limited to 10 MB.</summary>
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    /// <summary>US-ONB-003 FR-3: allowed MIME types for onboarding document uploads (mirrors core-HR docs).</summary>
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", // .docx
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",       // .xlsx
    };

    public OnboardingChecklistService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IFileStorage fileStorage,
        IVirusScanner malwareScanner,
        ILogger<OnboardingChecklistService> logger,
        IBackgroundJobClient? backgroundJobs = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
        _malwareScanner = malwareScanner;
        _logger = logger;
        _backgroundJobs = backgroundJobs;
    }

    // ── AC-1 / FR-1: applicable templates ───────────────────────────────

    public async Task<Result<IReadOnlyList<ApplicableTemplateDto>>> GetApplicableTemplatesAsync(
        Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<ApplicableTemplateDto>>.Failure("Tenant context is not resolved.", 400);

        var employee = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (employee is null)
            return Result<IReadOnlyList<ApplicableTemplateDto>>.Failure("Employee not found.", 404, "employee_not_found");

        // BR-1: active templates only. The global filter already scopes to the tenant + excludes deleted.
        var templates = await _dbContext.OnboardingChecklistTemplates
            .AsNoTracking()
            .Include(t => t.Tasks)
            .Where(t => t.IsActive)
            .ToListAsync(cancellationToken);

        // FR-1: a template applies when it is universal in a dimension (empty array) OR explicitly lists the
        // employee's department / job title. Universal-overall templates (both empty) always apply.
        var applicable = templates
            .Where(t =>
                (t.ApplicableDepartments.Count == 0 || t.ApplicableDepartments.Contains(employee.DepartmentId)) &&
                (t.ApplicableJobTitles.Count == 0 || t.ApplicableJobTitles.Contains(employee.JobTitleId)))
            .OrderBy(t => t.TemplateName)
            .Select(t => new ApplicableTemplateDto
            {
                Id = t.Id,
                TemplateName = t.TemplateName,
                Description = t.Description,
                IsUniversal = t.ApplicableDepartments.Count == 0 && t.ApplicableJobTitles.Count == 0,
                TaskCount = t.Tasks.Count,
                MandatoryTaskCount = t.Tasks.Count(x => x.IsMandatory),
            })
            .ToList();

        return Result<IReadOnlyList<ApplicableTemplateDto>>.Success(applicable);
    }

    // ── AC-2 / AC-3: assign ─────────────────────────────────────────────

    public async Task<Result<OnboardingChecklistInstanceDto>> AssignAsync(
        AssignChecklistInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OnboardingChecklistInstanceDto>.Failure("Tenant context is not resolved.", 400);

        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(e => e.Id == input.EmployeeId, cancellationToken);
        if (employee is null)
            return Result<OnboardingChecklistInstanceDto>.Failure("Employee not found.", 404, "employee_not_found");

        var template = await _dbContext.OnboardingChecklistTemplates
            .Include(t => t.Tasks)
            .FirstOrDefaultAsync(t => t.Id == input.TemplateId, cancellationToken);
        if (template is null)
            return Result<OnboardingChecklistInstanceDto>.Failure("Template not found.", 404, "template_not_found");

        // BR-1: only active templates can be assigned.
        if (!template.IsActive)
            return Result<OnboardingChecklistInstanceDto>.Failure(
                "This template is inactive and cannot be assigned.", 409, "template_inactive");

        // NFR-5: idempotency — if this exact assignment was already recorded in the session (same employee +
        // template + idempotency key), return the existing active instance instead of creating a duplicate.
        if (!string.IsNullOrWhiteSpace(input.IdempotencyKey))
        {
            var existingByKey = await _dbContext.OnboardingChecklistInstances
                .Include(c => c.Tasks)
                .FirstOrDefaultAsync(
                    c => c.EmployeeId == input.EmployeeId
                         && c.TemplateId == input.TemplateId
                         && c.CreatedBy == input.IdempotencyKey
                         && c.Status == OnboardingChecklistStatus.Active,
                    cancellationToken);
            if (existingByKey is not null)
                return Result<OnboardingChecklistInstanceDto>.Success(ToDto(existingByKey, 0));
        }

        // BR-2: at most one active checklist per employee.
        var activeExisting = await _dbContext.OnboardingChecklistInstances
            .Include(c => c.Tasks)
            .FirstOrDefaultAsync(
                c => c.EmployeeId == input.EmployeeId && c.Status == OnboardingChecklistStatus.Active,
                cancellationToken);

        // BR-4: anchor due dates to the override start date or the joining date, but never the past.
        var startDate = (input.OverrideStartDate ?? employee.DateOfJoining).Date;
        var today = DateTime.UtcNow.Date;
        if (startDate < today)
            startDate = today;

        // FR-3: resolve responsible users once for the assignment.
        var resolution = await ResolveResponsiblePartiesAsync(employee, cancellationToken);

        OnboardingChecklistInstance instance;
        var queuedNotifications = 0;

        if (activeExisting is not null && input.Mode == ChecklistAssignmentMode.Merge)
        {
            // AC-3 merge: add the template's tasks (+ ad-hoc) onto the existing active checklist. Merged
            // tasks anchor to the existing instance's start date.
            instance = activeExisting;
            var mergeStart = instance.StartDate;
            var maxSort = instance.Tasks.Count == 0 ? 0 : instance.Tasks.Max(t => t.SortOrder);

            // Add the new task instances directly to the DbSet (not via the tracked parent navigation) so EF
            // states them Added unambiguously; keep the in-memory collection in sync for the response/outbox.
            foreach (var tt in template.Tasks.OrderBy(t => t.SortOrder))
                AddTaskInstance(NewTaskFromTemplate(instance.Id, tt, mergeStart, resolution, employee, ++maxSort));

            foreach (var ad in input.AdditionalTasks)
                AddTaskInstance(NewAdHocTask(instance.Id, ad, mergeStart, resolution, employee, ++maxSort));

            queuedNotifications = WriteOutbox(instance, resolution, employee);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            // AC-3 replace (or no existing): supersede any active checklist and create a new version.
            var version = 1;
            if (activeExisting is not null)
            {
                activeExisting.Status = OnboardingChecklistStatus.Superseded;
                version = activeExisting.Version + 1;
            }

            instance = new OnboardingChecklistInstance
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId, // FR-7
                EmployeeId = employee.Id,
                TemplateId = template.Id,
                TemplateName = template.TemplateName,
                Status = OnboardingChecklistStatus.Active,
                StartDate = startDate,
                Version = version,
                AssignedByUserId = _currentUser.UserId,
                // Stash idempotency key in created_by when supplied so NFR-5 retry detection works without a
                // dedicated column; the AuditInterceptor overwrites created_by with the real actor otherwise.
                CreatedBy = string.IsNullOrWhiteSpace(input.IdempotencyKey) ? null : input.IdempotencyKey,
                IsDeleted = false,
            };

            var sort = 0;
            foreach (var tt in template.Tasks.OrderBy(t => t.SortOrder))
                instance.Tasks.Add(NewTaskFromTemplate(instance.Id, tt, startDate, resolution, employee, sort++));

            foreach (var ad in input.AdditionalTasks)
                instance.Tasks.Add(NewAdHocTask(instance.Id, ad, startDate, resolution, employee, sort++));

            _dbContext.OnboardingChecklistInstances.Add(instance);

            queuedNotifications = WriteOutbox(instance, resolution, employee);
            // NFR-3: outbox rows + instance + tasks all persist in ONE transaction (single SaveChanges).
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // NFR-3: enqueue the Hangfire dispatch worker AFTER the transaction commits.
        EnqueueDispatch();

        _logger.LogInformation(
            "Onboarding checklist assigned. InstanceId={InstanceId}, Mode={Mode}, Employee={EmployeeId}, " +
            "Template={TemplateId}, Version={Version}, Tasks={TaskCount}, NotificationsQueued={Notifications}, " +
            "TenantId={TenantId}, By={User}",
            instance.Id, input.Mode, employee.Id, template.Id, instance.Version,
            instance.Tasks.Count(t => !t.IsDeleted), queuedNotifications, _tenantContext.TenantId, _currentUser.Email);

        return Result<OnboardingChecklistInstanceDto>.Success(ToDto(instance, queuedNotifications));
    }

    // ── Get ─────────────────────────────────────────────────────────────

    public async Task<Result<OnboardingChecklistInstanceDto>> GetInstanceAsync(
        Guid checklistInstanceId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OnboardingChecklistInstanceDto>.Failure("Tenant context is not resolved.", 400);

        var instance = await _dbContext.OnboardingChecklistInstances
            .AsNoTracking()
            .Include(c => c.Tasks)
            .FirstOrDefaultAsync(c => c.Id == checklistInstanceId, cancellationToken);
        if (instance is null)
            return Result<OnboardingChecklistInstanceDto>.Failure("Checklist not found.", 404, "checklist_not_found");

        return Result<OnboardingChecklistInstanceDto>.Success(ToDto(instance, 0));
    }

    // ── AC-4 / FR-5 / FR-6: modify ──────────────────────────────────────

    public async Task<Result<OnboardingChecklistInstanceDto>> ModifyAsync(
        ModifyChecklistInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OnboardingChecklistInstanceDto>.Failure("Tenant context is not resolved.", 400);

        var instance = await _dbContext.OnboardingChecklistInstances
            .Include(c => c.Tasks)
            .FirstOrDefaultAsync(c => c.Id == input.ChecklistInstanceId, cancellationToken);
        if (instance is null)
            return Result<OnboardingChecklistInstanceDto>.Failure("Checklist not found.", 404, "checklist_not_found");

        if (instance.Status != OnboardingChecklistStatus.Active)
            return Result<OnboardingChecklistInstanceDto>.Failure(
                "Only an active checklist can be modified.", 409, "checklist_not_active");

        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(e => e.Id == instance.EmployeeId, cancellationToken);
        if (employee is null)
            return Result<OnboardingChecklistInstanceDto>.Failure("Employee not found.", 404, "employee_not_found");

        // Apply per-task changes (FR-6 due-date edits, AC-4 soft-delete) — BR-3 blocks mandatory removal.
        foreach (var change in input.TaskChanges)
        {
            var task = instance.Tasks.FirstOrDefault(t => t.Id == change.TaskInstanceId && !t.IsDeleted);
            if (task is null)
                return Result<OnboardingChecklistInstanceDto>.Failure(
                    "Task not found on this checklist.", 404, "task_not_found");

            if (change.Remove)
            {
                // BR-3: mandatory tasks cannot be removed.
                if (task.IsMandatory)
                    return Result<OnboardingChecklistInstanceDto>.Failure(
                        "Mandatory tasks cannot be removed.", 409, "mandatory_task");
                task.IsDeleted = true; // AC-4 soft-delete.
            }
            else if (change.NewDueDate.HasValue)
            {
                task.DueDate = change.NewDueDate.Value.Date; // FR-6.
            }
        }

        // FR-5: add ad-hoc tasks — due date = today + offset (AC-4 "added tasks based on today's date").
        if (input.AddTasks.Count > 0)
        {
            var resolution = await ResolveResponsiblePartiesAsync(employee, cancellationToken);
            var today = DateTime.UtcNow.Date;
            var maxSort = instance.Tasks.Count == 0 ? 0 : instance.Tasks.Max(t => t.SortOrder);
            foreach (var ad in input.AddTasks)
                AddTaskInstance(NewAdHocTask(instance.Id, ad, today, resolution, employee, ++maxSort));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Onboarding checklist modified. InstanceId={InstanceId}, Added={Added}, Changed={Changed}, " +
            "TenantId={TenantId}, By={User}",
            instance.Id, input.AddTasks.Count, input.TaskChanges.Count, _tenantContext.TenantId, _currentUser.Email);

        return Result<OnboardingChecklistInstanceDto>.Success(ToDto(instance, 0));
    }

    // ── FR-3 responsible-party resolution ───────────────────────────────

    private sealed record PartyResolution(
        Guid? ManagerUserId,
        Guid HrUserId,
        Guid? EmployeeUserId,
        IReadOnlyList<Guid> ItUserIds);

    private async Task<PartyResolution> ResolveResponsiblePartiesAsync(
        Employee employee, CancellationToken cancellationToken)
    {
        // Manager (FR-3) → the reporting manager's linked user account (BR-5: may be null if unlinked).
        Guid? managerUserId = null;
        if (employee.ReportsToEmployeeId.HasValue)
        {
            var manager = await _dbContext.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == employee.ReportsToEmployeeId.Value, cancellationToken);
            managerUserId = manager?.UserId;
        }

        // IT (FR-3) → users with the "IT" role in this tenant. The IT role is not a seeded built-in; matched
        // by name when a tenant has defined one. Resolved as user ids for the outbox recipients.
        var itUserIds = await _dbContext.UserTenants
            .Where(ut => ut.UserTenantRoles.Any(r => r.Role.Name == ItRoleName))
            .Select(ut => ut.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return new PartyResolution(
            ManagerUserId: managerUserId,
            HrUserId: _currentUser.UserId,           // HR (FR-3) → the assigning officer.
            EmployeeUserId: employee.UserId,          // Employee (FR-3) → the new hire's linked user (BR-5).
            ItUserIds: itUserIds);
    }

    private static Guid? ResolveTaskUser(OnboardingResponsibleRole role, Guid? explicitUserId, PartyResolution r)
    {
        if (explicitUserId.HasValue)
            return explicitUserId; // a named user always wins (FR-3).

        return role switch
        {
            OnboardingResponsibleRole.Manager => r.ManagerUserId,
            OnboardingResponsibleRole.HR => r.HrUserId,
            OnboardingResponsibleRole.Employee => r.EmployeeUserId,
            // IT may resolve to many users; store a single one only when unambiguous, else leave null (the
            // outbox still notifies every IT user).
            OnboardingResponsibleRole.IT => r.ItUserIds.Count == 1 ? r.ItUserIds[0] : null,
            _ => null,
        };
    }

    /// <summary>
    /// Adds a new task instance to a TRACKED checklist (merge/modify paths): registers it on the DbSet so
    /// EF states it Added unambiguously. EF relationship fixup then mirrors it into the tracked parent's
    /// <see cref="OnboardingChecklistInstance.Tasks"/> navigation (its ChecklistInstanceId is set), so the
    /// response DTO + outbox see it — adding it manually too would double-count it.
    /// </summary>
    private void AddTaskInstance(OnboardingTaskInstance task)
        => _dbContext.OnboardingTaskInstances.Add(task);

    private OnboardingTaskInstance NewTaskFromTemplate(
        Guid instanceId, OnboardingTemplateTask t, DateTime startDate, PartyResolution r, Employee employee, int sortOrder)
        => new()
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId, // FR-7
            ChecklistInstanceId = instanceId,
            SourceTemplateTaskId = t.Id,
            Title = t.Title,
            Description = t.Description,
            Category = t.Category,
            ResponsibleRole = t.ResponsibleRole,
            ResponsibleUserId = ResolveTaskUser(t.ResponsibleRole, t.ResponsibleUserId, r),
            DueDate = startDate.AddDays(t.DueOffsetDays), // FR-2 / BR-4.
            Status = OnboardingTaskStatus.Pending,         // AC-2.
            IsMandatory = t.IsMandatory,
            RequiresDocument = t.RequiresDocument,          // US-ONB-003 AC-4 propagation.
            SortOrder = sortOrder,
            IsDeleted = false,
        };

    private OnboardingTaskInstance NewAdHocTask(
        Guid instanceId, AdHocTaskInput t, DateTime anchorDate, PartyResolution r, Employee employee, int sortOrder)
        => new()
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId, // FR-7
            ChecklistInstanceId = instanceId,
            SourceTemplateTaskId = null, // FR-5 ad-hoc.
            Title = t.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(t.Description) ? null : t.Description.Trim(),
            Category = string.IsNullOrWhiteSpace(t.Category) ? null : t.Category.Trim(),
            ResponsibleRole = t.ResponsibleRole,
            ResponsibleUserId = ResolveTaskUser(t.ResponsibleRole, t.ResponsibleUserId, r),
            DueDate = anchorDate.AddDays(t.DueOffsetDays),
            Status = OnboardingTaskStatus.Pending,
            IsMandatory = t.IsMandatory,
            SortOrder = sortOrder,
            IsDeleted = false,
        };

    // ── NFR-3 outbox ────────────────────────────────────────────────────

    /// <summary>
    /// Writes one notification-intent row per distinct recipient (AC-2/AC-5/FR-4) onto the tracked context
    /// so they persist in the SAME transaction as the assignment. Returns the count queued.
    /// </summary>
    private int WriteOutbox(OnboardingChecklistInstance instance, PartyResolution r, Employee employee)
    {
        var recipients = new Dictionary<Guid, OnboardingResponsibleRole>();

        // The new hire (AC-2) — only when their user account is linked (BR-5).
        if (r.EmployeeUserId.HasValue)
            recipients[r.EmployeeUserId.Value] = OnboardingResponsibleRole.Employee;
        // The manager (AC-5).
        if (r.ManagerUserId.HasValue)
            recipients.TryAdd(r.ManagerUserId.Value, OnboardingResponsibleRole.Manager);
        // IT users (AC-5).
        foreach (var it in r.ItUserIds)
            recipients.TryAdd(it, OnboardingResponsibleRole.IT);
        // Any additionally named responsible users on the active tasks (FR-4).
        foreach (var task in instance.Tasks.Where(t => !t.IsDeleted && t.ResponsibleUserId.HasValue))
            recipients.TryAdd(task.ResponsibleUserId!.Value, task.ResponsibleRole);

        var payload = JsonSerializer.Serialize(new
        {
            checklistInstanceId = instance.Id,
            employeeId = employee.Id,
            employeeName = $"{employee.FirstName} {employee.LastName}".Trim(),
            templateId = instance.TemplateId,
            templateName = instance.TemplateName,
            startDate = instance.StartDate,
            taskCount = instance.Tasks.Count(t => !t.IsDeleted),
        });

        foreach (var (userId, role) in recipients)
        {
            _dbContext.OnboardingNotificationOutbox.Add(new OnboardingNotificationOutbox
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId, // FR-7
                ChecklistInstanceId = instance.Id,
                RecipientUserId = userId,
                RecipientRole = role,
                NotificationType = AssignedNotificationType,
                Payload = payload,
                Status = OnboardingNotificationStatus.Pending,
                AttemptCount = 0,
                IsDeleted = false,
            });
        }

        return recipients.Count;
    }

    private void EnqueueDispatch()
    {
        if (_backgroundJobs is null)
            return; // no Hangfire client wired (e.g. unit tests) — the worker can also be run on a schedule.

        var tenantId = _tenantContext.TenantId;
        _backgroundJobs.Enqueue<IOnboardingNotificationDispatchJob>(j => j.RunAsync(tenantId, CancellationToken.None));
    }

    // ── US-ONB-003: self-service (the logged-in employee acts on their own tasks) ──

    public async Task<Result<MyChecklistDto>> GetMyChecklistAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<MyChecklistDto>.Failure("Tenant context is not resolved.", 400);

        var (instance, error) = await LoadMyActiveChecklistAsync(cancellationToken);
        if (error is not null)
            return Result<MyChecklistDto>.Failure(error.Message, error.Status, error.Code);

        var now = DateTime.UtcNow;
        var tasks = instance!.Tasks.Where(t => !t.IsDeleted).OrderBy(t => t.SortOrder).ToList();

        var dto = new MyChecklistDto
        {
            ChecklistInstanceId = instance.Id,
            Status = instance.Status,
            ProgressPercent = ProgressPercent(tasks),
            TotalTasks = tasks.Count,
            CompletedTasks = tasks.Count(t => t.Status == OnboardingTaskStatus.Completed),
            PendingTasks = tasks.Count(t => t.Status != OnboardingTaskStatus.Completed),
            OverdueTasks = tasks.Count(t => IsOverdue(t, now)),
            // AC-2: group by category (null/empty → "General"), preserving sort order within each group.
            Categories = tasks
                .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "General" : t.Category!)
                .OrderBy(g => g.Key)
                .Select(g => new MyChecklistCategoryDto
                {
                    Category = g.Key,
                    Tasks = g.Select(t => ToTaskDto(t, now)).ToList(),
                })
                .ToList(),
        };

        return Result<MyChecklistDto>.Success(dto);
    }

    public async Task<Result<MyChecklistProgressDto>> GetMyChecklistProgressAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<MyChecklistProgressDto>.Failure("Tenant context is not resolved.", 400);

        var (instance, error) = await LoadMyActiveChecklistAsync(cancellationToken);
        if (error is not null)
            return Result<MyChecklistProgressDto>.Failure(error.Message, error.Status, error.Code);

        var now = DateTime.UtcNow;
        var tasks = instance!.Tasks.Where(t => !t.IsDeleted).ToList();
        return Result<MyChecklistProgressDto>.Success(Progress(tasks, now));
    }

    public async Task<Result<CompleteTaskResultDto>> CompleteTaskAsync(
        CompleteTaskInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CompleteTaskResultDto>.Failure("Tenant context is not resolved.", 400);

        // Resolve the caller's employee record (FR-7: a task is theirs only if it belongs to their checklist).
        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);
        if (employee is null)
            return Result<CompleteTaskResultDto>.Failure(
                "No employee record is linked to your account.", 404, "employee_not_found");

        // Load the task with its parent checklist (tracked, tenant-scoped via the global filter).
        var task = await _dbContext.OnboardingTaskInstances
            .Include(t => t.ChecklistInstance)
            .FirstOrDefaultAsync(t => t.Id == input.TaskInstanceId && !t.IsDeleted, cancellationToken);
        if (task is null || task.ChecklistInstance is null)
            return Result<CompleteTaskResultDto>.Failure("Task not found.", 404, "task_not_found");

        // FR-7 / BR-1: the task must belong to THIS employee's checklist AND be assigned to the Employee role.
        // We return 404 (not 403) when the task is on someone else's checklist to avoid leaking existence.
        if (task.ChecklistInstance.EmployeeId != employee.Id)
            return Result<CompleteTaskResultDto>.Failure("Task not found.", 404, "task_not_found");

        if (task.ResponsibleRole != OnboardingResponsibleRole.Employee)
            return Result<CompleteTaskResultDto>.Failure(
                "This task is assigned to another role and cannot be completed by you.", 403, "task_not_yours");

        // BR-3: a completed task cannot be reverted/re-completed by the employee (only HR may reopen).
        if (task.Status == OnboardingTaskStatus.Completed)
            return Result<CompleteTaskResultDto>.Failure(
                "This task is already completed and cannot be changed.", 409, "task_already_completed");

        // AC-4 / FR-3: a document-required task must have a file; a non-document task may optionally have one.
        var hasFile = input.AttachmentStream is not null;
        if (task.RequiresDocument && !hasFile)
            return Result<CompleteTaskResultDto>.Failure(
                "This task requires a document to be uploaded.", 400, "document_required");

        string? storageKey = null;
        if (hasFile)
        {
            var uploadResult = await ValidateAndStoreAttachmentAsync(employee.Id, task.Id, input, cancellationToken);
            if (uploadResult.IsFailure)
                return Result<CompleteTaskResultDto>.Failure(
                    uploadResult.Error!, uploadResult.StatusCode ?? 400, uploadResult.ErrorCode);
            storageKey = uploadResult.Value;
        }

        // AC-3: mark completed, record timestamp + actor; comment (FR-2). Audit columns set by AuditInterceptor.
        var now = DateTime.UtcNow;
        task.Status = OnboardingTaskStatus.Completed;
        task.CompletedAt = now;
        task.CompletedByUserId = _currentUser.UserId;
        task.CompletionComment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim();
        if (storageKey is not null)
        {
            task.AttachmentStorageKey = storageKey;
            task.AttachmentFileName = SanitizeFileName(input.AttachmentFileName!);
        }

        // AC-3/AC-4/FR-5: notify HR + manager via the outbox (same transaction as the completion).
        var queued = await WriteTaskCompletionOutboxAsync(task, employee, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        EnqueueDispatch();

        _logger.LogInformation(
            "Onboarding task completed. TaskId={TaskId}, ChecklistId={ChecklistId}, Employee={EmployeeId}, " +
            "HadAttachment={HadAttachment}, NotificationsQueued={Queued}, TenantId={TenantId}, By={User}",
            task.Id, task.ChecklistInstanceId, employee.Id, hasFile, queued,
            _tenantContext.TenantId, _currentUser.Email);

        // Recalculate progress over the (now updated) sibling tasks.
        var siblings = await _dbContext.OnboardingTaskInstances
            .AsNoTracking()
            .Where(t => t.ChecklistInstanceId == task.ChecklistInstanceId && !t.IsDeleted)
            .ToListAsync(cancellationToken);

        return Result<CompleteTaskResultDto>.Success(new CompleteTaskResultDto
        {
            Task = ToTaskDto(task, now),
            Progress = Progress(siblings, now),
        });
    }

    /// <summary>
    /// US-ONB-003 FR-6/AC-5/BR-4: the daily overdue sweep. Finds past-due, not-completed tasks for the given
    /// tenant and writes overdue notification-outbox rows to the employee, HR (the assigning officer), and the
    /// manager. Idempotent per task per UTC day: a task already notified today is skipped. Runs at SYSTEM
    /// level (no request scope), so it bypasses the query filter and scopes by the passed tenant id explicitly.
    /// NOTE: tenant-timezone scheduling (BR-4 default 09:00) is NOT built — the sweep uses UTC.
    /// </summary>
    public async Task<int> RunOverdueSweepAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;

        // Past-due, not-completed, on an active checklist, for this tenant.
        var overdue = await _dbContext.OnboardingTaskInstances
            .IgnoreQueryFilters()
            .Include(t => t.ChecklistInstance)
            .Where(t => !t.IsDeleted
                && t.TenantId == tenantId
                && t.Status != OnboardingTaskStatus.Completed
                && t.Status != OnboardingTaskStatus.Skipped
                && t.DueDate < today
                && t.ChecklistInstance!.Status == OnboardingChecklistStatus.Active
                && !t.ChecklistInstance.IsDeleted)
            .ToListAsync(cancellationToken);

        if (overdue.Count == 0)
            return 0;

        // Pre-load the employees referenced by these checklists (for manager/employee user resolution).
        var employeeIds = overdue.Select(t => t.ChecklistInstance!.EmployeeId).Distinct().ToList();
        var employees = await _dbContext.Employees
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && employeeIds.Contains(e.Id))
            .ToListAsync(cancellationToken);
        var employeesById = employees.ToDictionary(e => e.Id);

        var managerUserByEmployeeId = new Dictionary<Guid, Guid?>();
        foreach (var emp in employees)
        {
            Guid? managerUserId = null;
            if (emp.ReportsToEmployeeId.HasValue)
            {
                var manager = await _dbContext.Employees
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        e => e.TenantId == tenantId && e.Id == emp.ReportsToEmployeeId.Value, cancellationToken);
                managerUserId = manager?.UserId;
            }
            managerUserByEmployeeId[emp.Id] = managerUserId;
        }

        var written = 0;
        foreach (var task in overdue)
        {
            if (!employeesById.TryGetValue(task.ChecklistInstance!.EmployeeId, out var emp))
                continue;

            // Idempotency (BR-4 "once per day"): skip if an overdue row for this task already exists today.
            // BUG-126: Payload is a jsonb column — `string.Contains` on it is not translatable by Npgsql
            // (`operator does not exist: jsonb ~~ jsonb`) and threw, failing this recurring job on every run.
            // Match the task id IN MEMORY over the tiny per-instance/day candidate set instead.
            var taskIdString = task.Id.ToString();
            var candidatePayloads = await _dbContext.OnboardingNotificationOutbox
                .IgnoreQueryFilters()
                .Where(o => o.TenantId == tenantId
                    && o.ChecklistInstanceId == task.ChecklistInstanceId
                    && o.NotificationType == TaskOverdueNotificationType
                    && o.CreatedAt >= today)
                .Select(o => o.Payload)
                .ToListAsync(cancellationToken);
            var alreadyNotifiedToday = candidatePayloads.Any(p => p != null && p.Contains(taskIdString));
            if (alreadyNotifiedToday)
                continue;

            var recipients = new Dictionary<Guid, OnboardingResponsibleRole>();
            if (emp.UserId.HasValue)
                recipients[emp.UserId.Value] = OnboardingResponsibleRole.Employee; // the employee.
            if (task.ChecklistInstance.AssignedByUserId.HasValue)
                recipients.TryAdd(task.ChecklistInstance.AssignedByUserId.Value, OnboardingResponsibleRole.HR); // HR.
            if (managerUserByEmployeeId.TryGetValue(emp.Id, out var mgr) && mgr.HasValue)
                recipients.TryAdd(mgr.Value, OnboardingResponsibleRole.Manager); // manager.

            var payload = JsonSerializer.Serialize(new
            {
                checklistInstanceId = task.ChecklistInstanceId,
                taskInstanceId = task.Id,
                taskTitle = task.Title,
                dueDate = task.DueDate,
                daysOverdue = (today - task.DueDate.Date).Days,
                employeeId = emp.Id,
                employeeName = $"{emp.FirstName} {emp.LastName}".Trim(),
            });

            foreach (var (userId, role) in recipients)
            {
                _dbContext.OnboardingNotificationOutbox.Add(new OnboardingNotificationOutbox
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = tenantId,
                    ChecklistInstanceId = task.ChecklistInstanceId,
                    RecipientUserId = userId,
                    RecipientRole = role,
                    NotificationType = TaskOverdueNotificationType,
                    Payload = payload,
                    Status = OnboardingNotificationStatus.Pending,
                    AttemptCount = 0,
                    IsDeleted = false,
                });
                written++;
            }
        }

        if (written > 0)
            await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Onboarding overdue sweep: {Overdue} overdue task(s), {Written} notification row(s) for tenant {TenantId}.",
            overdue.Count, written, tenantId);

        return written;
    }

    // ── US-ONB-003 helpers ───────────────────────────────────────────────

    private sealed record LoadError(string Message, int Status, string? Code);

    /// <summary>Loads the caller's active checklist (with tasks), or a typed error (no employee / no checklist).</summary>
    private async Task<(OnboardingChecklistInstance? Instance, LoadError? Error)> LoadMyActiveChecklistAsync(
        CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);
        if (employee is null)
            return (null, new LoadError("No employee record is linked to your account.", 404, "employee_not_found"));

        var instance = await _dbContext.OnboardingChecklistInstances
            .AsNoTracking()
            .Include(c => c.Tasks)
            .FirstOrDefaultAsync(
                c => c.EmployeeId == employee.Id && c.Status == OnboardingChecklistStatus.Active, cancellationToken);
        if (instance is null)
            return (null, new LoadError("You have no active onboarding checklist.", 404, "checklist_not_found"));

        return (instance, null);
    }

    /// <summary>
    /// US-ONB-003 FR-3/NFR-3/NFR-6/AC-4: validates the attachment (MIME + size), scans it via the malware
    /// seam, and stores it at the tenant-isolated path {tenantId}/onboarding/{employeeId}/{taskId}/{filename}.
    /// Returns the relative storage key.
    /// </summary>
    private async Task<Result<string>> ValidateAndStoreAttachmentAsync(
        Guid employeeId, Guid taskId, CompleteTaskInput input, CancellationToken cancellationToken)
    {
        var stream = input.AttachmentStream!;

        // FR-3: MIME allow-list.
        if (string.IsNullOrWhiteSpace(input.AttachmentContentType) ||
            !AllowedMimeTypes.Contains(input.AttachmentContentType))
            return Result<string>.Failure("File type not allowed. Supported: PDF, JPEG, PNG, DOCX, XLSX.", 400, "invalid_file_type");

        // NFR-6: size cap. Prefer the supplied size; fall back to the stream length when seekable.
        var size = input.AttachmentSize > 0 ? input.AttachmentSize : (stream.CanSeek ? stream.Length : 0);
        if (size > MaxFileSizeBytes)
            return Result<string>.Failure("File exceeds the 10 MB limit.", 400, "file_too_large");

        // NFR-3: malware scan before persistence (seam — allow-all stub until ClamAV is wired).
        var scan = await _malwareScanner.ScanAsync(stream, input.AttachmentFileName ?? "attachment", cancellationToken);
        if (!scan.IsClean)
        {
            _logger.LogWarning(
                "Onboarding document rejected by malware scanner. Task={TaskId}, Threat={Threat}, TenantId={TenantId}",
                taskId, scan.ThreatName, _tenantContext.TenantId);
            return Result<string>.Failure($"File rejected by malware scanner: {scan.ThreatName}.", 400, "malware_detected");
        }
        if (stream.CanSeek)
            stream.Position = 0;

        // AC-4: tenant-isolated path. IFileStorage already prefixes the tenant id, so the relative path is
        // onboarding/{employeeId}/{taskId}/{filename} → physical {tenantId}/onboarding/{employeeId}/{taskId}/{filename}.
        var fileName = SanitizeFileName(input.AttachmentFileName!);
        var relativePath = $"onboarding/{employeeId}/{taskId}/{fileName}";
        await _fileStorage.UploadAsync(
            _tenantContext.TenantId, relativePath, stream, input.AttachmentContentType!, cancellationToken);

        return Result<string>.Success(relativePath);
    }

    /// <summary>AC-3/AC-4/FR-5: writes completion notification-outbox rows to HR + the manager.</summary>
    private async Task<int> WriteTaskCompletionOutboxAsync(
        OnboardingTaskInstance task, Employee employee, CancellationToken cancellationToken)
    {
        var recipients = new Dictionary<Guid, OnboardingResponsibleRole>();

        // HR — the officer who assigned the checklist (FR-5).
        if (task.ChecklistInstance!.AssignedByUserId.HasValue)
            recipients[task.ChecklistInstance.AssignedByUserId.Value] = OnboardingResponsibleRole.HR;

        // Manager — the new hire's reporting manager's linked user (FR-5).
        if (employee.ReportsToEmployeeId.HasValue)
        {
            var manager = await _dbContext.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == employee.ReportsToEmployeeId.Value, cancellationToken);
            if (manager?.UserId is Guid mgrUserId)
                recipients.TryAdd(mgrUserId, OnboardingResponsibleRole.Manager);
        }

        if (recipients.Count == 0)
            return 0;

        var payload = JsonSerializer.Serialize(new
        {
            checklistInstanceId = task.ChecklistInstanceId,
            taskInstanceId = task.Id,
            taskTitle = task.Title,
            employeeId = employee.Id,
            employeeName = $"{employee.FirstName} {employee.LastName}".Trim(),
            completedAt = task.CompletedAt,
        });

        foreach (var (userId, role) in recipients)
        {
            _dbContext.OnboardingNotificationOutbox.Add(new OnboardingNotificationOutbox
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                ChecklistInstanceId = task.ChecklistInstanceId,
                RecipientUserId = userId,
                RecipientRole = role,
                NotificationType = TaskCompletedNotificationType,
                Payload = payload,
                Status = OnboardingNotificationStatus.Pending,
                AttemptCount = 0,
                IsDeleted = false,
            });
        }

        return recipients.Count;
    }

    /// <summary>AC-5: a task is overdue when past its due date and not completed/skipped.</summary>
    private static bool IsOverdue(OnboardingTaskInstance t, DateTime now) =>
        t.Status != OnboardingTaskStatus.Completed
        && t.Status != OnboardingTaskStatus.Skipped
        && t.DueDate.Date < now.Date;

    /// <summary>FR-4: progress = completed / total (0 when no tasks). Excludes soft-deleted (caller pre-filters).</summary>
    private static int ProgressPercent(IReadOnlyCollection<OnboardingTaskInstance> tasks)
    {
        if (tasks.Count == 0) return 0;
        var completed = tasks.Count(t => t.Status == OnboardingTaskStatus.Completed);
        return (int)Math.Round(completed * 100.0 / tasks.Count, MidpointRounding.AwayFromZero);
    }

    private static MyChecklistProgressDto Progress(IReadOnlyCollection<OnboardingTaskInstance> tasks, DateTime now) => new()
    {
        ProgressPercent = ProgressPercent(tasks),
        TotalTasks = tasks.Count,
        CompletedTasks = tasks.Count(t => t.Status == OnboardingTaskStatus.Completed),
        PendingTasks = tasks.Count(t => t.Status != OnboardingTaskStatus.Completed),
        OverdueTasks = tasks.Count(t => IsOverdue(t, now)),
    };

    /// <summary>Maps a task instance to the employee-facing DTO, computing the effective overdue status (AC-5).</summary>
    private MyChecklistTaskDto ToTaskDto(OnboardingTaskInstance t, DateTime now)
    {
        var status = t.Status == OnboardingTaskStatus.Completed
            ? "completed"
            : IsOverdue(t, now) ? "overdue" : t.Status.ToString().ToLowerInvariant();

        string? attachmentUrl = null;
        if (!string.IsNullOrEmpty(t.AttachmentStorageKey))
            attachmentUrl = _fileStorage.GetSignedUrl(_tenantContext.TenantId, t.AttachmentStorageKey, TimeSpan.FromMinutes(5));

        return new MyChecklistTaskDto
        {
            TaskInstanceId = t.Id,
            Title = t.Title,
            Description = t.Description,
            Category = t.Category,
            ResponsibleRole = t.ResponsibleRole,
            DueDate = t.DueDate,
            Status = status,
            IsMandatory = t.IsMandatory,
            RequiresDocument = t.RequiresDocument,
            CompletedAt = t.CompletedAt,
            CompletedBy = t.CompletedByUserId,
            Comment = t.CompletionComment,
            AttachmentUrl = attachmentUrl,
        };
    }

    /// <summary>Sanitizes a file name (strip path components + invalid chars) for storage-key safety.</summary>
    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "attachment" : name;
    }

    // ── Mapping ─────────────────────────────────────────────────────────

    private static OnboardingChecklistInstanceDto ToDto(OnboardingChecklistInstance c, int notificationsQueued) => new()
    {
        Id = c.Id,
        EmployeeId = c.EmployeeId,
        TemplateId = c.TemplateId,
        TemplateName = c.TemplateName,
        Status = c.Status,
        StatusName = c.Status.ToString(),
        StartDate = c.StartDate,
        Version = c.Version,
        AssignedByUserId = c.AssignedByUserId,
        NotificationsQueued = notificationsQueued,
        TaskCount = c.Tasks.Count(t => !t.IsDeleted),
        Tasks = c.Tasks
            .Where(t => !t.IsDeleted)
            .OrderBy(t => t.SortOrder)
            .Select(t => new OnboardingTaskInstanceDto
            {
                Id = t.Id,
                SourceTemplateTaskId = t.SourceTemplateTaskId,
                Title = t.Title,
                Description = t.Description,
                Category = t.Category,
                ResponsibleRole = t.ResponsibleRole,
                ResponsibleRoleName = t.ResponsibleRole.ToString(),
                ResponsibleUserId = t.ResponsibleUserId,
                DueDate = t.DueDate,
                Status = t.Status,
                StatusName = t.Status.ToString(),
                IsMandatory = t.IsMandatory,
                SortOrder = t.SortOrder,
            }).ToList(),
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
    };
}
