using System.Globalization;
using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Dashboard.DTOs;
using HRM.Application.Features.LeaveRequests.DTOs;
using HRM.Application.Features.Reports.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Role-based KPI dashboard (US-RPT-005). A THIN COMPOSITION layer over the existing per-module services —
/// it does NOT recompute any metric and adds NO new entity / migration. The role is derived SERVER-SIDE from
/// <see cref="ICurrentUser"/> (BR-1 / FR-8); a client never supplies it. Widget data is tenant-scoped by the
/// EF global query filter (AC-5) and gated by role/permission (FR-8).
///
/// <para>Composition map (which existing service feeds each widget):</para>
/// <list type="bullet">
/// <item>headcount / turnover-rate / recent-joiners ← <see cref="IHrReportService"/> (US-RPT-001).</item>
/// <item>open-positions ← <see cref="IVacancyService"/> (status = Open, US-REC-001).</item>
/// <item>pending-leave / pending-approvals ← <see cref="ILeaveRequestService"/> (BR-3 assigned-to-me).</item>
/// <item>leave-balance (donut) ← <see cref="ILeaveDashboardService"/> (US-LV-006).</item>
/// <item>attendance-today / team-attendance-today ← <see cref="IAttendanceDashboardService"/> (US-ATT-010).</item>
/// <item>onboarding-progress / onboarding-in-progress ← <see cref="IOnboardingChecklistService"/> + a direct count.</item>
/// <item>upcoming-holidays ← <see cref="IHolidayService"/> (US-LV-007).</item>
/// <item>upcoming-reviews ← <see cref="IAppraisalCycleService"/> (US-PRF-004; omitted if no active cycle).</item>
/// <item>recent-payslips ← <see cref="IMyPayslipService"/> (US-PAY-005).</item>
/// <item>team-size / upcoming-birthdays / recent-joiners population ← direct tenant-scoped Employee reads.</item>
/// </list>
///
/// <para>DEFERRED (documented, not fabricated): BR-5 module-enablement gating — there is no per-tenant
/// module-enablement flag wired through (ITenantContext.EnabledModules is empty in practice), so ALL modules
/// are assumed enabled; when a real enablement source exists, filter the widget set by it. A general
/// pending-actions / tasks queue (US-NTF tasks are not persisted) — the employee "pending-actions" widget
/// surfaces onboarding pending tasks + a link only. PostgreSQL RLS (NFR-3) — EF global query filters are in
/// force.</para>
/// </summary>
public sealed class DashboardService : IDashboardService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(3);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IHrReportService _hrReports;
    private readonly ILogger<DashboardService> _logger;

    // DF-50: fans each role's widgets out over child DI scopes (bounded) so the ~8 independent per-role widget
    // builders no longer serialize on the single scoped AppDbContext. The runner re-seeds the tenant on every
    // child scope (isolation gate). Default bound 4 (Dashboard:MaxWidgetParallelism) to avoid Npgsql pool
    // exhaustion (~8 widgets × concurrent users × EnableRetryOnFailure can cascade).
    private readonly IParallelTenantScopeRunner _parallelRunner;
    private readonly int _maxWidgetParallelism;

    // Per-module collaborators are OPTIONAL so the tests can construct the service with only the pieces a
    // given scenario exercises (mirrors the HrReportService optional-collaborator pattern). A widget whose
    // collaborator is absent is simply omitted (graceful degradation).
    private readonly IVacancyService? _vacancies;
    private readonly ILeaveRequestService? _leaveRequests;
    private readonly ILeaveDashboardService? _leaveDashboard;
    private readonly IAttendanceDashboardService? _attendance;
    private readonly IOnboardingChecklistService? _onboarding;
    private readonly IHolidayService? _holidays;
    private readonly IAppraisalCycleService? _appraisalCycles;
    private readonly IMyPayslipService? _payslips;
    private readonly IDistributedCache? _cache;
    // ISSUE-285(a): clock seam so the recurring birthday/anniversary window (which depends on "today") is
    // deterministically testable — including the year-end wrap — without waiting for Dec 31. Trailing-optional
    // and defaults to TimeProvider.System, so production/DI behaviour is unchanged.
    private readonly TimeProvider _timeProvider;

    public DashboardService(
        AppDbContext db,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IHrReportService hrReports,
        ILogger<DashboardService> logger,
        IParallelTenantScopeRunner parallelRunner,
        IConfiguration configuration,
        IVacancyService? vacancies = null,
        ILeaveRequestService? leaveRequests = null,
        ILeaveDashboardService? leaveDashboard = null,
        IAttendanceDashboardService? attendance = null,
        IOnboardingChecklistService? onboarding = null,
        IHolidayService? holidays = null,
        IAppraisalCycleService? appraisalCycles = null,
        IMyPayslipService? payslips = null,
        IDistributedCache? cache = null,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _hrReports = hrReports;
        _logger = logger;
        _parallelRunner = parallelRunner;
        _maxWidgetParallelism = Math.Max(1, configuration.GetValue("Dashboard:MaxWidgetParallelism", 4));
        _vacancies = vacancies;
        _leaveRequests = leaveRequests;
        _leaveDashboard = leaveDashboard;
        _attendance = attendance;
        _onboarding = onboarding;
        _holidays = holidays;
        _appraisalCycles = appraisalCycles;
        _payslips = payslips;
        _cache = cache;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Result<DashboardResponse>> GetWidgetsAsync(
        bool refresh = false, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<DashboardResponse>.Failure("No tenant context resolved.", 400);

        // Resolve the acting employee (if any) + role server-side (BR-1 / FR-8).
        var me = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);

        bool hasDirectReports = me is not null && await _db.Employees.AsNoTracking()
            .AnyAsync(e => e.ReportsToEmployeeId == me.Id, cancellationToken);

        var role = ResolveRole(_currentUser.Permissions, _currentUser.Roles, hasDirectReports);
        var greetingName = me?.FirstName ?? string.Empty;

        var widgets = role switch
        {
            "hr" => await BuildHrWidgetsAsync(refresh, cancellationToken),
            "manager" => await BuildManagerWidgetsAsync(me, refresh, cancellationToken),
            _ => await BuildEmployeeWidgetsAsync(me, refresh, cancellationToken),
        };

        return Result<DashboardResponse>.Success(new DashboardResponse
        {
            Role = role,
            GreetingName = greetingName,
            GeneratedAt = DateTime.UtcNow,
            Widgets = widgets,
        });
    }

    // ── Role resolution (BR-1 / FR-8) ───────────────────────────────────────

    /// <summary>
    /// Server-side role precedence (BR-1): HR (HR Officer / HR Manager / Tenant Admin — i.e. holds a
    /// cross-org View.All or Reports HR permission) → "hr"; else a user with direct reports OR the Manager
    /// role → "manager"; else → "employee". Pure + static so it is unit-tested directly.
    /// </summary>
    public static string ResolveRole(
        IReadOnlyList<string> permissions, IReadOnlyList<string> roles, bool hasDirectReports)
    {
        var perms = permissions ?? [];
        bool isHr =
            perms.Contains(PermissionCatalog.Employee.ViewAll) ||
            perms.Contains(PermissionCatalog.Leave.ViewAll) ||
            perms.Contains(PermissionCatalog.Attendance.ViewAll);

        if (isHr)
            return "hr";

        bool isManager = hasDirectReports ||
            (roles ?? []).Any(r => string.Equals(r, PermissionCatalog.BuiltInRoles.Manager, StringComparison.OrdinalIgnoreCase));

        return isManager ? "manager" : "employee";
    }

    // ── HR dashboard (AC-1) ─────────────────────────────────────────────────
    // headcount, open-positions, pending-leave, attendance-today, upcoming-birthdays, recent-joiners,
    // onboarding-in-progress, turnover-rate.

    // DF-50: the ~8 HR widgets are mutually independent — each is now a factory that resolves a FRESH,
    // tenant-re-seeded DashboardService from its own child scope (own AppDbContext + collaborators) and builds
    // ONE widget. The runner drives them concurrently (bounded) and returns them IN ORDER; nulls omit (as before).
    private async Task<List<DashboardWidget>> BuildHrWidgetsAsync(bool refresh, CancellationToken ct)
    {
        var factories = new List<Func<IServiceProvider, CancellationToken, Task<DashboardWidget?>>>
        {
            // headcount (+ MoM trend from headcount-this-month vs joiners last month — growth = positive/green).
            (sp, c) => Svc(sp).BuildHeadcountCachedAsync(refresh, c),
            // open-positions (status = Open) ← IVacancyService.
            (sp, c) => Svc(sp).BuildOpenPositionsCachedAsync(refresh, c),
            // pending-leave (tenant-wide pending leave requests; click-through to the leave queue).
            (sp, c) => Svc(sp).BuildPendingLeaveAllCachedAsync(refresh, c),
            // attendance-today (HR all scope) ← IAttendanceDashboardService.
            (sp, c) => Svc(sp).BuildAttendanceTodayCachedAsync("all", "attendance-today", "Today's Attendance", refresh, c),
            // upcoming-birthdays + anniversaries (next 7 days, BR-4).
            (sp, c) => Svc(sp).BuildUpcomingBirthdaysCachedAsync(null, refresh, c),
            // recent-joiners (last 30 days).
            (sp, c) => Svc(sp).BuildRecentJoinersCachedAsync(refresh, c),
            // onboarding-in-progress (active checklist instances).
            (sp, c) => Svc(sp).BuildOnboardingInProgressCachedAsync(refresh, c),
            // turnover-rate (current quarter) ← IHrReportService EmployeeTurnover; increase = negative/red.
            (sp, c) => Svc(sp).BuildTurnoverCachedAsync(refresh, c),
        };

        return await FanOutAsync(factories, ct);
    }

    // ── Manager dashboard (AC-2) ────────────────────────────────────────────
    // team-size, team-attendance-today, pending-approvals, team-leave-calendar, upcoming-reviews, quick-actions.

    private async Task<List<DashboardWidget>> BuildManagerWidgetsAsync(Employee? me, bool refresh, CancellationToken ct)
    {
        var factories = new List<Func<IServiceProvider, CancellationToken, Task<DashboardWidget?>>>
        {
            // team-size (direct reports of me).
            (sp, c) => Svc(sp).BuildTeamSizeCachedAsync(me, refresh, c),
            // team-attendance-today (team scope) ← IAttendanceDashboardService.
            (sp, c) => Svc(sp).BuildAttendanceTodayCachedAsync("team", "team-attendance-today", "Team Attendance Today", refresh, c),
            // pending-approvals (leave requests assigned to ME only, BR-3) ← ILeaveRequestService.
            (sp, c) => Svc(sp).BuildPendingApprovalsCachedAsync(refresh, c),
            // team-leave-calendar (pure link widget — no scope/DB needed).
            (_, _) => Task.FromResult<DashboardWidget?>(TeamLeaveCalendarWidget()),
            // upcoming-reviews (active appraisal cycle) ← IAppraisalCycleService; OMITTED gracefully if none.
            (sp, c) => Svc(sp).BuildUpcomingReviewsCachedAsync(refresh, c),
            // quick-actions (BR-6 actionable shortcuts for the manager persona, FR-7) — pure.
            (_, _) => Task.FromResult<DashboardWidget?>(QuickActionsWidget()),
        };

        return await FanOutAsync(factories, ct);
    }

    // Pure manager widgets (no DB) — extracted so they slot into the fan-out factory list in order.
    private static DashboardWidget TeamLeaveCalendarWidget() => new()
    {
        WidgetKey = "team-leave-calendar",
        Label = "Team Leave Calendar",
        LinkUrl = "/leave/calendar",
    };

    private static DashboardWidget QuickActionsWidget() => new()
    {
        WidgetKey = "quick-actions",
        Label = "Quick Actions",
        Items =
        [
            new() { Label = "Review pending leave", LinkUrl = "/leave/approvals", LinkFilters = Filters(("status", "Pending")) },
            new() { Label = "Review attendance regularizations", LinkUrl = "/attendance/approvals", LinkFilters = Filters(("status", "Pending")) },
            new() { Label = "View team", LinkUrl = "/team" },
        ],
    };

    // ── Employee dashboard (AC-3) ───────────────────────────────────────────
    // leave-balance, attendance-this-month, onboarding-progress, upcoming-holidays, recent-payslips, pending-actions.

    private async Task<List<DashboardWidget>> BuildEmployeeWidgetsAsync(Employee? me, bool refresh, CancellationToken ct)
    {
        var factories = new List<Func<IServiceProvider, CancellationToken, Task<DashboardWidget?>>>
        {
            // leave-balance (donut: consumed vs remaining) ← ILeaveDashboardService.
            (sp, c) => Svc(sp).BuildLeaveBalanceCachedAsync(refresh, c),
            // attendance-this-month (progress bar) ← the materialized monthly summary (own record, current month).
            (sp, c) => Svc(sp).BuildAttendanceThisMonthCachedAsync(me, refresh, c),
            // onboarding-progress (only when there is an active checklist) ← IOnboardingChecklistService.
            (sp, c) => Svc(sp).BuildOnboardingProgressCachedAsync(refresh, c),
            // upcoming-holidays (next 30 days) ← IHolidayService.
            (sp, c) => Svc(sp).BuildUpcomingHolidaysCachedAsync(refresh, c),
            // recent-payslips (link + latest count) ← IMyPayslipService.
            (sp, c) => Svc(sp).BuildRecentPayslipsCachedAsync(refresh, c),
            // pending-actions (onboarding pending tasks + link; the broader US-NTF task queue is deferred).
            //   NOTE (DF-50): onboarding-progress + pending-actions both call GetMyChecklistProgressAsync. They now
            //   run in SEPARATE child scopes so they can't share that result — accepted (no cross-scope state).
            (sp, c) => Svc(sp).BuildPendingActionsCachedAsync(refresh, c),
        };

        return await FanOutAsync(factories, ct);
    }

    // ── DF-50 fan-out plumbing ───────────────────────────────────────────────

    /// <summary>
    /// Runs the per-widget factories concurrently (bounded by <see cref="_maxWidgetParallelism"/>) via
    /// <see cref="IParallelTenantScopeRunner"/> — each in its own tenant-re-seeded child scope — then drops the
    /// null results (graceful omit, as the old sequential <c>Add</c> did) preserving input order.
    /// </summary>
    private async Task<List<DashboardWidget>> FanOutAsync(
        IEnumerable<Func<IServiceProvider, CancellationToken, Task<DashboardWidget?>>> factories, CancellationToken ct)
    {
        var results = await _parallelRunner.RunAllAsync(factories, _maxWidgetParallelism, ct);
        var widgets = new List<DashboardWidget>(results.Count);
        foreach (var w in results)
            if (w is not null)
                widgets.Add(w);
        return widgets;
    }

    /// <summary>
    /// Resolves the FRESH, tenant-re-seeded <see cref="DashboardService"/> from a child scope. The child instance
    /// has its own scoped <c>AppDbContext</c> + collaborators, so one widget's queries never race another's.
    /// </summary>
    private static DashboardService Svc(IServiceProvider sp) => (DashboardService)sp.GetRequiredService<IDashboardService>();

    // ── Internal per-widget entrypoints (cache-wrapped) — invoked ON a child-scope instance by the factories ──
    // Each mirrors exactly what the old sequential role builders did for that widget (same cache key/role/TTL).

    internal Task<DashboardWidget?> BuildHeadcountCachedAsync(bool refresh, CancellationToken ct)
        => CachedAsync("hr", "headcount", refresh, ct, () => HeadcountWidgetAsync(ct));

    internal Task<DashboardWidget?> BuildOpenPositionsCachedAsync(bool refresh, CancellationToken ct)
        => CachedAsync("hr", "open-positions", refresh, ct, () => OpenPositionsWidgetAsync(ct));

    internal Task<DashboardWidget?> BuildPendingLeaveAllCachedAsync(bool refresh, CancellationToken ct)
        => CachedAsync("hr", "pending-leave", refresh, ct, () => PendingLeaveAllWidgetAsync(ct));

    internal Task<DashboardWidget?> BuildAttendanceTodayCachedAsync(
        string scope, string key, string label, bool refresh, CancellationToken ct)
        => CachedAsync(scope == "team" ? "manager" : "hr", key, refresh, ct, () => AttendanceTodayWidgetAsync(scope, key, label, ct));

    internal Task<DashboardWidget?> BuildUpcomingBirthdaysCachedAsync(Guid? teamManagerId, bool refresh, CancellationToken ct)
        => CachedAsync("hr", "upcoming-birthdays", refresh, ct, () => UpcomingBirthdaysWidgetAsync(teamManagerId, ct));

    internal Task<DashboardWidget?> BuildRecentJoinersCachedAsync(bool refresh, CancellationToken ct)
        => CachedAsync("hr", "recent-joiners", refresh, ct, () => RecentJoinersWidgetAsync(ct));

    internal Task<DashboardWidget?> BuildOnboardingInProgressCachedAsync(bool refresh, CancellationToken ct)
        => CachedAsync("hr", "onboarding-in-progress", refresh, ct, () => OnboardingInProgressWidgetAsync(ct));

    internal Task<DashboardWidget?> BuildTurnoverCachedAsync(bool refresh, CancellationToken ct)
        => CachedAsync("hr", "turnover-rate", refresh, ct, () => TurnoverWidgetAsync(DateTime.UtcNow, ct));

    internal Task<DashboardWidget?> BuildTeamSizeCachedAsync(Employee? me, bool refresh, CancellationToken ct)
        => CachedAsync("manager", "team-size", refresh, ct, () => TeamSizeWidgetAsync(me, ct));

    internal Task<DashboardWidget?> BuildPendingApprovalsCachedAsync(bool refresh, CancellationToken ct)
        => CachedAsync("manager", "pending-approvals", refresh, ct, () => PendingApprovalsWidgetAsync(ct));

    internal Task<DashboardWidget?> BuildUpcomingReviewsCachedAsync(bool refresh, CancellationToken ct)
        => CachedAsync("manager", "upcoming-reviews", refresh, ct, () => UpcomingReviewsWidgetAsync(ct));

    internal Task<DashboardWidget?> BuildLeaveBalanceCachedAsync(bool refresh, CancellationToken ct)
        => CachedAsync("employee", "leave-balance", refresh, ct, () => LeaveBalanceWidgetAsync(ct));

    internal Task<DashboardWidget?> BuildAttendanceThisMonthCachedAsync(Employee? me, bool refresh, CancellationToken ct)
        => CachedAsync("employee", "attendance-this-month", refresh, ct, () => AttendanceThisMonthWidgetAsync(me, ct));

    internal Task<DashboardWidget?> BuildOnboardingProgressCachedAsync(bool refresh, CancellationToken ct)
        => CachedAsync("employee", "onboarding-progress", refresh, ct, () => OnboardingProgressWidgetAsync(ct));

    internal Task<DashboardWidget?> BuildUpcomingHolidaysCachedAsync(bool refresh, CancellationToken ct)
        => CachedAsync("employee", "upcoming-holidays", refresh, ct, () => UpcomingHolidaysWidgetAsync(ct));

    internal Task<DashboardWidget?> BuildRecentPayslipsCachedAsync(bool refresh, CancellationToken ct)
        => CachedAsync("employee", "recent-payslips", refresh, ct, () => RecentPayslipsWidgetAsync(ct));

    internal Task<DashboardWidget?> BuildPendingActionsCachedAsync(bool refresh, CancellationToken ct)
        => CachedAsync("employee", "pending-actions", refresh, ct, () => PendingActionsWidgetAsync(ct));

    // ════════════════════════════════════════════════════════════════════════
    //  Per-widget builders (each returns null → the widget is omitted)
    // ════════════════════════════════════════════════════════════════════════

    private async Task<DashboardWidget?> HeadcountWidgetAsync(CancellationToken ct)
    {
        // Reuse IHrReportService's HeadcountSummary "Total Headcount" KPI (it computes active/inactive).
        var report = await _hrReports.GenerateReportAsync(HrReportType.HeadcountSummary, new HrReportQueryParams(), refresh: true, ct);
        if (report.IsFailure)
            return null;

        var total = StatValue(report.Value!, "Total Headcount");

        // MoM trend: this-month joiners vs last-month joiners (the directional headcount movement). Growth →
        // positive/green (semantic, §8). A cheap tenant-scoped read; HrReportService owns the static numbers.
        var (thisMonth, lastMonth) = await JoinersByMonthAsync(ct);
        var trend = Trend(thisMonth, lastMonth, growthIsPositive: true);

        return new DashboardWidget
        {
            WidgetKey = "headcount",
            Label = "Total Headcount",
            Value = total,
            PreviousValue = trend.Previous,
            TrendDirection = trend.Direction,
            TrendPercentage = trend.Percentage,
            TrendIsPositive = trend.IsPositive,
            MiniChart = new DashboardMiniChart
            {
                Type = "sparkline",
                Series =
                [
                    new() { Label = "Last month joiners", Value = lastMonth },
                    new() { Label = "This month joiners", Value = thisMonth },
                ],
            },
            LinkUrl = "/employees",
        };
    }

    private async Task<DashboardWidget?> OpenPositionsWidgetAsync(CancellationToken ct)
    {
        if (_vacancies is null)
            return null;

        var result = await _vacancies.ListAsync(VacancyStatus.Open, null, null, page: 1, pageSize: 1, ct);
        if (result.IsFailure)
            return null;

        return new DashboardWidget
        {
            WidgetKey = "open-positions",
            Label = "Open Positions",
            Value = result.Value!.TotalCount,
            LinkUrl = "/recruitment/vacancies",
            LinkFilters = Filters(("status", "Open")),
        };
    }

    private async Task<DashboardWidget?> PendingLeaveAllWidgetAsync(CancellationToken ct)
    {
        // HR view: count tenant-wide Pending leave requests (tenant-scoped by the EF global filter). A thin
        // direct read — there is no "all pending leave count" service method (the leave service exposes only
        // the manager-scoped queue, which is BR-3 assigned-to-me, not the HR-wide total).
        var count = await _db.LeaveRequests.AsNoTracking()
            .CountAsync(r => r.Status == LeaveRequestStatus.Pending, ct);

        return new DashboardWidget
        {
            WidgetKey = "pending-leave",
            Label = "Pending Leave Requests",
            Value = count,
            LinkUrl = "/leave/requests",
            LinkFilters = Filters(("status", "Pending")),
        };
    }

    private async Task<DashboardWidget?> PendingApprovalsWidgetAsync(CancellationToken ct)
    {
        if (_leaveRequests is null)
            return null;

        // BR-3: only items assigned to the logged-in user. GetPendingForManagerAsync returns the current
        // manager's direct reports' pending requests (its own resolution of "assigned to me").
        var queue = await _leaveRequests.GetPendingForManagerAsync(
            new PendingLeaveQueueQueryParams { Page = 1, PageSize = 1 }, ct);
        if (queue.IsFailure)
            return null;

        return new DashboardWidget
        {
            WidgetKey = "pending-approvals",
            Label = "Pending Approvals",
            Value = queue.Value!.TotalCount,
            LinkUrl = "/leave/approvals",
            LinkFilters = Filters(("status", "Pending")),
        };
    }

    private async Task<DashboardWidget?> AttendanceTodayWidgetAsync(string scope, string key, string label, CancellationToken ct)
    {
        if (_attendance is null)
            return null;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var kpis = await _attendance.GetKpisAsync(today, scope, ct);
        if (kpis.IsFailure)
            return null;

        var k = kpis.Value!;
        return new DashboardWidget
        {
            WidgetKey = key,
            Label = label,
            Value = k.AttendancePercent,
            Unit = "%",
            MiniChart = new DashboardMiniChart
            {
                Type = "progress",
                Max = 100,
                Series = [new() { Label = "Attendance", Value = k.AttendancePercent }],
            },
            LinkUrl = "/attendance/dashboard",
        };
    }

    private const int UpcomingWindowDays = 7;

    private async Task<DashboardWidget?> UpcomingBirthdaysWidgetAsync(Guid? teamManagerId, CancellationToken ct)
    {
        // BR-4: birthdays + work anniversaries within the next 7 days. ISSUE-285(a): the recurring month/day
        // window is projected to a small set of month*100+day KEYS and pushed into SQL, so the birthday arm rides
        // the (tenant_id, birth_month_day) index and neither query materializes every active employee (the old
        // ToListAsync blew the dashboard p95<800ms SLA at 50k employees). Two index-friendly queries beat one OR
        // (an OR across an indexed column and an unindexed expression forces a seq scan).
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(nowUtc);
        var windowKeys = BuildRecurringWindowKeys(today, UpcomingWindowDays);

        IQueryable<Employee> baseQuery = _db.Employees.AsNoTracking()
            .Where(e => e.Status == EmployeeStatus.Active || e.Status == EmployeeStatus.Probation);
        if (teamManagerId is { } mgr)
            baseQuery = baseQuery.Where(e => e.ReportsToEmployeeId == mgr);

        // Birthdays — index-backed on (tenant_id, birth_month_day). BirthMonthDay is null when DOB is unset.
        var birthdayRows = await baseQuery
            .Where(e => e.BirthMonthDay != null && windowKeys.Contains(e.BirthMonthDay.Value))
            .Select(e => new { e.FirstName, e.LastName, e.DateOfBirth })
            .ToListAsync(ct);

        // Work anniversaries — the DateOfJoining month/day window, filtered in SQL (returns only the few matches
        // instead of the whole table). Scope note (ISSUE-285(a)): no denormalized join-key column is in scope, so
        // this arm is a filtered scan, not an index seek; it still avoids the full in-memory materialization.
        var todayStart = nowUtc.Date;
        var anniversaryRows = await baseQuery
            .Where(e => e.DateOfJoining < todayStart
                        && windowKeys.Contains(e.DateOfJoining.Month * 100 + e.DateOfJoining.Day))
            .Select(e => new { e.FirstName, e.LastName, e.DateOfJoining })
            .ToListAsync(ct);

        var items = new List<DashboardListItem>();

        foreach (var e in birthdayRows)
        {
            var name = $"{e.FirstName} {e.LastName}".Trim();
            // IsWithinNextDays recomputes the exact occurrence for display (and clamps Feb-29 → Feb-28); it is
            // always true here because the row already matched the same window keys.
            if (e.DateOfBirth is { } dob && IsWithinNextDays(DateOnly.FromDateTime(dob), today, UpcomingWindowDays, out var bDay))
                items.Add(new DashboardListItem { Label = $"{name} — Birthday", Value = bDay.ToString("MMM d", CultureInfo.InvariantCulture) });
        }

        foreach (var e in anniversaryRows)
        {
            var name = $"{e.FirstName} {e.LastName}".Trim();
            var doj = DateOnly.FromDateTime(e.DateOfJoining);
            if (IsWithinNextDays(doj, today, UpcomingWindowDays, out var aDay))
                items.Add(new DashboardListItem { Label = $"{name} — Work Anniversary", Value = aDay.ToString("MMM d", CultureInfo.InvariantCulture) });
        }

        items = items.OrderBy(i => i.Value as string).ToList();

        return new DashboardWidget
        {
            WidgetKey = "upcoming-birthdays",
            Label = "Upcoming Birthdays & Anniversaries",
            Value = items.Count,
            Items = items,
            LinkUrl = "/employees",
        };
    }

    /// <summary>
    /// ISSUE-285(a): the set of month*100+day keys covering [today, today + <paramref name="days"/>] inclusive —
    /// the SQL-translatable form of the recurring birthday/anniversary window. Naturally wraps the year boundary
    /// (Dec 30 + 7d → 1230,1231,0101…0106) because it walks <see cref="DateOnly.AddDays(int)"/>. When the window
    /// spans Feb-28 in a NON-leap year it also emits 229 so a Feb-29 birthday still surfaces (projected onto
    /// Feb-28), matching <see cref="IsWithinNextDays"/>.
    /// </summary>
    internal static List<int> BuildRecurringWindowKeys(DateOnly today, int days)
    {
        var keys = new List<int>(days + 2);
        for (int i = 0; i <= days; i++)
        {
            var d = today.AddDays(i);
            keys.Add(d.Month * 100 + d.Day);
            if (d is { Month: 2, Day: 28 } && !DateTime.IsLeapYear(d.Year))
                keys.Add(229); // Feb-29 birthday projects onto Feb-28 in a non-leap year.
        }
        return keys;
    }

    private async Task<DashboardWidget?> RecentJoinersWidgetAsync(CancellationToken ct)
    {
        var since = DateTime.UtcNow.Date.AddDays(-30);
        var joiners = await _db.Employees.AsNoTracking()
            .Where(e => e.DateOfJoining >= since)
            .OrderByDescending(e => e.DateOfJoining)
            .Select(e => new { e.FirstName, e.LastName, e.EmployeeNo, e.DateOfJoining })
            .Take(5)
            .ToListAsync(ct);

        var total = await _db.Employees.AsNoTracking().CountAsync(e => e.DateOfJoining >= since, ct);

        return new DashboardWidget
        {
            WidgetKey = "recent-joiners",
            Label = "Recent Joiners (30 days)",
            Value = total,
            Items = joiners
                .Select(j => new DashboardListItem
                {
                    Label = $"{j.FirstName} {j.LastName}".Trim(),
                    Value = j.DateOfJoining.ToString("MMM d", CultureInfo.InvariantCulture),
                })
                .ToList(),
            LinkUrl = "/employees",
            LinkFilters = Filters(("sort", "recent")),
        };
    }

    private async Task<DashboardWidget?> OnboardingInProgressWidgetAsync(CancellationToken ct)
    {
        var count = await _db.Set<OnboardingChecklistInstance>().AsNoTracking()
            .CountAsync(i => i.Status == OnboardingChecklistStatus.Active, ct);

        return new DashboardWidget
        {
            WidgetKey = "onboarding-in-progress",
            Label = "Onboarding In Progress",
            Value = count,
            LinkUrl = "/onboarding/checklists",
            LinkFilters = Filters(("status", "Active")),
        };
    }

    private async Task<DashboardWidget?> TurnoverWidgetAsync(DateTime now, CancellationToken ct)
    {
        // Current quarter window.
        var quarterStartMonth = (now.Month - 1) / 3 * 3 + 1;
        var from = new DateTime(now.Year, quarterStartMonth, 1);
        var report = await _hrReports.GenerateReportAsync(
            HrReportType.EmployeeTurnover,
            new HrReportQueryParams { DateFrom = from, DateTo = now },
            refresh: true, ct);
        if (report.IsFailure)
            return null;

        var rate = StatValue(report.Value!, "Turnover Rate %");

        return new DashboardWidget
        {
            WidgetKey = "turnover-rate",
            Label = "Turnover Rate (Quarter)",
            Value = rate,
            Unit = "%",
            // Turnover is a "lower is better" metric; we have no prior-quarter snapshot table, so we surface
            // the value without a trend rather than fabricate one (the snapshot table is the US-RPT-001 FR-6
            // follow-up). When a trend IS available, an INCREASE is negative/red (semantic, §8).
            LinkUrl = "/reports/turnover",
        };
    }

    private async Task<DashboardWidget?> TeamSizeWidgetAsync(Employee? me, CancellationToken ct)
    {
        if (me is null)
            return null;

        var count = await _db.Employees.AsNoTracking().CountAsync(e => e.ReportsToEmployeeId == me.Id, ct);

        return new DashboardWidget
        {
            WidgetKey = "team-size",
            Label = "Team Size",
            Value = count,
            LinkUrl = "/team",
        };
    }

    private async Task<DashboardWidget?> UpcomingReviewsWidgetAsync(CancellationToken ct)
    {
        if (_appraisalCycles is null)
            return null;

        var active = await _appraisalCycles.GetActiveAsync(ct);
        // OMITTED gracefully when there is no active cycle (story directive).
        if (active.IsFailure || active.Value is null)
            return null;

        var c = active.Value;
        return new DashboardWidget
        {
            WidgetKey = "upcoming-reviews",
            Label = "Upcoming Reviews",
            Value = c.Name,
            Items =
            [
                new() { Label = "Cycle", Value = c.Name },
                new() { Label = "Current phase", Value = c.CurrentPhaseName ?? "—" },
                new() { Label = "Manager review window", Value = c.ManagerReviewOpen ? "Open" : "Closed" },
            ],
            LinkUrl = "/performance/reviews",
        };
    }

    private async Task<DashboardWidget?> LeaveBalanceWidgetAsync(CancellationToken ct)
    {
        if (_leaveDashboard is null)
            return null;

        var balances = await _leaveDashboard.GetMyBalancesAsync(null, ct);
        if (balances.IsFailure || balances.Value!.Count == 0)
            return null;

        // Donut: consumed (used) vs remaining (balance) aggregated across active leave types.
        decimal used = balances.Value.Sum(b => b.Used);
        decimal remaining = balances.Value.Sum(b => b.Balance);
        decimal entitlement = balances.Value.Sum(b => b.Entitlement + b.CarryForward);

        return new DashboardWidget
        {
            WidgetKey = "leave-balance",
            Label = "Leave Balance",
            Value = remaining,
            MiniChart = new DashboardMiniChart
            {
                Type = "donut",
                Max = entitlement > 0 ? entitlement : null,
                Series =
                [
                    new() { Label = "Remaining", Value = remaining },
                    new() { Label = "Used", Value = used },
                ],
            },
            LinkUrl = "/leave/balance",
        };
    }

    private async Task<DashboardWidget?> AttendanceThisMonthWidgetAsync(Employee? me, CancellationToken ct)
    {
        if (me is null)
            return null;

        var yearMonth = DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var summary = await _db.Set<AttendanceMonthlySummary>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.EmployeeId == me.Id && s.YearMonth == yearMonth, ct);

        if (summary is null)
            return null;

        decimal present = summary.TotalPresentDays;
        decimal absent = summary.TotalAbsentDays;
        decimal working = present + absent;
        decimal rate = working > 0 ? Math.Round(present / working * 100m, 2) : 0m;

        return new DashboardWidget
        {
            WidgetKey = "attendance-this-month",
            Label = "Attendance This Month",
            Value = rate,
            Unit = "%",
            MiniChart = new DashboardMiniChart
            {
                Type = "progress",
                Max = 100,
                Series = [new() { Label = "Present", Value = rate }],
            },
            LinkUrl = "/attendance/me",
        };
    }

    private async Task<DashboardWidget?> OnboardingProgressWidgetAsync(CancellationToken ct)
    {
        if (_onboarding is null)
            return null;

        var progress = await _onboarding.GetMyChecklistProgressAsync(ct);
        // No active checklist → omit (AC-3 "if active").
        if (progress.IsFailure || progress.Value is null)
            return null;

        var p = progress.Value;
        return new DashboardWidget
        {
            WidgetKey = "onboarding-progress",
            Label = "Onboarding Progress",
            Value = p.ProgressPercent,
            Unit = "%",
            MiniChart = new DashboardMiniChart
            {
                Type = "progress",
                Max = 100,
                Series = [new() { Label = "Complete", Value = p.ProgressPercent }],
            },
            LinkUrl = "/onboarding/me",
        };
    }

    private async Task<DashboardWidget?> UpcomingHolidaysWidgetAsync(CancellationToken ct)
    {
        if (_holidays is null)
            return null;

        var from = DateOnly.FromDateTime(DateTime.UtcNow);
        var to = from.AddDays(30);
        var holidays = await _holidays.GetAllAsync(from, to, null, null, activeOnly: true, ct);
        if (holidays.IsFailure)
            return null;

        return new DashboardWidget
        {
            WidgetKey = "upcoming-holidays",
            Label = "Upcoming Holidays",
            Value = holidays.Value!.Count,
            Items = holidays.Value
                .OrderBy(h => h.Date)
                .Take(5)
                .Select(h => new DashboardListItem
                {
                    Label = h.Name,
                    Value = h.Date.ToString("MMM d", CultureInfo.InvariantCulture),
                })
                .ToList(),
            LinkUrl = "/holidays",
        };
    }

    private async Task<DashboardWidget?> RecentPayslipsWidgetAsync(CancellationToken ct)
    {
        if (_payslips is null)
            return null;

        var list = await _payslips.ListMineAsync(null, month: null, page: 1, pageSize: 3, ct);
        if (list.IsFailure)
            return null;

        return new DashboardWidget
        {
            WidgetKey = "recent-payslips",
            Label = "Recent Payslips",
            Value = list.Value!.TotalCount,
            Items = list.Value.Items
                .Select(s => new DashboardListItem
                {
                    Label = $"{s.PayYear}-{s.PayMonth:D2}",
                    Value = s.NetSalary,
                    LinkUrl = $"/payslips/{s.PayslipId}",
                })
                .ToList(),
            LinkUrl = "/payslips",
        };
    }

    private async Task<DashboardWidget?> PendingActionsWidgetAsync(CancellationToken ct)
    {
        // The general US-NTF task/forms queue is NOT persisted (deferred); we surface the employee's
        // OUTSTANDING onboarding tasks + a link, which is the one real, tenant-scoped pending-actions source.
        var items = new List<DashboardListItem>();
        int count = 0;

        if (_onboarding is not null)
        {
            var progress = await _onboarding.GetMyChecklistProgressAsync(ct);
            if (progress.IsSuccess && progress.Value is { } p && (p.PendingTasks > 0 || p.OverdueTasks > 0))
            {
                count = p.PendingTasks;
                items.Add(new DashboardListItem
                {
                    Label = "Onboarding tasks to complete",
                    Value = p.PendingTasks,
                    LinkUrl = "/onboarding/me",
                });
                if (p.OverdueTasks > 0)
                    items.Add(new DashboardListItem
                    {
                        Label = "Overdue onboarding tasks",
                        Value = p.OverdueTasks,
                        LinkUrl = "/onboarding/me",
                    });
            }
        }

        return new DashboardWidget
        {
            WidgetKey = "pending-actions",
            Label = "Pending Actions",
            Value = count,
            Items = items,
            LinkUrl = "/tasks",
        };
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Pure helpers (unit-tested directly)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// BR-4: true when <paramref name="recurring"/>'s month/day falls within the next <paramref name="days"/>
    /// days (inclusive of today), wrapping across a year boundary. <paramref name="occurrence"/> is the matched
    /// date in the current window (this year or, when it wraps, next year). Pure + static so it is unit-tested.
    /// </summary>
    public static bool IsWithinNextDays(DateOnly recurring, DateOnly today, int days, out DateOnly occurrence)
    {
        // Project the recurring month/day onto this year; if already passed, onto next year.
        int year = today.Year;
        int month = recurring.Month;
        int day = Math.Min(recurring.Day, DateTime.DaysInMonth(year, month)); // Feb-29 → Feb-28 in non-leap years.
        var thisYear = new DateOnly(year, month, day);
        occurrence = thisYear < today
            ? new DateOnly(year + 1, month, Math.Min(recurring.Day, DateTime.DaysInMonth(year + 1, month)))
            : thisYear;

        int delta = occurrence.DayNumber - today.DayNumber;
        return delta >= 0 && delta <= days;
    }

    private sealed record TrendResult(object? Previous, string? Direction, decimal? Percentage, bool? IsPositive);

    /// <summary>
    /// BR-2 trend: compares <paramref name="current"/> to <paramref name="previous"/> and sets the semantic
    /// sentiment. <paramref name="growthIsPositive"/> = true means an INCREASE is good/green (headcount);
    /// false means an increase is bad/red (turnover). Pure helper used by the widget builders.
    /// </summary>
    private static TrendResult Trend(decimal current, decimal previous, bool growthIsPositive)
    {
        string direction = current > previous ? "up" : current < previous ? "down" : "flat";
        decimal? pct = previous != 0
            ? Math.Round((current - previous) / Math.Abs(previous) * 100m, 1)
            : (current != 0 ? 100m : 0m);

        bool? isPositive = direction == "flat"
            ? null
            : (direction == "up") == growthIsPositive;

        return new TrendResult(previous, direction, pct, isPositive);
    }

    private async Task<(decimal ThisMonth, decimal LastMonth)> JoinersByMonthAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var thisStart = new DateTime(now.Year, now.Month, 1);
        var lastStart = thisStart.AddMonths(-1);

        decimal thisMonth = await _db.Employees.AsNoTracking()
            .CountAsync(e => e.DateOfJoining >= thisStart && e.DateOfJoining < thisStart.AddMonths(1), ct);
        decimal lastMonth = await _db.Employees.AsNoTracking()
            .CountAsync(e => e.DateOfJoining >= lastStart && e.DateOfJoining < thisStart, ct);

        return (thisMonth, lastMonth);
    }

    private static decimal StatValue(HrReportResult report, string label)
    {
        var stat = report.Metadata.Summary.FirstOrDefault(s => s.Label == label);
        if (stat is null) return 0m;
        return stat.Value switch
        {
            decimal d => d,
            int i => i,
            long l => l,
            double dbl => (decimal)dbl,
            _ => decimal.TryParse(stat.Value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m,
        };
    }

    private static IReadOnlyDictionary<string, object?> Filters(params (string Key, object? Value)[] pairs)
        => pairs.ToDictionary(p => p.Key, p => p.Value);

    // ── FR-4 caching (per widget; key includes userId for team/personal scoping) ─────────────────

    private async Task<DashboardWidget?> CachedAsync(
        string role, string widgetKey, bool refresh, CancellationToken ct, Func<Task<DashboardWidget?>> build)
    {
        if (_cache is null)
            return await build();

        var key = $"t:{_tenantContext.TenantId}:dashboard:{role}:{_currentUser.UserId}:{widgetKey}";

        if (!refresh)
        {
            var cached = await TryGetCachedAsync(key, ct);
            if (cached is not null)
                return cached;
        }

        var widget = await build();
        if (widget is not null)
            await SetCachedAsync(key, widget, ct);
        return widget;
    }

    private async Task<DashboardWidget?> TryGetCachedAsync(string key, CancellationToken ct)
    {
        try
        {
            var json = await _cache!.GetStringAsync(key, ct);
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<DashboardWidget>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dashboard widget cache read failed for {Key}; computing fresh.", key);
            return null;
        }
    }

    private async Task SetCachedAsync(string key, DashboardWidget widget, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(widget, JsonOptions);
            await _cache!.SetStringAsync(key, json,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dashboard widget cache write failed for {Key}.", key);
        }
    }
}
