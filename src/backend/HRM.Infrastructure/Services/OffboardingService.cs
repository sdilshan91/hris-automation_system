using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Offboarding / exit-clearance service (US-ONB-005). All queries are tenant-scoped via ITenantContext + the
/// EF global query filter (NFR-2/AC-6); the tenant_id is stamped from the session, never user input (FR-8).
/// Audit columns (FR-9) are stamped by AuditInterceptor. Account deactivation (FR-5) reuses the existing
/// User.IsActive flag + IAuthService.RevokeAllSessionsAsync refresh-token revocation; the ISessionRevoker
/// seam (FR-7) covers the Redis JWT denylist that is not in the stack. F&amp;F (FR-6/BR-4) is triggered via the
/// IPayrollFnFIntegration seam — offboarding never calculates settlement (Payroll owns it).
/// </summary>
public sealed class OffboardingService : IOffboardingService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IAuthService _authService;
    private readonly ISessionRevoker _sessionRevoker;
    private readonly IPayrollFnFIntegration _payrollFnF;
    private readonly ILogger<OffboardingService> _logger;

    /// <summary>
    /// BR-1 status gate. EmployeeStatus has no "resignation_accepted"/"contract_ended" values, so the story's
    /// statuses are MAPPED to the closest existing terminal/notice states: <see cref="EmployeeStatus.Terminated"/>
    /// (≈ "terminated") and <see cref="EmployeeStatus.Suspended"/> (≈ employee on notice / resignation accepted /
    /// contract ending). NOTE: this is an approximation flagged to the caller; a dedicated notice-period status
    /// would be more accurate when the Employee lifecycle adds one.
    /// </summary>
    private static readonly HashSet<EmployeeStatus> OffboardingEligibleStatuses =
    [
        EmployeeStatus.Terminated,
        EmployeeStatus.Suspended,
    ];

    public OffboardingService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IAuthService authService,
        ISessionRevoker sessionRevoker,
        IPayrollFnFIntegration payrollFnF,
        ILogger<OffboardingService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _authService = authService;
        _sessionRevoker = sessionRevoker;
        _payrollFnF = payrollFnF;
        _logger = logger;
    }

    // ── AC-1 / FR-2: initiate ───────────────────────────────────────────

    public async Task<Result<OffboardingInstanceDto>> InitiateAsync(
        InitiateOffboardingInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OffboardingInstanceDto>.Failure("Tenant context is not resolved.", 400);

        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(e => e.Id == input.EmployeeId, cancellationToken);
        if (employee is null)
            return Result<OffboardingInstanceDto>.Failure("Employee not found.", 404, "employee_not_found");

        // BR-1: only departing-status employees may be offboarded (mapped statuses — see field doc).
        if (!OffboardingEligibleStatuses.Contains(employee.Status))
            return Result<OffboardingInstanceDto>.Failure(
                "Offboarding can only be initiated for an employee whose status is Terminated or Suspended " +
                "(notice/resignation-accepted). Update the employee status first.",
                409, "invalid_employee_status");

        // FR data rule: last working day must be today or in the future.
        // BUG-289 class: last_working_day / task due_date are timestamptz — .Date is Kind=Unspecified (rejected
        // by Npgsql). UTC-kind it here so it (and the derived task due dates) persist on real Postgres.
        var lwd = DateTime.SpecifyKind(input.LastWorkingDay.Date, DateTimeKind.Utc);
        if (lwd < DateTime.UtcNow.Date)
            return Result<OffboardingInstanceDto>.Failure(
                "Last working day must be today or in the future.", 400, "lwd_in_past");

        // At most one in-progress offboarding per employee.
        var existing = await _dbContext.OffboardingInstances
            .FirstOrDefaultAsync(
                o => o.EmployeeId == input.EmployeeId && o.Status == OffboardingStatus.InProgress,
                cancellationToken);
        if (existing is not null)
            return Result<OffboardingInstanceDto>.Failure(
                "An offboarding is already in progress for this employee.", 409, "offboarding_in_progress");

        // FR-1: resolve the offboarding template, if one was supplied (must be a Kind=Offboarding template).
        OnboardingChecklistTemplate? template = null;
        if (input.OffboardingTemplateId.HasValue)
        {
            template = await _dbContext.OnboardingChecklistTemplates
                .Include(t => t.Tasks)
                .FirstOrDefaultAsync(t => t.Id == input.OffboardingTemplateId.Value, cancellationToken);
            if (template is null)
                return Result<OffboardingInstanceDto>.Failure("Template not found.", 404, "template_not_found");
            if (template.Kind != ChecklistKind.Offboarding)
                return Result<OffboardingInstanceDto>.Failure(
                    "The supplied template is not an offboarding template.", 400, "not_offboarding_template");
        }

        // FR-3: resolve responsible users once (Manager → reporting manager's user, Employee → the departing hire).
        Guid? managerUserId = null;
        if (employee.ReportsToEmployeeId.HasValue)
        {
            var manager = await _dbContext.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == employee.ReportsToEmployeeId.Value, cancellationToken);
            managerUserId = manager?.UserId;
        }

        var instanceId = BaseEntity.NewUuidV7();
        var instance = new OffboardingInstance
        {
            Id = instanceId,
            TenantId = _tenantContext.TenantId, // FR-8
            EmployeeId = employee.Id,
            TemplateId = template?.Id,
            TemplateName = template?.TemplateName ?? "Default Exit Clearance",
            LastWorkingDay = lwd,
            Reason = input.Reason,
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            Status = OffboardingStatus.InProgress,
            InitiatedByUserId = _currentUser.UserId,
            IsDeleted = false,
        };

        // FR-2: build tasks — from the template, or from the built-in DEFAULT clearance set (FR-3).
        var taskSpecs = template is not null
            ? template.Tasks.OrderBy(t => t.SortOrder).Select(t => new TaskSpec(
                t.Id,
                MapCategory(t.Category, t.ResponsibleRole),
                t.Title,
                t.Description,
                t.ResponsibleRole,
                t.DueOffsetDays,
                t.IsMandatory)).ToList()
            : DefaultClearanceTaskSpecs();

        var sort = 0;
        foreach (var spec in taskSpecs)
        {
            instance.Tasks.Add(new OffboardingTaskInstance
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId, // FR-8
                OffboardingInstanceId = instanceId,
                SourceTemplateTaskId = spec.SourceTemplateTaskId,
                ClearanceCategory = spec.Category,
                Title = spec.Title,
                Description = spec.Description,
                ResponsibleRole = spec.ResponsibleRole,
                ResponsibleUserId = spec.ResponsibleRole switch
                {
                    OnboardingResponsibleRole.Manager => managerUserId,
                    OnboardingResponsibleRole.Employee => employee.UserId,
                    _ => null,
                },
                // FR-2: due_date = last_working_day - offset_days (clamped so it never precedes today).
                DueDate = ClampDueDate(lwd.AddDays(-spec.OffsetDays)),
                Status = OnboardingTaskStatus.Pending,
                IsMandatory = spec.IsMandatory,
                SortOrder = sort++,
                IsDeleted = false,
            });
        }

        _dbContext.OffboardingInstances.Add(instance);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Offboarding initiated. InstanceId={InstanceId}, Employee={EmployeeId}, LWD={Lwd}, Reason={Reason}, " +
            "Tasks={TaskCount}, TenantId={TenantId}, By={User}",
            instance.Id, employee.Id, lwd, input.Reason, instance.Tasks.Count,
            _tenantContext.TenantId, _currentUser.Email);

        return Result<OffboardingInstanceDto>.Success(await BuildDtoAsync(instance, cancellationToken));
    }

    // ── AC-3 / FR-4: get ────────────────────────────────────────────────

    public async Task<Result<OffboardingInstanceDto>> GetByIdAsync(
        Guid offboardingInstanceId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OffboardingInstanceDto>.Failure("Tenant context is not resolved.", 400);

        var instance = await _dbContext.OffboardingInstances
            .AsNoTracking()
            .Include(o => o.Tasks)
            .FirstOrDefaultAsync(o => o.Id == offboardingInstanceId, cancellationToken);
        if (instance is null)
            return Result<OffboardingInstanceDto>.Failure("Offboarding not found.", 404, "offboarding_not_found");

        return Result<OffboardingInstanceDto>.Success(await BuildDtoAsync(instance, cancellationToken));
    }

    public async Task<Result<OffboardingInstanceDto>> GetByEmployeeAsync(
        Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OffboardingInstanceDto>.Failure("Tenant context is not resolved.", 400);

        // The most recent offboarding for the employee (prefer the in-progress one).
        var instance = await _dbContext.OffboardingInstances
            .AsNoTracking()
            .Include(o => o.Tasks)
            .Where(o => o.EmployeeId == employeeId)
            .OrderByDescending(o => o.Status == OffboardingStatus.InProgress)
            .ThenByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (instance is null)
            return Result<OffboardingInstanceDto>.Failure(
                "No offboarding found for this employee.", 404, "offboarding_not_found");

        return Result<OffboardingInstanceDto>.Success(await BuildDtoAsync(instance, cancellationToken));
    }

    // ── AC-3: record clearance ──────────────────────────────────────────

    public async Task<Result<OffboardingInstanceDto>> RecordClearanceAsync(
        RecordClearanceInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OffboardingInstanceDto>.Failure("Tenant context is not resolved.", 400);

        var (instance, task, error) = await LoadTaskAsync(input.TaskId, cancellationToken);
        if (error is not null)
            return Result<OffboardingInstanceDto>.Failure(error.Message, error.Status, error.Code);

        if (instance!.Status != OffboardingStatus.InProgress)
            return Result<OffboardingInstanceDto>.Failure(
                "This offboarding is already completed and cannot be changed.", 409, "offboarding_completed");

        // AC-3: record the decision; an approved clearance marks the task complete, pending_issues leaves it open.
        task!.ClearanceStatus = input.Status;
        task.Remarks = string.IsNullOrWhiteSpace(input.Remarks) ? null : input.Remarks.Trim();
        task.CompletedAt = DateTime.UtcNow;
        task.CompletedByUserId = _currentUser.UserId;
        task.Status = input.Status == ClearanceStatus.Approved
            ? OnboardingTaskStatus.Completed
            : OnboardingTaskStatus.InProgress;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Offboarding clearance recorded. TaskId={TaskId}, Offboarding={InstanceId}, Decision={Decision}, " +
            "TenantId={TenantId}, By={User}",
            task.Id, instance.Id, input.Status, _tenantContext.TenantId, _currentUser.Email);

        return Result<OffboardingInstanceDto>.Success(await BuildDtoAsync(instance, cancellationToken));
    }

    // ── AC-2 / BR-3: return asset ───────────────────────────────────────

    public async Task<Result<OffboardingInstanceDto>> ReturnAssetAsync(
        ReturnAssetInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OffboardingInstanceDto>.Failure("Tenant context is not resolved.", 400);

        var (instance, task, error) = await LoadTaskAsync(input.TaskId, cancellationToken);
        if (error is not null)
            return Result<OffboardingInstanceDto>.Failure(error.Message, error.Status, error.Code);

        if (instance!.Status != OffboardingStatus.InProgress)
            return Result<OffboardingInstanceDto>.Failure(
                "This offboarding is already completed and cannot be changed.", 409, "offboarding_completed");

        var asset = await _dbContext.Assets
            .FirstOrDefaultAsync(a => a.Id == input.AssetId, cancellationToken);
        if (asset is null)
            return Result<OffboardingInstanceDto>.Failure("Asset not found.", 404, "asset_not_found");

        // BR-3: the asset must currently be assigned (to this departing employee) to be returned.
        if (asset.Status != AssetStatus.Assigned)
            return Result<OffboardingInstanceDto>.Failure(
                $"Asset is '{asset.Status}' and cannot be returned (only assigned assets can be returned).",
                409, "asset_not_assigned");
        if (asset.AssignedEmployeeId != instance.EmployeeId)
            return Result<OffboardingInstanceDto>.Failure(
                "This asset is not assigned to the departing employee.", 409, "asset_employee_mismatch");

        // BR-3 / AC-2: flip the register — Available (returned to pool) or Disposed (written off).
        var beforeStatus = asset.Status;
        asset.Status = input.Disposed ? AssetStatus.Disposed : AssetStatus.Available;
        asset.Condition = input.Condition;
        asset.AssignedEmployeeId = null;

        // AC-2: complete the task + link the returned asset.
        task!.LinkedAssetId = asset.Id;
        task.Status = OnboardingTaskStatus.Completed;
        task.ClearanceStatus = ClearanceStatus.Approved;
        task.CompletedAt = DateTime.UtcNow;
        task.CompletedByUserId = _currentUser.UserId;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Offboarding asset return. AssetId={AssetId}, Tag={Tag}, StatusBefore={Before}, StatusAfter={After}, " +
            "TaskId={TaskId}, Offboarding={InstanceId}, TenantId={TenantId}, By={User}",
            asset.Id, asset.AssetTag, beforeStatus, asset.Status, task.Id, instance.Id,
            _tenantContext.TenantId, _currentUser.Email);

        return Result<OffboardingInstanceDto>.Success(await BuildDtoAsync(instance, cancellationToken));
    }

    // ── AC-4 / AC-5 / FR-5 / FR-6 / FR-7: complete ──────────────────────

    public async Task<Result<CompleteOffboardingResultDto>> CompleteAsync(
        Guid offboardingInstanceId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CompleteOffboardingResultDto>.Failure("Tenant context is not resolved.", 400);

        var instance = await _dbContext.OffboardingInstances
            .Include(o => o.Tasks)
            .FirstOrDefaultAsync(o => o.Id == offboardingInstanceId, cancellationToken);
        if (instance is null)
            return Result<CompleteOffboardingResultDto>.Failure(
                "Offboarding not found.", 404, "offboarding_not_found");

        // BR-6: completion is irreversible — a completed offboarding cannot be completed again.
        if (instance.Status == OffboardingStatus.Completed)
            return Result<CompleteOffboardingResultDto>.Failure(
                "This offboarding is already completed.", 409, "offboarding_completed");

        // AC-5 / BR-2: every mandatory task must be complete AND (where it is a clearance) approved.
        var pending = instance.Tasks
            .Where(t => !t.IsDeleted && t.IsMandatory)
            .Where(t => t.Status != OnboardingTaskStatus.Completed
                        || t.ClearanceStatus == ClearanceStatus.PendingIssues)
            .OrderBy(t => t.SortOrder)
            .Select(t => new PendingMandatoryItemDto
            {
                TaskId = t.Id,
                Title = t.Title,
                ClearanceCategory = t.ClearanceCategory,
                ClearanceCategoryName = t.ClearanceCategory.ToString(),
                Reason = t.ClearanceStatus == ClearanceStatus.PendingIssues
                    ? "clearance_not_approved"
                    : "not_completed",
            })
            .ToList();

        if (pending.Count > 0)
        {
            // AC-5: block — return the explicit pending list. The controller maps this to 409.
            return Result<CompleteOffboardingResultDto>.Success(new CompleteOffboardingResultDto
            {
                Completed = false,
                PendingItems = pending,
            });
        }

        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(e => e.Id == instance.EmployeeId, cancellationToken);
        if (employee is null)
            return Result<CompleteOffboardingResultDto>.Failure("Employee not found.", 404, "employee_not_found");

        // AC-4: terminate the employee (mapped from reason: resignation/contract-end/retirement → Terminated too,
        // since EmployeeStatus has no Resigned value — the closest terminal state).
        employee.Status = EmployeeStatus.Terminated;
        employee.IsActive = false;

        // FR-5: deactivate the linked user account so the employee cannot log in (existing user-active gate).
        if (employee.UserId.HasValue)
        {
            var user = await _dbContext.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == employee.UserId.Value, cancellationToken);
            if (user is not null)
                user.IsActive = false; // AuthService.LoginAsync / RefreshTokenAsync reject !IsActive.
        }

        instance.Status = OffboardingStatus.Completed;
        instance.CompletedAt = DateTime.UtcNow;
        instance.CompletedByUserId = _currentUser.UserId;

        // Persist the employee/user/instance state in one transaction (FR-9 audit columns stamped by interceptor).
        await _dbContext.SaveChangesAsync(cancellationToken);

        // FR-7: revoke the employee's active sessions. Refresh tokens are revoked via the existing auth path
        // (blocks re-auth + refresh); the ISessionRevoker seam sets the Redis JWT denylist "revoked-before"
        // cutoff (P3-2) so already-issued access tokens are rejected too (no-op when Redis is unconfigured).
        Guid? settlementRef = null;
        if (employee.UserId.HasValue)
        {
            await _authService.RevokeAllSessionsAsync(employee.UserId.Value, _tenantContext.TenantId, cancellationToken);
            await _sessionRevoker.RevokeActiveSessionsAsync(employee.UserId.Value, _tenantContext.TenantId, cancellationToken);
        }

        // FR-6 / BR-4: trigger F&F settlement in Payroll (offboarding only triggers; Payroll calculates).
        settlementRef = await _payrollFnF.TriggerFinalSettlementAsync(
            _tenantContext.TenantId, employee.Id, instance.Id, instance.LastWorkingDay, cancellationToken);

        _logger.LogInformation(
            "Offboarding completed. InstanceId={InstanceId}, Employee={EmployeeId}, UserDeactivated={UserDeactivated}, " +
            "FnFRef={SettlementRef}, TenantId={TenantId}, By={User}",
            instance.Id, employee.Id, employee.UserId.HasValue, settlementRef,
            _tenantContext.TenantId, _currentUser.Email);

        return Result<CompleteOffboardingResultDto>.Success(new CompleteOffboardingResultDto
        {
            Completed = true,
            Instance = await BuildDtoAsync(instance, cancellationToken),
            FinalSettlementRef = settlementRef,
        });
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private sealed record LoadError(string Message, int Status, string? Code);

    /// <summary>Loads a task with its (tracked) parent instance, tenant-scoped via the global filter.</summary>
    private async Task<(OffboardingInstance? Instance, OffboardingTaskInstance? Task, LoadError? Error)> LoadTaskAsync(
        Guid taskId, CancellationToken cancellationToken)
    {
        var task = await _dbContext.OffboardingTaskInstances
            .Include(t => t.OffboardingInstance!)
                .ThenInclude(o => o.Tasks)
            .FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted, cancellationToken);
        if (task is null || task.OffboardingInstance is null)
            return (null, null, new LoadError("Offboarding task not found.", 404, "task_not_found"));

        return (task.OffboardingInstance, task, null);
    }

    /// <summary>FR-2: due dates never precede today (a past LWD-offset is clamped to today).</summary>
    private static DateTime ClampDueDate(DateTime due)
    {
        // BUG-289 class: the result is persisted to the timestamptz due_date column, so both branches must be
        // UTC-kinded (.Date alone is Kind=Unspecified, which Npgsql rejects).
        var today = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        return due.Date < today ? today : DateTime.SpecifyKind(due.Date, DateTimeKind.Utc);
    }

    /// <summary>Maps a template task's free-text category/role to a clearance category for offboarding reuse.</summary>
    private static ClearanceCategory MapCategory(string? category, OnboardingResponsibleRole role)
    {
        if (!string.IsNullOrWhiteSpace(category)
            && Enum.TryParse<ClearanceCategory>(category.Trim(), ignoreCase: true, out var parsed))
            return parsed;

        return role switch
        {
            OnboardingResponsibleRole.IT => ClearanceCategory.IT,
            OnboardingResponsibleRole.Manager => ClearanceCategory.Manager,
            OnboardingResponsibleRole.Employee => ClearanceCategory.Employee,
            _ => ClearanceCategory.HR,
        };
    }

    private sealed record TaskSpec(
        Guid? SourceTemplateTaskId,
        ClearanceCategory Category,
        string Title,
        string? Description,
        OnboardingResponsibleRole ResponsibleRole,
        int OffsetDays,
        bool IsMandatory);

    /// <summary>
    /// FR-3: the built-in DEFAULT clearance task set used when no offboarding template is supplied. Covers IT
    /// (access revocation, asset return), Finance (advances/loans/F&amp;F), Admin (ID card/parking/keys), Manager
    /// (knowledge transfer/handover), Employee. The asset-return + finance clearance are mandatory.
    /// Offsets are days BEFORE the last working day (due_date = LWD - offset).
    /// </summary>
    private static List<TaskSpec> DefaultClearanceTaskSpecs() =>
    [
        new(null, ClearanceCategory.IT, "Revoke system access",
            "Disable accounts, email, VPN, and application access.", OnboardingResponsibleRole.IT, 0, false),
        new(null, ClearanceCategory.IT, "Return IT assets",
            "Recover laptop, phone, and other issued IT equipment.", OnboardingResponsibleRole.IT, 0, true),
        new(null, ClearanceCategory.Finance, "Settle advances and loans",
            "Clear outstanding advances/loans and confirm F&F readiness.", OnboardingResponsibleRole.HR, 1, true),
        new(null, ClearanceCategory.Admin, "Return ID card, parking pass, and keys",
            "Collect ID card, access badge, parking pass, and physical keys.", OnboardingResponsibleRole.HR, 0, false),
        new(null, ClearanceCategory.Manager, "Knowledge transfer and handover",
            "Complete knowledge transfer and document handover.", OnboardingResponsibleRole.Manager, 3, false),
        new(null, ClearanceCategory.Employee, "Personal handover items",
            "Return company property in your possession and confirm exit details.", OnboardingResponsibleRole.Employee, 1, false),
    ];

    /// <summary>
    /// AC-3/FR-4: builds the dashboard DTO — tasks grouped by clearance department, per-department status,
    /// the overall clearance summary, and progress. Resolves the employee name via a separate lookup (avoids
    /// projecting through a filtered required navigation, which empties results on the InMemory provider).
    /// </summary>
    private async Task<OffboardingInstanceDto> BuildDtoAsync(
        OffboardingInstance instance, CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == instance.EmployeeId, cancellationToken);
        var employeeName = employee is null ? string.Empty : $"{employee.FirstName} {employee.LastName}".Trim();

        var tasks = instance.Tasks.Where(t => !t.IsDeleted).OrderBy(t => t.SortOrder).ToList();
        var total = tasks.Count;
        var completed = tasks.Count(t => t.Status == OnboardingTaskStatus.Completed);

        var departments = tasks
            .GroupBy(t => t.ClearanceCategory)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var groupTasks = g.OrderBy(t => t.SortOrder).ToList();
                var approved = groupTasks.Count(t => t.ClearanceStatus == ClearanceStatus.Approved);
                var issues = groupTasks.Count(t => t.ClearanceStatus == ClearanceStatus.PendingIssues);
                var undecided = groupTasks.Count(t => t.ClearanceStatus is null);

                // FR-4 traffic light: cleared (all approved, mandatory done) / issues / pending.
                string deptStatus;
                if (issues > 0)
                    deptStatus = "issues";
                else if (undecided == 0 && groupTasks.All(t => t.Status == OnboardingTaskStatus.Completed))
                    deptStatus = "cleared";
                else
                    deptStatus = "pending";

                return new DepartmentClearanceDto
                {
                    ClearanceCategory = g.Key,
                    ClearanceCategoryName = g.Key.ToString(),
                    Status = deptStatus,
                    TotalTasks = groupTasks.Count,
                    ApprovedTasks = approved,
                    PendingIssueTasks = issues,
                    UndecidedTasks = undecided,
                    Tasks = groupTasks.Select(ToTaskDto).ToList(),
                };
            })
            .ToList();

        var clearedDepartments = departments.Count(d => d.Status == "cleared");
        var fullyCleared = departments.Count > 0 && departments.All(d => d.Status == "cleared");

        return new OffboardingInstanceDto
        {
            Id = instance.Id,
            EmployeeId = instance.EmployeeId,
            EmployeeName = employeeName,
            TemplateId = instance.TemplateId,
            TemplateName = instance.TemplateName,
            LastWorkingDay = instance.LastWorkingDay,
            Reason = instance.Reason,
            ReasonName = instance.Reason.ToString(),
            Notes = instance.Notes,
            Status = instance.Status,
            StatusName = instance.Status.ToString(),
            InitiatedByUserId = instance.InitiatedByUserId,
            CompletedAt = instance.CompletedAt,
            ProgressPercent = total == 0 ? 0 : (int)Math.Round(completed * 100.0 / total, MidpointRounding.AwayFromZero),
            TotalTasks = total,
            CompletedTasks = completed,
            ClearanceSummary = new ClearanceSummaryDto
            {
                FullyCleared = fullyCleared,
                TotalDepartments = departments.Count,
                ClearedDepartments = clearedDepartments,
                PendingDepartments = departments.Count - clearedDepartments,
            },
            Departments = departments,
            CreatedAt = instance.CreatedAt,
            UpdatedAt = instance.UpdatedAt,
        };
    }

    private static OffboardingTaskDto ToTaskDto(OffboardingTaskInstance t) => new()
    {
        Id = t.Id,
        ClearanceCategory = t.ClearanceCategory,
        ClearanceCategoryName = t.ClearanceCategory.ToString(),
        Title = t.Title,
        Description = t.Description,
        ResponsibleRole = t.ResponsibleRole,
        ResponsibleRoleName = t.ResponsibleRole.ToString(),
        ResponsibleUserId = t.ResponsibleUserId,
        DueDate = t.DueDate,
        Status = t.Status,
        StatusName = t.Status.ToString(),
        IsMandatory = t.IsMandatory,
        SortOrder = t.SortOrder,
        ClearanceStatus = t.ClearanceStatus switch
        {
            HRM.Domain.Enums.ClearanceStatus.Approved => "approved",
            HRM.Domain.Enums.ClearanceStatus.PendingIssues => "pending_issues",
            _ => null,
        },
        Remarks = t.Remarks,
        LinkedAssetId = t.LinkedAssetId,
        CompletedAt = t.CompletedAt,
    };
}
