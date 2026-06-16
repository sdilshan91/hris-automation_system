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
/// Goal-setting service (US-PRF-001). Every query is tenant-scoped via ITenantContext + the EF global
/// query filter (NFR-2). Authorization (BR-4), the goal-setting-window gate (BR-1/AC-5), the 1-10 count
/// (BR-2) and the ≤100% weight rule (FR-3/AC-3) are all enforced here so they hold for any entry point.
/// Audit of create/update/delete (FR-6) is provided by the AuditInterceptor (CreatedBy/UpdatedBy stamping
/// on SaveChanges) plus structured Serilog entries. Optimistic concurrency (NFR-4) rides the xmin token.
/// </summary>
public sealed class GoalService : IGoalService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IPerformanceNotificationService _notifications;
    private readonly ILogger<GoalService> _logger;

    private const int MaxGoalsPerEmployee = 10; // BR-2
    private const int RequiredTotalWeight = 100; // FR-3/AC-3

    public GoalService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IPerformanceNotificationService notifications,
        ILogger<GoalService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<Result<GoalDto>> CreateAsync(GoalInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<GoalDto>.Failure("Tenant context is not resolved.", 400);

        var authz = await AuthorizeForEmployeeAsync(input.EmployeeId, cancellationToken);
        if (authz.IsFailure)
            return Result<GoalDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        var cycleResult = await GetOpenCycleAsync(input.CycleId, cancellationToken);
        if (cycleResult.IsFailure)
            return Result<GoalDto>.Failure(cycleResult.Error!, cycleResult.StatusCode ?? 400, cycleResult.ErrorCode);

        // FR-4: a parent goal (cascading) must exist in-tenant (global filter ⇒ found row is in-tenant).
        if (input.ParentGoalId is { } parentId &&
            !await _dbContext.Goals.AnyAsync(g => g.Id == parentId, cancellationToken))
            return Result<GoalDto>.Failure("The specified parent goal does not exist.", 400, "parent_goal_not_found");

        var existing = await _dbContext.Goals
            .Where(g => g.EmployeeId == input.EmployeeId && g.CycleId == input.CycleId)
            .ToListAsync(cancellationToken);

        // BR-2: max 10 goals per employee per cycle.
        if (existing.Count >= MaxGoalsPerEmployee)
            return Result<GoalDto>.Failure(
                $"An employee can have at most {MaxGoalsPerEmployee} goals per cycle.", 409, "goal_limit_reached");

        // FR-3/AC-3: the running total must never exceed 100% (the 105% case). The "must equal exactly
        // 100%" check is surfaced via the employee-goals/dashboard TotalWeight so the UI can block submit.
        var newTotal = existing.Sum(g => g.Weight) + input.Weight;
        if (newTotal > RequiredTotalWeight)
            return Result<GoalDto>.Failure(
                $"Goal weights for this employee would total {newTotal}%, which exceeds 100%.",
                422, "weight_exceeds_100");

        var goal = new Goal
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            CycleId = input.CycleId,
            EmployeeId = input.EmployeeId,
            Title = input.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            Category = input.Category,
            Weight = input.Weight,
            TargetValue = input.TargetValue.Trim(),
            MeasurementUnit = input.MeasurementUnit.Trim(),
            DueDate = input.DueDate,
            ParentGoalId = input.ParentGoalId,
            Status = GoalStatus.Draft,
            IsDeleted = false,
        };

        _dbContext.Goals.Add(goal);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Goal created. Id={GoalId}, EmployeeId={EmployeeId}, CycleId={CycleId}, Weight={Weight}, TenantId={TenantId}, By={User}",
            goal.Id, goal.EmployeeId, goal.CycleId, goal.Weight, _tenantContext.TenantId, _currentUser.Email);

        await _notifications.NotifyGoalChangedAsync("goal-assigned", goal.Id, goal.EmployeeId, goal.CycleId, cancellationToken);

        return Result<GoalDto>.Success(ToDto(goal));
    }

    public async Task<Result<GoalDto>> UpdateAsync(Guid goalId, GoalInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<GoalDto>.Failure("Tenant context is not resolved.", 400);

        var goal = await _dbContext.Goals.FirstOrDefaultAsync(g => g.Id == goalId, cancellationToken);
        if (goal is null)
            return Result<GoalDto>.Failure("Goal not found.", 404);

        var authz = await AuthorizeForEmployeeAsync(goal.EmployeeId, cancellationToken);
        if (authz.IsFailure)
            return Result<GoalDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        var cycleResult = await GetOpenCycleAsync(goal.CycleId, cancellationToken);
        if (cycleResult.IsFailure)
            return Result<GoalDto>.Failure(cycleResult.Error!, cycleResult.StatusCode ?? 400, cycleResult.ErrorCode);

        if (input.ParentGoalId is { } parentId && parentId != goal.ParentGoalId &&
            (parentId == goal.Id || !await _dbContext.Goals.AnyAsync(g => g.Id == parentId, cancellationToken)))
            return Result<GoalDto>.Failure("The specified parent goal does not exist.", 400, "parent_goal_not_found");

        // FR-3/AC-3: re-check the ≤100% total with this goal's NEW weight (exclude its old weight).
        var otherTotal = await _dbContext.Goals
            .Where(g => g.EmployeeId == goal.EmployeeId && g.CycleId == goal.CycleId && g.Id != goal.Id)
            .SumAsync(g => g.Weight, cancellationToken);
        var newTotal = otherTotal + input.Weight;
        if (newTotal > RequiredTotalWeight)
            return Result<GoalDto>.Failure(
                $"Goal weights for this employee would total {newTotal}%, which exceeds 100%.",
                422, "weight_exceeds_100");

        goal.Title = input.Title.Trim();
        goal.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        goal.Category = input.Category;
        goal.Weight = input.Weight;
        goal.TargetValue = input.TargetValue.Trim();
        goal.MeasurementUnit = input.MeasurementUnit.Trim();
        goal.DueDate = input.DueDate;
        goal.ParentGoalId = input.ParentGoalId;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // NFR-4: a concurrent edit changed the row's xmin since we read it.
            return Result<GoalDto>.Failure(
                "This goal was modified by another session. Reload and try again.", 409, "concurrency_conflict");
        }

        _logger.LogInformation(
            "Goal updated. Id={GoalId}, EmployeeId={EmployeeId}, CycleId={CycleId}, TenantId={TenantId}, By={User}",
            goal.Id, goal.EmployeeId, goal.CycleId, _tenantContext.TenantId, _currentUser.Email);

        await _notifications.NotifyGoalChangedAsync("goal-modified", goal.Id, goal.EmployeeId, goal.CycleId, cancellationToken);

        return Result<GoalDto>.Success(ToDto(goal));
    }

    public async Task<Result> DeleteAsync(Guid goalId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var goal = await _dbContext.Goals.FirstOrDefaultAsync(g => g.Id == goalId, cancellationToken);
        if (goal is null)
            return Result.Failure("Goal not found.", 404);

        var authz = await AuthorizeForEmployeeAsync(goal.EmployeeId, cancellationToken);
        if (authz.IsFailure)
            return Result.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        var cycleResult = await GetOpenCycleAsync(goal.CycleId, cancellationToken);
        if (cycleResult.IsFailure)
            return Result.Failure(cycleResult.Error!, cycleResult.StatusCode ?? 400, cycleResult.ErrorCode);

        goal.IsDeleted = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Goal deleted (soft). Id={GoalId}, EmployeeId={EmployeeId}, CycleId={CycleId}, TenantId={TenantId}, By={User}",
            goal.Id, goal.EmployeeId, goal.CycleId, _tenantContext.TenantId, _currentUser.Email);

        await _notifications.NotifyGoalChangedAsync("goal-removed", goal.Id, goal.EmployeeId, goal.CycleId, cancellationToken);

        return Result.Success();
    }

    public async Task<Result<EmployeeGoalsDto>> GetEmployeeGoalsAsync(
        Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<EmployeeGoalsDto>.Failure("Tenant context is not resolved.", 400);

        var authz = await AuthorizeForEmployeeAsync(employeeId, cancellationToken);
        if (authz.IsFailure)
            return Result<EmployeeGoalsDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        var goals = await _dbContext.Goals
            .AsNoTracking()
            .Where(g => g.EmployeeId == employeeId && g.CycleId == cycleId)
            .OrderBy(g => g.CreatedAt)
            .ToListAsync(cancellationToken);

        var cycle = await _dbContext.AppraisalCycles
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);

        return Result<EmployeeGoalsDto>.Success(new EmployeeGoalsDto
        {
            EmployeeId = employeeId,
            CycleId = cycleId,
            TotalWeight = goals.Sum(g => g.Weight),
            IsGoalSettingOpen = cycle?.IsGoalSettingOpen(DateTime.UtcNow) ?? false,
            Goals = goals.Select(ToDto).ToList(),
        });
    }

    public async Task<Result<TeamGoalsDashboardDto>> GetTeamDashboardAsync(
        Guid cycleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<TeamGoalsDashboardDto>.Failure("Tenant context is not resolved.", 400);

        var manager = await GetCurrentEmployeeAsync(cancellationToken);
        if (manager is null)
            return Result<TeamGoalsDashboardDto>.Failure(
                "The current user is not linked to an employee record.", 403, "no_employee_record");

        // AC-4: direct reports of the calling manager. (HR with SetGoal.All can still target any employee
        // via the per-employee endpoints; the team dashboard is intentionally the manager's own team.)
        var reports = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.ReportsToEmployeeId == manager.Id)
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.EmployeeNo })
            .ToListAsync(cancellationToken);

        var reportIds = reports.Select(r => r.Id).ToList();

        var goals = reportIds.Count == 0
            ? new List<Goal>()
            : await _dbContext.Goals
                .AsNoTracking()
                .Where(g => g.CycleId == cycleId && reportIds.Contains(g.EmployeeId))
                .ToListAsync(cancellationToken);

        var byEmployee = goals.GroupBy(g => g.EmployeeId).ToDictionary(grp => grp.Key, grp => grp.ToList());

        var members = reports.Select(r =>
        {
            byEmployee.TryGetValue(r.Id, out var memberGoals);
            memberGoals ??= [];
            return new TeamGoalStatusDto
            {
                EmployeeId = r.Id,
                EmployeeName = $"{r.FirstName} {r.LastName}".Trim(),
                EmployeeNo = r.EmployeeNo,
                GoalCount = memberGoals.Count,
                TotalWeight = memberGoals.Sum(g => g.Weight),
                Status = AggregateStatus(memberGoals),
            };
        }).ToList();

        return Result<TeamGoalsDashboardDto>.Success(new TeamGoalsDashboardDto
        {
            CycleId = cycleId,
            Members = members,
        });
    }

    // ── Authorization (BR-4) ─────────────────────────────────────────

    /// <summary>
    /// BR-4: the caller may set/view goals for <paramref name="employeeId"/> if they hold
    /// Performance.SetGoal.All (HR override) OR they hold Performance.SetGoal.Team AND are the employee's
    /// direct reporting manager. Also validates the target employee exists in-tenant (global filter).
    /// </summary>
    private async Task<Result> AuthorizeForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var target = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (target is null)
            return Result.Failure("Employee not found.", 404, "employee_not_found");

        var permissions = _currentUser.Permissions;

        // HR override.
        if (permissions.Contains(PermissionCatalog.Performance.SetGoalAll))
            return Result.Success();

        if (!permissions.Contains(PermissionCatalog.Performance.SetGoalTeam))
            return Result.Failure("You do not have permission to set goals.", 403, "forbidden");

        var manager = await GetCurrentEmployeeAsync(cancellationToken);
        if (manager is null)
            return Result.Failure(
                "The current user is not linked to an employee record.", 403, "no_employee_record");

        if (target.ReportsToEmployeeId != manager.Id)
            return Result.Failure(
                "You can only set goals for your direct reports.", 403, "not_direct_report");

        return Result.Success();
    }

    private Task<Employee?> GetCurrentEmployeeAsync(CancellationToken cancellationToken)
        => _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);

    // ── Goal-setting window (BR-1/AC-5) ──────────────────────────────

    private async Task<Result> GetOpenCycleAsync(Guid cycleId, CancellationToken cancellationToken)
    {
        var cycle = await _dbContext.AppraisalCycles
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (cycle is null)
            return Result.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        if (!cycle.IsGoalSettingOpen(DateTime.UtcNow))
            return Result.Failure(
                "The goal-setting window for this cycle has closed.", 409, "goal_setting_closed");

        return Result.Success();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static string AggregateStatus(IReadOnlyList<Goal> goals)
    {
        if (goals.Count == 0) return "NotStarted";
        if (goals.All(g => g.Status == GoalStatus.Acknowledged)) return "Acknowledged";
        if (goals.All(g => g.Status is GoalStatus.Submitted or GoalStatus.Acknowledged)) return "Submitted";
        return "Draft";
    }

    private static GoalDto ToDto(Goal g) => new()
    {
        Id = g.Id,
        CycleId = g.CycleId,
        EmployeeId = g.EmployeeId,
        Title = g.Title,
        Description = g.Description,
        Category = g.Category,
        CategoryName = g.Category.ToString(),
        Weight = g.Weight,
        TargetValue = g.TargetValue,
        MeasurementUnit = g.MeasurementUnit,
        DueDate = g.DueDate,
        ParentGoalId = g.ParentGoalId,
        Status = g.Status,
        StatusName = g.Status.ToString(),
        CreatedAt = g.CreatedAt,
        UpdatedAt = g.UpdatedAt,
    };
}
