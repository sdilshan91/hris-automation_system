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
    private readonly IBackgroundJobClient? _backgroundJobs;
    private readonly ILogger<OnboardingChecklistService> _logger;

    /// <summary>Notification kind written to the outbox on assignment (the dispatcher branches on this).</summary>
    private const string AssignedNotificationType = "onboarding.checklist.assigned";

    /// <summary>Role name resolved as "IT" for FR-3. Not a seeded built-in role; matched by name if present.</summary>
    private const string ItRoleName = "IT";

    public OnboardingChecklistService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<OnboardingChecklistService> logger,
        IBackgroundJobClient? backgroundJobs = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
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
