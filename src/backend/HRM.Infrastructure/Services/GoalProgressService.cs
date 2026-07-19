using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Goal-tracking / progress-update service (US-PRF-009). Every read/write is tenant-scoped via ITenantContext +
/// the EF global query filter (NFR-2). Enforces: append-only progress updates with NO update/delete path
/// (NFR-3/FR-3); the active-cycle window gate on posting (BR-1, reusing the AppraisalCycle window helpers);
/// BR-2 (100% auto-sets Completed, overridable); BR-3 (Blocked notifies manager + HR); the FR-4 weighted overall
/// completion (delegating to the pure <see cref="GoalProgressCalculator"/>); the AC-4 manager team summary scoped
/// to direct reports (Review.Team) / all (Review.All); the FR-8 manager/HR comment thread; and BR-5 visibility
/// (employee / manager / HR only).
///
/// NOTIFICATIONS (FR-5/BR-3): dispatched through the log-only <see cref="IPerformanceNotificationService"/> seam.
/// Real-time delivery (SignalR) is NOT set up in this codebase; it is a deferred hook tied to US-NTF-001 — the
/// notification call sites are the single integration point when that lands.
/// </summary>
public sealed class GoalProgressService : IGoalProgressService
{
    private const int MaxAttachments = 3;                      // FR-2: ≤3 files per update.
    private const long MaxAttachmentBytes = 10L * 1024 * 1024; // FR-2: ≤10MB each.
    private const int MaxNotes = 2000;                         // FR-2.
    private const int MaxCommentBody = 500;                    // FR-8/§10.

    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IPerformanceNotificationService _notifications;
    private readonly ILogger<GoalProgressService> _logger;

    public GoalProgressService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IPerformanceNotificationService notifications,
        ILogger<GoalProgressService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _notifications = notifications;
        _logger = logger;
    }

    // ── My Goals (AC-1) ─────────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<MyGoalProgressDto>>> GetMyGoalsAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<MyGoalProgressDto>>.Failure("Tenant context is not resolved.", 400);

        var me = await GetCurrentEmployeeAsync(cancellationToken);
        if (me is null)
            return Result<IReadOnlyList<MyGoalProgressDto>>.Failure(
                "The current user is not linked to an employee record.", 403, "no_employee_record");

        var rows = await BuildGoalProgressRowsAsync(me.Id, cancellationToken);
        return Result<IReadOnlyList<MyGoalProgressDto>>.Success(rows);
    }

    // ── Add progress update (AC-2/FR-1/FR-2, BR-1/BR-2/BR-3) ────────────

    public async Task<Result<GoalTimelineDto>> AddProgressUpdateAsync(
        AddGoalProgressInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<GoalTimelineDto>.Failure("Tenant context is not resolved.", 400);

        var me = await GetCurrentEmployeeAsync(cancellationToken);
        if (me is null)
            return Result<GoalTimelineDto>.Failure(
                "The current user is not linked to an employee record.", 403, "no_employee_record");

        var goal = await _dbContext.Goals.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == input.GoalId, cancellationToken);
        if (goal is null)
            return Result<GoalTimelineDto>.Failure("Goal not found.", 404, "goal_not_found");

        // BR-5: only the goal's own employee may post progress.
        if (goal.EmployeeId != me.Id)
            return Result<GoalTimelineDto>.Failure(
                "You can only post progress on your own goals.", 403, "not_goal_owner");

        // BR-1: updates only during the active cycle window (goal-setting close → review start).
        var cycle = await _dbContext.AppraisalCycles.AsNoTracking()
            .Include(c => c.Phases)
            .FirstOrDefaultAsync(c => c.Id == goal.CycleId, cancellationToken);
        if (cycle is null)
            return Result<GoalTimelineDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        if (!IsTrackingWindowOpen(cycle, DateTime.UtcNow))
            return Result<GoalTimelineDto>.Failure(
                "Progress updates can only be posted during the active cycle period.", 409, "tracking_window_closed");

        if (input.ProgressPct is < 0 or > 100)
            return Result<GoalTimelineDto>.Failure("Progress percentage must be between 0 and 100.", 422, "invalid_progress");

        if (input.Notes is { Length: > MaxNotes })
            return Result<GoalTimelineDto>.Failure($"Notes cannot exceed {MaxNotes} characters.", 422, "notes_too_long");

        var attachments = input.Attachments ?? [];
        if (attachments.Count > MaxAttachments)
            return Result<GoalTimelineDto>.Failure($"A maximum of {MaxAttachments} attachments is allowed.", 422, "too_many_attachments");
        if (attachments.Any(a => a.SizeBytes > MaxAttachmentBytes))
            return Result<GoalTimelineDto>.Failure("Each attachment must be 10MB or smaller.", 422, "attachment_too_large");

        // BR-2 (ISSUE-140): 100% auto-sets Completed ONLY from the "no explicit status" default. The request's
        // Status is a required non-nullable enum, so an omitted status deserializes to NotStarted (the 0 default) —
        // that is the only value auto-completed at 100%. An EXPLICIT status (InProgress "done, pending sign-off",
        // or AtRisk/Blocked) is the employee's overridable choice (BR-2) and is honoured verbatim, never forced.
        var status = input.Status;
        if (input.ProgressPct == 100 && status is GoalProgressStatus.NotStarted)
            status = GoalProgressStatus.Completed;

        var now = DateTime.UtcNow;
        var update = new GoalProgressUpdate
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            GoalId = goal.Id,
            EmployeeId = me.Id,
            ProgressPct = input.ProgressPct,
            Status = status,
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            CreatedAtUtc = now,
            IsDeleted = false,
        };

        foreach (var a in attachments)
        {
            update.Attachments.Add(new GoalProgressAttachment
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                ProgressUpdateId = update.Id,
                FileName = a.FileName.Trim(),
                StorageKey = a.StorageKey.Trim(),
                ContentType = string.IsNullOrWhiteSpace(a.ContentType) ? null : a.ContentType.Trim(),
                SizeBytes = a.SizeBytes,
                IsDeleted = false,
            });
        }

        _dbContext.GoalProgressUpdates.Add(update);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Goal progress update posted. GoalId={GoalId}, EmployeeId={EmployeeId}, Progress={Progress}, " +
            "Status={Status}, TenantId={TenantId}",
            goal.Id, me.Id, update.ProgressPct, update.Status, _tenantContext.TenantId);

        // FR-5: notify the manager on every posted update. SignalR/real-time is a deferred hook (US-NTF-001).
        var managerId = await ResolveManagerIdAsync(goal.EmployeeId, cancellationToken);
        if (managerId is { } mgr)
            await _notifications.NotifyGoalProgressAsync(
                "goal-progress-updated", goal.Id, goal.EmployeeId, mgr, $"{update.ProgressPct}% / {update.Status}", cancellationToken);

        // BR-3: a Blocked update additionally notifies the manager + HR.
        if (status == GoalProgressStatus.Blocked)
        {
            if (managerId is { } bmgr)
                await _notifications.NotifyGoalProgressAsync("goal-blocked", goal.Id, goal.EmployeeId, bmgr, cancellationToken: cancellationToken);
            await NotifyHrAsync("goal-blocked", goal.Id, goal.EmployeeId, cancellationToken);
        }

        return await BuildTimelineResultAsync(goal.Id, cancellationToken);
    }

    // ── Goal timeline (AC-3/FR-3/FR-8) ──────────────────────────────────

    public async Task<Result<GoalTimelineDto>> GetGoalTimelineAsync(Guid goalId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<GoalTimelineDto>.Failure("Tenant context is not resolved.", 400);

        var goal = await _dbContext.Goals.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == goalId, cancellationToken);
        if (goal is null)
            return Result<GoalTimelineDto>.Failure("Goal not found.", 404, "goal_not_found");

        var authz = await AuthorizeGoalViewAsync(goal, cancellationToken);
        if (authz.IsFailure)
            return Result<GoalTimelineDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        return await BuildTimelineResultAsync(goalId, cancellationToken);
    }

    // ── Manager team summary (AC-4/FR-4) ────────────────────────────────

    public async Task<Result<IReadOnlyList<TeamGoalProgressRowDto>>> GetTeamSummaryAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<TeamGoalProgressRowDto>>.Failure("Tenant context is not resolved.", 400);

        var isHr = _currentUser.Permissions.Contains(PermissionCatalog.Performance.ReviewAll);
        var isManager = _currentUser.Permissions.Contains(PermissionCatalog.Performance.ReviewTeam);
        if (!isHr && !isManager)
            return Result<IReadOnlyList<TeamGoalProgressRowDto>>.Failure(
                "You do not have permission to view team goal progress.", 403, "forbidden");

        var me = await GetCurrentEmployeeAsync(cancellationToken);

        // Scope (BR-5): Review.All ⇒ all in-tenant employees; else Review.Team ⇒ the caller's direct reports.
        var employeesQuery = _dbContext.Employees.AsNoTracking();
        List<Employee> employees;
        if (isHr)
            employees = await employeesQuery.ToListAsync(cancellationToken);
        else
            employees = me is null
                ? []
                : await employeesQuery.Where(e => e.ReportsToEmployeeId == me.Id).ToListAsync(cancellationToken);

        if (employees.Count == 0)
            return Result<IReadOnlyList<TeamGoalProgressRowDto>>.Success([]);

        var empIds = employees.Select(e => e.Id).ToList();

        // Active goals for the in-scope employees.
        var goals = await _dbContext.Goals.AsNoTracking()
            .Where(g => empIds.Contains(g.EmployeeId)
                && (g.Status == GoalStatus.Submitted || g.Status == GoalStatus.Acknowledged
                    || g.Status == GoalStatus.Finalized))
            .ToListAsync(cancellationToken);

        var goalIds = goals.Select(g => g.Id).ToList();
        var updates = await _dbContext.GoalProgressUpdates.AsNoTracking()
            .Where(u => goalIds.Contains(u.GoalId))
            .ToListAsync(cancellationToken);

        var cycleStarts = await GoalCycleStartsAsync(goals, cancellationToken);
        var nudgeDays = await GetTenantNudgeDaysAsync(cancellationToken);
        var now = DateTime.UtcNow;

        var rows = new List<TeamGoalProgressRowDto>(employees.Count);
        foreach (var emp in employees)
        {
            var empGoals = goals.Where(g => g.EmployeeId == emp.Id).ToList();
            var weighted = new List<(int Weight, int LatestProgressPct)>(empGoals.Count);
            var atRisk = 0;
            var needsAttention = 0;
            DateTime? lastUpdated = null;

            foreach (var g in empGoals)
            {
                var latest = updates.Where(u => u.GoalId == g.Id).OrderByDescending(u => u.CreatedAtUtc).FirstOrDefault();
                weighted.Add((g.Weight, latest?.ProgressPct ?? 0));
                if (latest is not null && latest.CreatedAtUtc > (lastUpdated ?? DateTime.MinValue))
                    lastUpdated = latest.CreatedAtUtc;
                if (latest?.Status is GoalProgressStatus.AtRisk or GoalProgressStatus.Blocked)
                    atRisk++;

                cycleStarts.TryGetValue(g.CycleId, out var since);
                if (GoalProgressCalculator.IsStale(latest?.CreatedAtUtc, since, nudgeDays, now))
                    needsAttention++;
            }

            rows.Add(new TeamGoalProgressRowDto
            {
                EmployeeId = emp.Id,
                EmployeeName = FullName(emp),
                EmployeeNo = emp.EmployeeNo,
                GoalCount = empGoals.Count,
                OverallCompletionPct = GoalProgressCalculator.WeightedOverallCompletion(weighted),
                AtRiskGoalCount = atRisk,
                NeedsAttentionGoalCount = needsAttention,
                LastUpdatedAt = lastUpdated,
            });
        }

        // Stable order: most-recently-active first, then by name.
        rows = rows
            .OrderByDescending(r => r.LastUpdatedAt ?? DateTime.MinValue)
            .ThenBy(r => r.EmployeeName)
            .ToList();

        return Result<IReadOnlyList<TeamGoalProgressRowDto>>.Success(rows);
    }

    public async Task<Result<IReadOnlyList<MyGoalProgressDto>>> GetEmployeeGoalsAsync(
        Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<MyGoalProgressDto>>.Failure("Tenant context is not resolved.", 400);

        var authz = await AuthorizeManagerScopeAsync(employeeId, cancellationToken);
        if (authz.IsFailure)
            return Result<IReadOnlyList<MyGoalProgressDto>>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        var rows = await BuildGoalProgressRowsAsync(employeeId, cancellationToken);
        return Result<IReadOnlyList<MyGoalProgressDto>>.Success(rows);
    }

    // ── Manager comment (FR-8) ──────────────────────────────────────────

    public async Task<Result<GoalTimelineDto>> AddCommentAsync(AddGoalCommentInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<GoalTimelineDto>.Failure("Tenant context is not resolved.", 400);

        if (string.IsNullOrWhiteSpace(input.Body))
            return Result<GoalTimelineDto>.Failure("Comment body is required.", 422, "body_required");
        if (input.Body.Length > MaxCommentBody)
            return Result<GoalTimelineDto>.Failure($"Comment cannot exceed {MaxCommentBody} characters.", 422, "body_too_long");

        var goal = await _dbContext.Goals.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == input.GoalId, cancellationToken);
        if (goal is null)
            return Result<GoalTimelineDto>.Failure("Goal not found.", 404, "goal_not_found");

        var author = await GetCurrentEmployeeAsync(cancellationToken);
        if (author is null)
            return Result<GoalTimelineDto>.Failure("The current user is not linked to an employee record.", 403, "no_employee_record");

        // FR-8 (ISSUE-141): the thread is two-way but SCOPED. The goal OWNER may reply on their own goal; anyone
        // else must be the owner's manager or HR. A Read.Self caller who is not the owner falls through to the
        // manager/HR scope check and (lacking Review.Team/Review.All) is refused — they cannot comment on a goal
        // they do not own.
        if (goal.EmployeeId != author.Id)
        {
            var authz = await AuthorizeManagerScopeAsync(goal.EmployeeId, cancellationToken);
            if (authz.IsFailure)
                return Result<GoalTimelineDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);
        }

        // Validate the anchored update belongs to this goal, when supplied.
        if (input.ProgressUpdateId is { } updateId)
        {
            var belongs = await _dbContext.GoalProgressUpdates.AsNoTracking()
                .AnyAsync(u => u.Id == updateId && u.GoalId == goal.Id, cancellationToken);
            if (!belongs)
                return Result<GoalTimelineDto>.Failure("The referenced progress update does not belong to this goal.", 422, "invalid_update_ref");
        }

        var comment = new GoalComment
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            GoalId = goal.Id,
            ProgressUpdateId = input.ProgressUpdateId,
            AuthorEmployeeId = author.Id,
            AuthorName = FullName(author),
            Body = input.Body.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            IsDeleted = false,
        };
        _dbContext.GoalComments.Add(comment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Goal comment posted. GoalId={GoalId}, AuthorEmployeeId={AuthorId}, TenantId={TenantId}",
            goal.Id, author.Id, _tenantContext.TenantId);

        // ISSUE-297: the comment thread is two-way (ISSUE-141), so route the notification to the COUNTERPARTY,
        // never back to the author about their own comment. When the goal owner replies, notify their manager
        // (if one is on file); when a manager/HR comments, notify the owner (the original behaviour). Subject of
        // the message (`employeeId`, 3rd arg) stays the goal owner so the payload renders the owner's name.
        var recipientId = author.Id == goal.EmployeeId
            ? await ResolveManagerIdAsync(goal.EmployeeId, cancellationToken)
            : goal.EmployeeId;
        if (recipientId is { } recipient)
        {
            await _notifications.NotifyGoalProgressAsync(
                "goal-comment-added", goal.Id, goal.EmployeeId, recipient, cancellationToken: cancellationToken);
        }

        return await BuildTimelineResultAsync(goal.Id, cancellationToken);
    }

    // ── Shared builders ─────────────────────────────────────────────────

    private async Task<IReadOnlyList<MyGoalProgressDto>> BuildGoalProgressRowsAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        // Active goals = Submitted/Acknowledged/Finalized (tracking happens after goal-setting closes;
        // BUG-056: a finalized/locked set is still tracked — finalize is the sign-off, not the end of tracking).
        var goals = await _dbContext.Goals.AsNoTracking()
            .Where(g => g.EmployeeId == employeeId
                && (g.Status == GoalStatus.Submitted || g.Status == GoalStatus.Acknowledged
                    || g.Status == GoalStatus.Finalized))
            .ToListAsync(cancellationToken);

        if (goals.Count == 0)
            return [];

        var goalIds = goals.Select(g => g.Id).ToList();
        var updates = await _dbContext.GoalProgressUpdates.AsNoTracking()
            .Where(u => goalIds.Contains(u.GoalId))
            .ToListAsync(cancellationToken);

        var cycleStarts = await GoalCycleStartsAsync(goals, cancellationToken);
        var nudgeDays = await GetTenantNudgeDaysAsync(cancellationToken);
        var now = DateTime.UtcNow;

        return goals
            .OrderBy(g => g.DueDate)
            .Select(g =>
            {
                var goalUpdates = updates.Where(u => u.GoalId == g.Id).OrderByDescending(u => u.CreatedAtUtc).ToList();
                var latest = goalUpdates.FirstOrDefault();
                var currentStatus = latest?.Status ?? GoalProgressStatus.NotStarted;
                cycleStarts.TryGetValue(g.CycleId, out var since);
                return new MyGoalProgressDto
                {
                    GoalId = g.Id,
                    CycleId = g.CycleId,
                    Title = g.Title,
                    Description = g.Description,
                    Category = g.Category,
                    CategoryName = g.Category.ToString(),
                    Weight = g.Weight,
                    TargetValue = g.TargetValue,
                    MeasurementUnit = g.MeasurementUnit,
                    DueDate = g.DueDate,
                    CurrentProgressPct = latest?.ProgressPct ?? 0,
                    CurrentStatus = currentStatus,
                    CurrentStatusName = currentStatus.ToString(),
                    LastUpdatedAt = latest?.CreatedAtUtc,
                    UpdateCount = goalUpdates.Count,
                    NeedsAttention = GoalProgressCalculator.IsStale(latest?.CreatedAtUtc, since, nudgeDays, now),
                };
            })
            .ToList();
    }

    private async Task<Result<GoalTimelineDto>> BuildTimelineResultAsync(Guid goalId, CancellationToken cancellationToken)
    {
        var goal = await _dbContext.Goals.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == goalId, cancellationToken);
        if (goal is null)
            return Result<GoalTimelineDto>.Failure("Goal not found.", 404, "goal_not_found");

        var updates = await _dbContext.GoalProgressUpdates.AsNoTracking()
            .Include(u => u.Attachments)
            .Where(u => u.GoalId == goalId)
            .OrderBy(u => u.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var comments = await _dbContext.GoalComments.AsNoTracking()
            .Where(c => c.GoalId == goalId)
            .OrderBy(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var latest = updates.LastOrDefault();
        var currentStatus = latest?.Status ?? GoalProgressStatus.NotStarted;

        var dto = new GoalTimelineDto
        {
            GoalId = goal.Id,
            Title = goal.Title,
            CurrentProgressPct = latest?.ProgressPct ?? 0,
            CurrentStatus = currentStatus,
            CurrentStatusName = currentStatus.ToString(),
            Updates = updates.Select(u => new GoalProgressUpdateDto
            {
                Id = u.Id,
                GoalId = u.GoalId,
                EmployeeId = u.EmployeeId,
                ProgressPct = u.ProgressPct,
                Status = u.Status,
                StatusName = u.Status.ToString(),
                Notes = u.Notes,
                CreatedAt = u.CreatedAtUtc,
                Attachments = u.Attachments.Where(a => !a.IsDeleted).Select(a => new GoalProgressAttachmentDto
                {
                    Id = a.Id,
                    FileName = a.FileName,
                    ContentType = a.ContentType,
                    SizeBytes = a.SizeBytes,
                }).ToList(),
            }).ToList(),
            Comments = comments.Select(c => new GoalCommentDto
            {
                Id = c.Id,
                GoalId = c.GoalId,
                ProgressUpdateId = c.ProgressUpdateId,
                AuthorEmployeeId = c.AuthorEmployeeId,
                AuthorName = c.AuthorName,
                Body = c.Body,
                CreatedAt = c.CreatedAtUtc,
            }).ToList(),
        };

        return Result<GoalTimelineDto>.Success(dto);
    }

    // ── Authorization (BR-5 visibility / scope) ─────────────────────────

    /// <summary>BR-5: a goal's progress is viewable by the employee, their manager or HR — anyone else 403.</summary>
    private async Task<Result> AuthorizeGoalViewAsync(Goal goal, CancellationToken cancellationToken)
    {
        if (_currentUser.Permissions.Contains(PermissionCatalog.Performance.ReviewAll))
            return Result.Success();

        var me = await GetCurrentEmployeeAsync(cancellationToken);
        if (me is null)
            return Result.Failure("You do not have permission to view this goal's progress.", 403, "goal_forbidden");

        if (goal.EmployeeId == me.Id)
            return Result.Success();

        if (_currentUser.Permissions.Contains(PermissionCatalog.Performance.ReviewTeam))
        {
            var managerId = await ResolveManagerIdAsync(goal.EmployeeId, cancellationToken);
            if (managerId == me.Id)
                return Result.Success();
        }

        return Result.Failure("You do not have permission to view this goal's progress.", 403, "goal_forbidden");
    }

    /// <summary>
    /// Manager-scope authorization (AC-4 drill-down / FR-8 comment): HR (Review.All) for any in-tenant employee, or
    /// a manager (Review.Team) who is the target employee's direct manager. Anyone else → 403.
    /// </summary>
    private async Task<Result> AuthorizeManagerScopeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var permissions = _currentUser.Permissions;
        if (permissions.Contains(PermissionCatalog.Performance.ReviewAll))
            return Result.Success();

        if (!permissions.Contains(PermissionCatalog.Performance.ReviewTeam))
            return Result.Failure("You do not have permission to view this employee's goal progress.", 403, "forbidden");

        var me = await GetCurrentEmployeeAsync(cancellationToken);
        if (me is null)
            return Result.Failure("The current user is not linked to an employee record.", 403, "no_employee_record");

        var managerId = await ResolveManagerIdAsync(employeeId, cancellationToken);
        if (managerId != me.Id)
            return Result.Failure("You can only view goal progress for your direct reports.", 403, "not_direct_report");

        return Result.Success();
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// The tracking window (BR-1): the active cycle period between goal-setting close and review start. Reuses the
    /// AppraisalCycle window helpers — open once goal-setting has CLOSED and (if a self-assessment/review phase
    /// exists) before review starts; while the cycle is Active.
    /// </summary>
    private static bool IsTrackingWindowOpen(AppraisalCycle cycle, DateTime nowUtc)
    {
        if (cycle.Status != AppraisalCycleStatus.Active)
            return false;

        // After goal-setting has closed.
        if (nowUtc < cycle.GoalSettingEnd)
            return false;

        // Before the review (self-assessment) phase starts, when one is configured. A zero SelfAssessmentStart
        // (no later phase configured) means tracking stays open for the remainder of the active cycle.
        if (cycle.SelfAssessmentStart != default && nowUtc >= cycle.SelfAssessmentStart)
            return false;

        return nowUtc <= cycle.EndDate || cycle.EndDate == default;
    }

    private async Task<Guid?> ResolveManagerIdAsync(Guid employeeId, CancellationToken cancellationToken)
        => await _dbContext.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => e.ReportsToEmployeeId)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<Dictionary<Guid, DateTime>> GoalCycleStartsAsync(IEnumerable<Goal> goals, CancellationToken cancellationToken)
    {
        var cycleIds = goals.Select(g => g.CycleId).Distinct().ToList();
        if (cycleIds.Count == 0)
            return [];
        return await _dbContext.AppraisalCycles.AsNoTracking()
            .Where(c => cycleIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.GoalSettingEnd == default ? c.StartDate : c.GoalSettingEnd, cancellationToken);
    }

    private async Task<int> GetTenantNudgeDaysAsync(CancellationToken cancellationToken)
        => await _dbContext.Tenants.AsNoTracking().IgnoreQueryFilters()
            .Where(t => t.Id == _tenantContext.TenantId)
            .Select(t => t.StaleGoalNudgeDays)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task NotifyHrAsync(string eventType, Guid goalId, Guid employeeId, CancellationToken cancellationToken)
    {
        // HR delivery is a tenant-broadcast in the real (US-NTF) impl. Through the log-only seam we dispatch a
        // single HR-targeted event (recipient null = HR audience) so the BR-3 path is observable + testable.
        await _notifications.NotifyGoalProgressAsync(eventType, goalId, employeeId, null, "hr", cancellationToken);
    }

    private Task<Employee?> GetCurrentEmployeeAsync(CancellationToken cancellationToken)
        => _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);

    private static string FullName(Employee e) => $"{e.FirstName} {e.LastName}".Trim();
}
