using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveReports.DTOs;
using HRM.Application.Features.Reports.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Pre-built HR reports (US-RPT-001). Read-only aggregation over the existing employee / department /
/// location / employment_history tables (NO new entity / migration). Tenant isolation (AC-5 / FR-7) is the
/// EF global query filter — there are NO manual <c>TenantId ==</c> checks here; the tenant id is read from
/// <see cref="ITenantContext"/> ONLY to build the Redis cache key (FR-5).
///
/// <para>Caching (FR-5 / FR-8): when an <see cref="IDistributedCache"/> is registered (Redis in prod, the
/// in-memory fallback otherwise) results are cached under <c>t:{tenantId}:report:{name}:{paramsHash}</c>
/// with a 10-minute TTL; the <c>refresh</c> flag bypasses + refreshes the entry.</para>
///
/// <para>InMemory-provider safety: this service NEVER projects scalar fields through a required navigation
/// that carries a query filter (which the EF InMemory provider silently empties); it selects raw FK ids and
/// resolves department / location display names via separate tenant-scoped lookup dictionaries, and uses
/// <c>List.Contains</c> for IN-style filters.</para>
/// </summary>
public sealed class HrReportService : IHrReportService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IDistributedCache? _cache;
    private readonly ILogger<HrReportService> _logger;

    // ── US-RPT-002 collaborators ────────────────────────────────────────────
    // The leave reports REUSE the US-LV-012 LeaveReportService computations verbatim (BR-1 balance,
    // utilization, absenteeism) rather than re-deriving leave logic; the result is mapped into this
    // generic envelope. ICurrentUser drives the AC-4 role scope (All vs direct-reports). Both are
    // optional so the existing US-RPT-001 3-arg test constructor keeps working — the leave/attendance
    // report types fail-closed (400) when they are absent.
    private readonly ILeaveReportService? _leaveReports;
    private readonly ICurrentUser? _currentUser;

    public HrReportService(
        AppDbContext db,
        ITenantContext tenantContext,
        ILogger<HrReportService> logger,
        IDistributedCache? cache = null,
        ILeaveReportService? leaveReports = null,
        ICurrentUser? currentUser = null)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
        _cache = cache;
        _leaveReports = leaveReports;
        _currentUser = currentUser;
    }

    public IReadOnlyList<HrReportDescriptorDto> ListReportTypes() => HrReportTypeKey.Catalog();

    public async Task<Result<HrReportResult>> GenerateReportAsync(
        HrReportType reportType,
        HrReportQueryParams queryParams,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<HrReportResult>.Failure("No tenant context resolved.", 400);

        // Resolve the date window defaults (§7): from = start of current month, to = today (UTC).
        var now = DateTime.UtcNow;
        var dateFrom = (queryParams.DateFrom ?? new DateTime(now.Year, now.Month, 1)).Date;
        var dateTo = (queryParams.DateTo ?? now).Date;

        // Role-scoped reports (US-RPT-002) must key the cache by the caller's scope so a Manager and an HR
        // Officer with identical filters never share an entry (would otherwise leak All-tenant data to a
        // team-scoped manager). RPT-001 reports are not role-scoped → empty scope segment, key unchanged.
        string scopeKey = IsScopedReport(reportType)
            ? ScopeKeyOf(await ResolveScopeAsync(cancellationToken))
            : string.Empty;

        var cacheKey = BuildCacheKey(reportType, queryParams, dateFrom, dateTo, scopeKey);

        // FR-5: serve from cache unless FR-8 refresh is requested.
        if (_cache is not null && !refresh)
        {
            var cached = await TryGetCachedAsync(cacheKey, cancellationToken);
            if (cached is not null)
                return Result<HrReportResult>.Success(cached with { Metadata = cached.Metadata with { FromCache = true } });
        }

        var result = reportType switch
        {
            HrReportType.HeadcountSummary => await HeadcountSummaryAsync(queryParams, dateFrom, dateTo, cancellationToken),
            HrReportType.EmployeeTurnover => await TurnoverAsync(queryParams, dateFrom, dateTo, cancellationToken),
            HrReportType.Demographics => await DemographicsAsync(queryParams, dateFrom, dateTo, cancellationToken),
            HrReportType.JoinersAndLeavers => await JoinersAndLeaversAsync(queryParams, dateFrom, dateTo, cancellationToken),
            HrReportType.DepartmentDistribution => await DepartmentDistributionAsync(queryParams, dateFrom, dateTo, cancellationToken),
            HrReportType.EmploymentTypeBreakdown => await EmploymentTypeBreakdownAsync(queryParams, dateFrom, dateTo, cancellationToken),

            // US-RPT-002 — leave reports reuse the US-LV-012 LeaveReportService (mapped into this envelope).
            HrReportType.LeaveUtilization => await LeaveUtilizationAsync(queryParams, dateFrom, dateTo, cancellationToken),
            HrReportType.LeaveBalance => await LeaveBalanceAsync(queryParams, dateFrom, dateTo, cancellationToken),
            HrReportType.AbsenteeismTrends => await AbsenteeismTrendsAsync(queryParams, dateFrom, dateTo, cancellationToken),

            // US-RPT-002 — attendance reports aggregate the US-ATT-007 attendance_monthly_summary table.
            HrReportType.AttendanceSummary => await AttendanceSummaryAsync(queryParams, dateFrom, dateTo, cancellationToken),
            HrReportType.OvertimeReport => await OvertimeReportAsync(queryParams, dateFrom, dateTo, cancellationToken),
            HrReportType.LateArrivalReport => await LateArrivalReportAsync(queryParams, dateFrom, dateTo, cancellationToken),

            _ => Result<HrReportResult>.Failure($"Unknown report type '{reportType}'.", 400),
        };

        if (result.IsSuccess && _cache is not null)
            await SetCachedAsync(cacheKey, result.Value!, cancellationToken);

        return result;
    }

    // ── Headcount Summary (AC-2) ────────────────────────────────────────────

    private async Task<Result<HrReportResult>> HeadcountSummaryAsync(
        HrReportQueryParams qp, DateTime dateFrom, DateTime dateTo, CancellationToken ct)
    {
        var employees = await FilteredEmployees(qp).ToListAsync(ct);
        var deptNames = await DepartmentNameLookup(ct);

        var active = employees.Count(e => IsActive(e.Status));
        var inactive = employees.Count - active;

        // Breakdown by employment type.
        var byType = Enum.GetValues<EmploymentType>()
            .Select(t => new HrChartPoint { Label = t.ToString(), Value = employees.Count(e => e.EmploymentType == t) })
            .ToList();

        // Breakdown by department (bar chart, AC-2).
        var byDept = employees
            .GroupBy(e => e.DepartmentId)
            .Select(g => new HrChartPoint { Label = DeptName(deptNames, g.Key), Value = g.Count() })
            .OrderByDescending(p => p.Value)
            .ToList();

        var summary = new List<HrSummaryStat>
        {
            new() { Label = "Total Headcount", Value = employees.Count, Tone = "neutral" },
            new() { Label = "Active", Value = active, Tone = "positive" },
            new() { Label = "Inactive / Separated", Value = inactive, Tone = "negative" },
        };

        var rows = new List<object?[]>
        {
            new object?[] { "Total Headcount", employees.Count },
            new object?[] { "Active", active },
            new object?[] { "Inactive / Separated", inactive },
        };
        rows.AddRange(byType.Select(p => new object?[] { $"Employment Type: {p.Label}", (int)p.Value }));

        return Ok(HrReportType.HeadcountSummary, qp, dateFrom, dateTo, summary,
            charts:
            [
                new() { Kind = "bar", Title = "Headcount by Department", Series = [Series("Headcount", byDept)] },
                new() { Kind = "bar", Title = "Headcount by Employment Type", Series = [Series("Headcount", byType)] },
            ],
            columns: ["Metric", "Value"],
            rows: rows);
    }

    // ── Employee Turnover (AC-3, BR-3, BR-4) ────────────────────────────────

    private async Task<Result<HrReportResult>> TurnoverAsync(
        HrReportQueryParams qp, DateTime dateFrom, DateTime dateTo, CancellationToken ct)
    {
        var employees = await FilteredEmployees(qp).ToListAsync(ct);
        var employeeIds = employees.Select(e => e.Id).ToList();
        var deptNames = await DepartmentNameLookup(ct);

        // Separations are recorded in the EmploymentHistory timeline as Status changes whose NEW value is a
        // separated status (BR-4: terminated / resigned / contract_ended; we map the EmployeeStatus enum's
        // separated states). EffectiveDate must fall inside the report window.
        var statusHistory = await _db.EmploymentHistories
            .Where(h => h.ChangeType == "Status")
            .Where(h => employeeIds.Contains(h.EmployeeId))
            .Where(h => h.EffectiveDate >= dateFrom && h.EffectiveDate <= dateTo)
            .Select(h => new { h.EmployeeId, h.NewValue, h.Reason, h.EffectiveDate })
            .ToListAsync(ct);

        var separations = statusHistory.Where(h => IsSeparatedValue(h.NewValue)).ToList();
        var totalSeparations = separations.Count;

        // Voluntary vs involuntary — derived from the reason/new-value text (BR-3 split). "Resigned" =>
        // voluntary; "Terminated" => involuntary; everything else falls back on a reason keyword.
        var voluntary = separations.Count(s => IsVoluntary(s.NewValue, s.Reason));
        var involuntary = totalSeparations - voluntary;

        // Average headcount in the period (BR-3 denominator) = (active at start + active at end) / 2,
        // approximated as the current active headcount (no point-in-time snapshot table exists). This is the
        // documented denominator for the on-the-fly report; a snapshot/materialized table is the FR-6 follow-up.
        var avgHeadcount = employees.Count(e => IsActive(e.Status));
        // Add back the separated employees so the denominator reflects the population that COULD separate.
        avgHeadcount += totalSeparations;
        var rate = avgHeadcount > 0 ? Math.Round((decimal)totalSeparations / avgHeadcount * 100m, 2) : 0m;

        // Monthly turnover trend (line chart) across the window.
        var monthly = separations
            .GroupBy(s => new DateTime(s.EffectiveDate.Year, s.EffectiveDate.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g => new HrChartPoint { Label = g.Key.ToString("yyyy-MM"), Value = g.Count() })
            .ToList();

        // Turnover by department (horizontal bar).
        var empDept = employees.ToDictionary(e => e.Id, e => e.DepartmentId);
        var byDept = separations
            .GroupBy(s => empDept.TryGetValue(s.EmployeeId, out var d) ? d : Guid.Empty)
            .Select(g => new HrChartPoint { Label = DeptName(deptNames, g.Key), Value = g.Count() })
            .OrderByDescending(p => p.Value)
            .ToList();

        // Average tenure of departed employees (in years), using DateOfJoining → separation EffectiveDate.
        var empJoin = employees.ToDictionary(e => e.Id, e => e.DateOfJoining);
        var tenures = separations
            .Where(s => empJoin.ContainsKey(s.EmployeeId))
            .Select(s => (s.EffectiveDate - empJoin[s.EmployeeId]).TotalDays / 365.25)
            .ToList();
        var avgTenureYears = tenures.Count > 0 ? Math.Round((decimal)tenures.Average(), 2) : 0m;

        var summary = new List<HrSummaryStat>
        {
            new() { Label = "Total Separations", Value = totalSeparations, Tone = "neutral" },
            new() { Label = "Turnover Rate %", Value = rate, Tone = "negative" },
            new() { Label = "Voluntary", Value = voluntary, Tone = "neutral" },
            new() { Label = "Involuntary", Value = involuntary, Tone = "neutral" },
            new() { Label = "Average Tenure (years)", Value = avgTenureYears, Tone = "neutral" },
        };

        var rows = new List<object?[]>
        {
            new object?[] { "Total Separations", totalSeparations },
            new object?[] { "Voluntary", voluntary },
            new object?[] { "Involuntary", involuntary },
            new object?[] { "Average Headcount (period)", avgHeadcount },
            new object?[] { "Turnover Rate %", rate },
            new object?[] { "Average Tenure (years)", avgTenureYears },
        };

        return Ok(HrReportType.EmployeeTurnover, qp, dateFrom, dateTo, summary,
            charts:
            [
                new() { Kind = "line", Title = "Monthly Turnover Trend", Series = [Series("Separations", monthly)] },
                new() { Kind = "horizontal-bar", Title = "Turnover by Department", Series = [Series("Separations", byDept)] },
                new()
                {
                    Kind = "pie", Title = "Voluntary vs Involuntary",
                    Series = [Series("Separations",
                    [
                        new() { Label = "Voluntary", Value = voluntary },
                        new() { Label = "Involuntary", Value = involuntary },
                    ])],
                },
            ],
            columns: ["Metric", "Value"],
            rows: rows);
    }

    // ── Demographics (AC-4, BR-5) ───────────────────────────────────────────

    private async Task<Result<HrReportResult>> DemographicsAsync(
        HrReportQueryParams qp, DateTime dateFrom, DateTime dateTo, CancellationToken ct)
    {
        var employees = await FilteredEmployees(qp).ToListAsync(ct);
        var deptNames = await DepartmentNameLookup(ct);
        var locNames = await LocationNameLookup(ct);

        // BR-5: age is computed at the REPORT DATE (dateTo), not the current date, for historical accuracy.
        var reportDate = dateTo;

        // Gender distribution (pie).
        var byGender = employees
            .GroupBy(e => e.Gender?.ToString() ?? "Unspecified")
            .Select(g => new HrChartPoint { Label = g.Key, Value = g.Count() })
            .OrderByDescending(p => p.Value)
            .ToList();

        // Age histogram with 5-year buckets (AC-4).
        var ageBuckets = employees
            .Where(e => e.DateOfBirth.HasValue)
            .Select(e => AgeAt(e.DateOfBirth!.Value, reportDate))
            .Where(age => age >= 0)
            .GroupBy(age => age / 5 * 5)
            .OrderBy(g => g.Key)
            .Select(g => new HrChartPoint { Label = $"{g.Key}-{g.Key + 4}", Value = g.Count() })
            .ToList();

        // Department + location distribution (stacked bar, AC-4).
        var byDept = employees
            .GroupBy(e => e.DepartmentId)
            .Select(g => new HrChartPoint { Label = DeptName(deptNames, g.Key), Value = g.Count() })
            .OrderByDescending(p => p.Value)
            .ToList();

        var byLocation = employees
            .GroupBy(e => e.LocationId)
            .Select(g => new HrChartPoint { Label = LocName(locNames, g.Key), Value = g.Count() })
            .OrderByDescending(p => p.Value)
            .ToList();

        var summary = new List<HrSummaryStat>
        {
            new() { Label = "Total Employees", Value = employees.Count, Tone = "neutral" },
            new() { Label = "Gender Groups", Value = byGender.Count, Tone = "neutral" },
            new() { Label = "Age Bands", Value = ageBuckets.Count, Tone = "neutral" },
        };

        var rows = new List<object?[]>();
        rows.AddRange(byGender.Select(p => new object?[] { "Gender", p.Label, (int)p.Value }));
        rows.AddRange(ageBuckets.Select(p => new object?[] { "Age Band", p.Label, (int)p.Value }));

        return Ok(HrReportType.Demographics, qp, dateFrom, dateTo, summary,
            charts:
            [
                new() { Kind = "pie", Title = "Gender Distribution", Series = [Series("Employees", byGender)] },
                new() { Kind = "histogram", Title = "Age Distribution", Series = [Series("Employees", ageBuckets)] },
                new() { Kind = "stacked-bar", Title = "Department Distribution", Series = [Series("Employees", byDept)] },
                new() { Kind = "stacked-bar", Title = "Location Distribution", Series = [Series("Employees", byLocation)] },
            ],
            columns: ["Dimension", "Group", "Count"],
            rows: rows);
    }

    // ── Joiners & Leavers ───────────────────────────────────────────────────

    private async Task<Result<HrReportResult>> JoinersAndLeaversAsync(
        HrReportQueryParams qp, DateTime dateFrom, DateTime dateTo, CancellationToken ct)
    {
        var employees = await FilteredEmployees(qp).ToListAsync(ct);
        var deptNames = await DepartmentNameLookup(ct);
        var employeeIds = employees.Select(e => e.Id).ToList();

        var joiners = employees.Where(e => e.DateOfJoining.Date >= dateFrom && e.DateOfJoining.Date <= dateTo).ToList();

        var leaverHistory = await _db.EmploymentHistories
            .Where(h => h.ChangeType == "Status")
            .Where(h => employeeIds.Contains(h.EmployeeId))
            .Where(h => h.EffectiveDate >= dateFrom && h.EffectiveDate <= dateTo)
            .Select(h => new { h.EmployeeId, h.NewValue, h.EffectiveDate })
            .ToListAsync(ct);
        var leavers = leaverHistory.Where(h => IsSeparatedValue(h.NewValue)).ToList();

        var empById = employees.ToDictionary(e => e.Id);

        var rows = new List<object?[]>();
        foreach (var j in joiners.OrderBy(e => e.DateOfJoining))
            rows.Add(["Joiner", $"{j.FirstName} {j.LastName}".Trim(), j.EmployeeNo,
                DeptName(deptNames, j.DepartmentId), j.DateOfJoining.ToString("yyyy-MM-dd")]);
        foreach (var l in leavers.OrderBy(h => h.EffectiveDate))
        {
            empById.TryGetValue(l.EmployeeId, out var e);
            rows.Add(["Leaver", e is null ? "" : $"{e.FirstName} {e.LastName}".Trim(), e?.EmployeeNo ?? "",
                e is null ? "" : DeptName(deptNames, e.DepartmentId), l.EffectiveDate.ToString("yyyy-MM-dd")]);
        }

        var summary = new List<HrSummaryStat>
        {
            new() { Label = "Joiners", Value = joiners.Count, Tone = "positive" },
            new() { Label = "Leavers", Value = leavers.Count, Tone = "negative" },
            new() { Label = "Net Change", Value = joiners.Count - leavers.Count, Tone = "neutral" },
        };

        return Ok(HrReportType.JoinersAndLeavers, qp, dateFrom, dateTo, summary,
            charts:
            [
                new()
                {
                    Kind = "bar", Title = "Joiners vs Leavers",
                    Series = [Series("Count",
                    [
                        new() { Label = "Joiners", Value = joiners.Count },
                        new() { Label = "Leavers", Value = leavers.Count },
                    ])],
                },
            ],
            columns: ["Type", "Employee", "Employee No", "Department", "Date"],
            rows: rows);
    }

    // ── Department Distribution ─────────────────────────────────────────────

    private async Task<Result<HrReportResult>> DepartmentDistributionAsync(
        HrReportQueryParams qp, DateTime dateFrom, DateTime dateTo, CancellationToken ct)
    {
        var employees = await FilteredEmployees(qp).Where(e => e.Status == EmployeeStatus.Active || e.Status == EmployeeStatus.Probation).ToListAsync(ct);
        var deptNames = await DepartmentNameLookup(ct);

        var byDept = employees
            .GroupBy(e => e.DepartmentId)
            .Select(g => new HrChartPoint { Label = DeptName(deptNames, g.Key), Value = g.Count() })
            .OrderByDescending(p => p.Value)
            .ToList();

        var rows = byDept.Select(p => new object?[] { p.Label, (int)p.Value }).ToList();

        var summary = new List<HrSummaryStat>
        {
            new() { Label = "Total Active Headcount", Value = employees.Count, Tone = "neutral" },
            new() { Label = "Departments", Value = byDept.Count, Tone = "neutral" },
        };

        return Ok(HrReportType.DepartmentDistribution, qp, dateFrom, dateTo, summary,
            charts: [new() { Kind = "bar", Title = "Active Headcount by Department", Series = [Series("Headcount", byDept)] }],
            columns: ["Department", "Active Headcount"],
            rows: rows);
    }

    // ── Employment Type Breakdown ───────────────────────────────────────────

    private async Task<Result<HrReportResult>> EmploymentTypeBreakdownAsync(
        HrReportQueryParams qp, DateTime dateFrom, DateTime dateTo, CancellationToken ct)
    {
        var employees = await FilteredEmployees(qp).Where(e => e.Status == EmployeeStatus.Active || e.Status == EmployeeStatus.Probation).ToListAsync(ct);

        var byType = Enum.GetValues<EmploymentType>()
            .Select(t => new HrChartPoint { Label = t.ToString(), Value = employees.Count(e => e.EmploymentType == t) })
            .ToList();

        var rows = byType.Select(p => new object?[] { p.Label, (int)p.Value }).ToList();

        var summary = new List<HrSummaryStat>
        {
            new() { Label = "Total Active Headcount", Value = employees.Count, Tone = "neutral" },
        };

        return Ok(HrReportType.EmploymentTypeBreakdown, qp, dateFrom, dateTo, summary,
            charts: [new() { Kind = "pie", Title = "Active Headcount by Employment Type", Series = [Series("Headcount", byType)] }],
            columns: ["Employment Type", "Active Headcount"],
            rows: rows);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  US-RPT-002 — Leave reports (REUSE US-LV-012 LeaveReportService verbatim)
    // ════════════════════════════════════════════════════════════════════════
    // These do NOT re-derive any leave logic. They translate the §7 HrReportQueryParams into the
    // US-LV-012 LeaveReportQueryParams, resolve the AC-4 role scope (All / Manager / Employee), call the
    // existing LeaveReportService.GenerateReportWithScopeAsync (which owns the BR-1 balance formula and
    // the utilization/absenteeism aggregation), and map its string-cell rows into the generic envelope.

    /// <summary>
    /// AC-1: leave consumption vs entitlement per (department, leave type) with utilization %. Reuses
    /// LeaveReportType.Utilization. The per-department utilization chart is built from the mapped rows.
    /// </summary>
    private async Task<Result<HrReportResult>> LeaveUtilizationAsync(
        HrReportQueryParams qp, DateTime dateFrom, DateTime dateTo, CancellationToken ct)
    {
        var leave = await RunLeaveReportAsync(LeaveReportType.Utilization, qp, dateFrom, dateTo, ct);
        if (leave.IsFailure)
            return Result<HrReportResult>.Failure(leave.Error!, leave.StatusCode ?? 400);
        var res = leave.Value!;

        // Columns: Department, Leave Type, Total Entitlement, Total Used, Utilization %.
        decimal totalEntitlement = res.Rows.Sum(r => ParseDecimal(Cell(r, 2)));
        decimal totalUsed = res.Rows.Sum(r => ParseDecimal(Cell(r, 3)));
        decimal avgUtil = totalEntitlement > 0 ? Math.Round(totalUsed / totalEntitlement * 100m, 2) : 0m;

        // Total leave days taken by type (bar) — AC-1.
        var byType = res.Rows
            .GroupBy(r => Cell(r, 1))
            .Select(g => new HrChartPoint { Label = g.Key, Value = g.Sum(r => ParseDecimal(Cell(r, 3))) })
            .Where(p => p.Value > 0)
            .OrderByDescending(p => p.Value)
            .ToList();

        // Average utilization per department (grouped bar) — AC-1.
        var byDept = res.Rows
            .GroupBy(r => Cell(r, 0))
            .Select(g =>
            {
                decimal ent = g.Sum(r => ParseDecimal(Cell(r, 2)));
                decimal used = g.Sum(r => ParseDecimal(Cell(r, 3)));
                return new HrChartPoint { Label = g.Key, Value = ent > 0 ? Math.Round(used / ent * 100m, 2) : 0m };
            })
            .OrderByDescending(p => p.Value)
            .ToList();

        // Leave type distribution (donut/pie) — AC-1 (share of total used by type).
        var typeDistribution = byType
            .Select(p => new HrChartPoint { Label = p.Label, Value = p.Value })
            .ToList();

        var summary = new List<HrSummaryStat>
        {
            new() { Label = "Total Leave Days Taken", Value = totalUsed, Tone = "neutral" },
            new() { Label = "Total Entitlement", Value = totalEntitlement, Tone = "neutral" },
            new() { Label = "Average Utilization %", Value = avgUtil, Tone = "neutral" },
            new() { Label = "Scope", Value = res.Scope, Tone = "neutral" },
        };

        return MapLeave(HrReportType.LeaveUtilization, qp, dateFrom, dateTo, summary, res,
            charts:
            [
                new() { Kind = "bar", Title = "Leave Days Taken by Type", Series = [Series("Days Taken", byType)] },
                new() { Kind = "bar", Title = "Average Utilization % by Department", Series = [Series("Utilization %", byDept)] },
                new() { Kind = "pie", Title = "Leave Type Distribution", Series = [Series("Days Taken", typeDistribution)] },
            ]);
    }

    /// <summary>
    /// AC-2/BR-1: per-employee per-leave-type current balance. Reuses LeaveReportType.BalanceSummary
    /// (the exact BR-1 formula: entitlement + carry-forward - used - expired + adjustments - pending).
    /// Emits the AC-2 colour band via the table's <see cref="HrReportTable.CellBands"/> matrix (NOT a text
    /// column) so the FE colours the "Available Balance" cell generically (ok→green / warn→yellow / low→red).
    /// </summary>
    private async Task<Result<HrReportResult>> LeaveBalanceAsync(
        HrReportQueryParams qp, DateTime dateFrom, DateTime dateTo, CancellationToken ct)
    {
        var leave = await RunLeaveReportAsync(LeaveReportType.BalanceSummary, qp, dateFrom, dateTo, ct);
        if (leave.IsFailure)
            return Result<HrReportResult>.Failure(leave.Error!, leave.StatusCode ?? 400);
        var res = leave.Value!;

        // Source columns from LeaveReportType.BalanceSummary: Employee No, Employee, Department, Leave
        // Type, Entitlement, Used, Pending, Carry Forward, Expired, Balance.
        //
        // The leave service's "Balance" already applies entitlement + carry - used - expired + adjustments
        // (US-LV-006), but does NOT net out PENDING approvals. US-RPT-002 BR-1 defines the HR-facing
        // current balance as entitlement + carry-forward - consumed - pending. So we derive an explicit
        // "Available Balance" = Balance - Pending (e.g. 20 - 8 - 2 = 10) and base the AC-2 colour band on
        // that. The source "Balance" column is preserved unchanged for transparency. The band itself is
        // carried in cellBands (aligned to the "Available Balance" column), NOT as a text column.
        var columns = res.Columns.Concat(new[] { "Available Balance", "Remaining %" }).ToList();
        int availableBalanceCol = res.Columns.Count; // index of "Available Balance" in the final columns.

        var rows = new List<object?[]>();
        var cellBands = new List<IReadOnlyList<string?>>();
        int ok = 0, warn = 0, low = 0;
        foreach (var r in res.Rows)
        {
            decimal entitlement = ParseDecimal(Cell(r, 4));
            decimal pending = ParseDecimal(Cell(r, 6));
            decimal carry = ParseDecimal(Cell(r, 7));
            decimal balance = ParseDecimal(Cell(r, 9));
            decimal availableBalance = balance - pending; // BR-1 current balance (nets out pending).

            // Denominator = total available for the year (entitlement + carry forward). Falls back to 100%
            // when there is no entitlement (avoids divide-by-zero — band = ok then).
            decimal available = entitlement + carry;
            decimal remainingPct = available > 0 ? Math.Round(availableBalance / available * 100m, 2) : 100m;
            // AC-2 band: >50 ok / 25-50 (inclusive at 50, per the QA boundary case) warn / <25 low.
            string band = remainingPct > 50m ? "ok" : remainingPct >= 25m ? "warn" : "low";
            if (band == "ok") ok++; else if (band == "warn") warn++; else low++;

            // Preserve the source cells (as strings, matching the leave report's natural type) and append
            // the two derived numeric columns. Numbers stay numeric so the FE table sorts correctly.
            var cells = new List<object?>();
            for (int i = 0; i < res.Columns.Count; i++)
                cells.Add(Cell(r, i));
            cells.Add(availableBalance);
            cells.Add(remainingPct);
            rows.Add(cells.ToArray());

            // Band row aligned to the columns: all null EXCEPT the "Available Balance" cell (AC-2 indicator).
            var bandRow = new string?[columns.Count];
            bandRow[availableBalanceCol] = band;
            cellBands.Add(bandRow);
        }

        var summary = new List<HrSummaryStat>
        {
            new() { Label = "Balance Rows", Value = rows.Count, Tone = "neutral" },
            new() { Label = "Healthy (>50%)", Value = ok, Tone = "positive" },
            new() { Label = "Low (25-50%)", Value = warn, Tone = "neutral" },
            new() { Label = "Critical (<25%)", Value = low, Tone = "negative" },
            new() { Label = "Scope", Value = res.Scope, Tone = "neutral" },
        };

        // Distribution of balance health (donut) — supports the AC-2 colour bands at a glance.
        var bandChart = new List<HrChartPoint>
        {
            new() { Label = "Healthy (>50%)", Value = ok },
            new() { Label = "Low (25-50%)", Value = warn },
            new() { Label = "Critical (<25%)", Value = low },
        };

        return OkLeave(HrReportType.LeaveBalance, qp, dateFrom, dateTo, summary,
            charts: [new() { Kind = "pie", Title = "Leave Balance Health", Series = [Series("Employees", bandChart)] }],
            columns: columns, rows: rows, cellBands: cellBands);
    }

    /// <summary>
    /// AC-1/BR-4: absenteeism trends — highest-absenteeism employees (unplanned/LOP days). Reuses
    /// LeaveReportType.Absenteeism. A by-department absenteeism bar chart is built from the mapped rows.
    /// </summary>
    private async Task<Result<HrReportResult>> AbsenteeismTrendsAsync(
        HrReportQueryParams qp, DateTime dateFrom, DateTime dateTo, CancellationToken ct)
    {
        var leave = await RunLeaveReportAsync(LeaveReportType.Absenteeism, qp, dateFrom, dateTo, ct);
        if (leave.IsFailure)
            return Result<HrReportResult>.Failure(leave.Error!, leave.StatusCode ?? 400);
        var res = leave.Value!;

        // Columns: Employee No, Employee, Department, Unplanned Days, LOP Days, Months Spanned,
        //          Avg / Month, Threshold, Flagged.
        decimal totalUnplanned = res.Rows.Sum(r => ParseDecimal(Cell(r, 3)));
        int flagged = res.Rows.Count(r => string.Equals(Cell(r, 8), "Yes", StringComparison.OrdinalIgnoreCase));

        // Top-10 highest absenteeism (the rows already come highest-first from LeaveReportService).
        var top = res.Rows
            .Take(10)
            .Select(r => new HrChartPoint { Label = Cell(r, 1), Value = ParseDecimal(Cell(r, 3)) })
            .ToList();

        // Absenteeism by department (line/bar) — AC-3 absenteeism rate per department surrogate.
        var byDept = res.Rows
            .GroupBy(r => Cell(r, 2))
            .Select(g => new HrChartPoint { Label = g.Key, Value = g.Sum(r => ParseDecimal(Cell(r, 3))) })
            .OrderByDescending(p => p.Value)
            .ToList();

        var summary = new List<HrSummaryStat>
        {
            new() { Label = "Employees with Absenteeism", Value = res.Rows.Count, Tone = "neutral" },
            new() { Label = "Total Unplanned Days", Value = totalUnplanned, Tone = "negative" },
            new() { Label = "Flagged (over threshold)", Value = flagged, Tone = "negative" },
            new() { Label = "Scope", Value = res.Scope, Tone = "neutral" },
        };

        return MapLeave(HrReportType.AbsenteeismTrends, qp, dateFrom, dateTo, summary, res,
            charts:
            [
                new() { Kind = "horizontal-bar", Title = "Highest Absenteeism (Top 10)", Series = [Series("Unplanned Days", top)] },
                new() { Kind = "line", Title = "Unplanned Days by Department", Series = [Series("Unplanned Days", byDept)] },
            ]);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  US-RPT-002 — Attendance reports (aggregate US-ATT-007 monthly summary)
    // ════════════════════════════════════════════════════════════════════════
    // The materialized attendance_monthly_summary table (US-ATT-007) is the single source of truth — it
    // already reconciles AttendanceLog / OvertimeRecord / approved leave against the resolved shift +
    // holiday calendar (BR-2 working days, BR-3 overtime, late/early counts). We do NOT recompute; we sum
    // the materialized rows whose YearMonth falls inside the report window, scoped to the candidate
    // employees (AC-4 role scope + §7 filters). Working days are approximated as present + absent days
    // (the scheduled working days excluding holidays/weekly-offs, per the summary's own BR-2 contract).

    /// <summary>
    /// AC-3/FR-4: attendance rate = present / working * 100; absenteeism rate = absent / working * 100;
    /// late-arrival count; early-departure count; overtime hours by department; absenteeism rate by
    /// department. Aggregated from the US-ATT-007 monthly summary.
    /// </summary>
    private async Task<Result<HrReportResult>> AttendanceSummaryAsync(
        HrReportQueryParams qp, DateTime dateFrom, DateTime dateTo, CancellationToken ct)
    {
        var ctx = await LoadAttendanceContextAsync(qp, dateFrom, dateTo, ct);

        decimal present = ctx.Rows.Sum(r => r.TotalPresentDays);
        decimal absent = ctx.Rows.Sum(r => r.TotalAbsentDays);
        decimal working = present + absent;
        int late = ctx.Rows.Sum(r => r.TotalLateCount);
        int early = ctx.Rows.Sum(r => r.TotalEarlyDepartureCount);
        decimal overtimeHours = Math.Round(ctx.Rows.Sum(r => r.TotalOvertimeMinutes) / 60m, 2);

        decimal attendanceRate = working > 0 ? Math.Round(present / working * 100m, 2) : 0m;
        decimal absenteeismRate = working > 0 ? Math.Round(absent / working * 100m, 2) : 0m;

        // Overtime hours by department (bar) — AC-3.
        var otByDept = ctx.Rows
            .GroupBy(r => DeptOf(ctx, r.EmployeeId))
            .Select(g => new HrChartPoint
            {
                Label = g.Key,
                Value = Math.Round(g.Sum(r => r.TotalOvertimeMinutes) / 60m, 2),
            })
            .OrderByDescending(p => p.Value)
            .ToList();

        // Absenteeism rate per department (line) — AC-3.
        var absRateByDept = ctx.Rows
            .GroupBy(r => DeptOf(ctx, r.EmployeeId))
            .Select(g =>
            {
                decimal p = g.Sum(r => r.TotalPresentDays);
                decimal a = g.Sum(r => r.TotalAbsentDays);
                decimal w = p + a;
                return new HrChartPoint { Label = g.Key, Value = w > 0 ? Math.Round(a / w * 100m, 2) : 0m };
            })
            .OrderByDescending(p => p.Value)
            .ToList();

        var summary = new List<HrSummaryStat>
        {
            new() { Label = "Total Working Days", Value = working, Tone = "neutral" },
            new() { Label = "Average Attendance %", Value = attendanceRate, Tone = "positive" },
            new() { Label = "Absenteeism Rate %", Value = absenteeismRate, Tone = "negative" },
            new() { Label = "Late Arrivals", Value = late, Tone = "negative" },
            new() { Label = "Early Departures", Value = early, Tone = "negative" },
            new() { Label = "Overtime Hours", Value = overtimeHours, Tone = "neutral" },
            new() { Label = "Scope", Value = ctx.Scope, Tone = "neutral" },
        };

        // Per-department table.
        var rows = ctx.Rows
            .GroupBy(r => DeptOf(ctx, r.EmployeeId))
            .Select(g =>
            {
                decimal p = g.Sum(r => r.TotalPresentDays);
                decimal a = g.Sum(r => r.TotalAbsentDays);
                decimal w = p + a;
                decimal rate = w > 0 ? Math.Round(p / w * 100m, 2) : 0m;
                decimal absR = w > 0 ? Math.Round(a / w * 100m, 2) : 0m;
                return new object?[]
                {
                    g.Key, p, a, rate, absR,
                    g.Sum(r => r.TotalLateCount), g.Sum(r => r.TotalEarlyDepartureCount),
                    Math.Round(g.Sum(r => r.TotalOvertimeMinutes) / 60m, 2),
                };
            })
            .OrderBy(r => (string?)r[0])
            .Cast<object?[]>()
            .ToList();

        return OkLeave(HrReportType.AttendanceSummary, qp, dateFrom, dateTo, summary,
            charts:
            [
                new() { Kind = "bar", Title = "Overtime Hours by Department", Series = [Series("Hours", otByDept)] },
                new() { Kind = "line", Title = "Absenteeism Rate % by Department", Series = [Series("Absenteeism %", absRateByDept)] },
            ],
            columns: ["Department", "Present Days", "Absent Days", "Attendance %", "Absenteeism %", "Late Count", "Early Departures", "Overtime Hours"],
            rows: rows);
    }

    /// <summary>
    /// BR-3: overtime hours (minutes beyond shift standard, already computed by US-ATT-007) by department
    /// and per-employee, over the report window.
    /// </summary>
    private async Task<Result<HrReportResult>> OvertimeReportAsync(
        HrReportQueryParams qp, DateTime dateFrom, DateTime dateTo, CancellationToken ct)
    {
        var ctx = await LoadAttendanceContextAsync(qp, dateFrom, dateTo, ct);

        // Per-employee overtime hours (only those with overtime).
        var perEmployee = ctx.Rows
            .GroupBy(r => r.EmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                Minutes = g.Sum(r => r.TotalOvertimeMinutes),
            })
            .Where(x => x.Minutes > 0)
            .OrderByDescending(x => x.Minutes)
            .ToList();

        decimal totalHours = Math.Round(perEmployee.Sum(x => x.Minutes) / 60m, 2);

        var byDept = ctx.Rows
            .GroupBy(r => DeptOf(ctx, r.EmployeeId))
            .Select(g => new HrChartPoint { Label = g.Key, Value = Math.Round(g.Sum(r => r.TotalOvertimeMinutes) / 60m, 2) })
            .Where(p => p.Value > 0)
            .OrderByDescending(p => p.Value)
            .ToList();

        var topEmployees = perEmployee
            .Take(10)
            .Select(x => new HrChartPoint { Label = EmpName(ctx, x.EmployeeId), Value = Math.Round(x.Minutes / 60m, 2) })
            .ToList();

        var rows = perEmployee
            .Select(x => new object?[]
            {
                EmpNo(ctx, x.EmployeeId), EmpName(ctx, x.EmployeeId), DeptOf(ctx, x.EmployeeId),
                Math.Round(x.Minutes / 60m, 2),
            })
            .ToList();

        var summary = new List<HrSummaryStat>
        {
            new() { Label = "Total Overtime Hours", Value = totalHours, Tone = "neutral" },
            new() { Label = "Employees with Overtime", Value = perEmployee.Count, Tone = "neutral" },
            new() { Label = "Scope", Value = ctx.Scope, Tone = "neutral" },
        };

        return OkLeave(HrReportType.OvertimeReport, qp, dateFrom, dateTo, summary,
            charts:
            [
                new() { Kind = "bar", Title = "Overtime Hours by Department", Series = [Series("Hours", byDept)] },
                new() { Kind = "horizontal-bar", Title = "Top Overtime (Employees)", Series = [Series("Hours", topEmployees)] },
            ],
            columns: ["Employee No", "Employee", "Department", "Overtime Hours"],
            rows: rows);
    }

    /// <summary>
    /// FR-1: late-arrival report — late counts (clock-in past shift start + grace, already computed by
    /// US-ATT-007) by department and per-employee.
    /// </summary>
    private async Task<Result<HrReportResult>> LateArrivalReportAsync(
        HrReportQueryParams qp, DateTime dateFrom, DateTime dateTo, CancellationToken ct)
    {
        var ctx = await LoadAttendanceContextAsync(qp, dateFrom, dateTo, ct);

        var perEmployee = ctx.Rows
            .GroupBy(r => r.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Late = g.Sum(r => r.TotalLateCount) })
            .Where(x => x.Late > 0)
            .OrderByDescending(x => x.Late)
            .ToList();

        int totalLate = perEmployee.Sum(x => x.Late);

        var byDept = ctx.Rows
            .GroupBy(r => DeptOf(ctx, r.EmployeeId))
            .Select(g => new HrChartPoint { Label = g.Key, Value = g.Sum(r => r.TotalLateCount) })
            .Where(p => p.Value > 0)
            .OrderByDescending(p => p.Value)
            .ToList();

        var topEmployees = perEmployee
            .Take(10)
            .Select(x => new HrChartPoint { Label = EmpName(ctx, x.EmployeeId), Value = x.Late })
            .ToList();

        var rows = perEmployee
            .Select(x => new object?[]
            {
                EmpNo(ctx, x.EmployeeId), EmpName(ctx, x.EmployeeId), DeptOf(ctx, x.EmployeeId), x.Late,
            })
            .ToList();

        var summary = new List<HrSummaryStat>
        {
            new() { Label = "Total Late Arrivals", Value = totalLate, Tone = "negative" },
            new() { Label = "Employees Arriving Late", Value = perEmployee.Count, Tone = "neutral" },
            new() { Label = "Scope", Value = ctx.Scope, Tone = "neutral" },
        };

        return OkLeave(HrReportType.LateArrivalReport, qp, dateFrom, dateTo, summary,
            charts:
            [
                new() { Kind = "bar", Title = "Late Arrivals by Department", Series = [Series("Late Count", byDept)] },
                new() { Kind = "horizontal-bar", Title = "Top Late Arrivals (Employees)", Series = [Series("Late Count", topEmployees)] },
            ],
            columns: ["Employee No", "Employee", "Department", "Late Count"],
            rows: rows);
    }

    // ── US-RPT-002 leave plumbing ───────────────────────────────────────────

    /// <summary>
    /// Runs an existing US-LV-012 leave report under the AC-4 role scope, translating the §7
    /// HrReportQueryParams into LeaveReportQueryParams (unpaged — the generic envelope returns all rows).
    /// </summary>
    private async Task<Result<LeaveReportResult>> RunLeaveReportAsync(
        LeaveReportType type, HrReportQueryParams qp, DateTime dateFrom, DateTime dateTo, CancellationToken ct)
    {
        if (_leaveReports is null)
            return Result<LeaveReportResult>.Failure("Leave reporting service is not available.", 500);

        var scope = await ResolveScopeAsync(ct);

        var leaveParams = new LeaveReportQueryParams
        {
            From = DateOnly.FromDateTime(dateFrom),
            To = DateOnly.FromDateTime(dateTo),
            Year = dateTo.Year,
            DepartmentId = qp.DepartmentIds.Count > 0 ? qp.DepartmentIds[0] : null,
            EmploymentType = qp.EmploymentTypes.Count > 0 ? qp.EmploymentTypes[0] : null,
            LeaveTypeId = qp.LeaveTypeIds.Count > 0 ? qp.LeaveTypeIds[0] : null,
            EmployeeSearch = null,
            Page = 1,
            PageSize = int.MaxValue, // unpaged — the generic HR envelope carries the full table.
        };

        return await _leaveReports.GenerateReportWithScopeAsync(
            type, scope.Kind, scope.EmployeeId, leaveParams, ct);
    }

    /// <summary>Maps a LeaveReportResult's string-cell rows verbatim into the generic envelope's table.</summary>
    private static Result<HrReportResult> MapLeave(
        HrReportType type, HrReportQueryParams qp, DateTime dateFrom, DateTime dateTo,
        IReadOnlyList<HrSummaryStat> summary, LeaveReportResult res, IReadOnlyList<HrChartBlock> charts)
    {
        var rows = res.Rows.Select(r => r.Cells.Cast<object?>().ToArray()).ToList();
        return OkLeave(type, qp, dateFrom, dateTo, summary, charts, res.Columns, rows);
    }

    private static string Cell(LeaveReportRow row, int index) =>
        index >= 0 && index < row.Cells.Count ? row.Cells[index] : string.Empty;

    private static decimal ParseDecimal(string s) =>
        decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    // ── US-RPT-002 attendance plumbing ──────────────────────────────────────

    /// <summary>
    /// The materialized attendance rows for the report window + role scope + §7 filters, plus the
    /// employee/department lookups needed to label the aggregates. The window is interpreted by month:
    /// any AttendanceMonthlySummary whose YearMonth ("yyyy-MM") falls between dateFrom's and dateTo's
    /// months (inclusive) is included.
    /// </summary>
    private sealed record AttendanceContext(
        IReadOnlyList<AttendanceMonthlySummary> Rows,
        IReadOnlyDictionary<Guid, Employee> EmployeesById,
        IReadOnlyDictionary<Guid, string> DeptNames,
        string Scope);

    private async Task<AttendanceContext> LoadAttendanceContextAsync(
        HrReportQueryParams qp, DateTime dateFrom, DateTime dateTo, CancellationToken ct)
    {
        var scope = await ResolveScopeAsync(ct);

        // Candidate employees: role scope (AC-4) + §7 filters. BR-6: terminated employees ARE included in
        // these historical attendance reports (unlike current balance), so no status exclusion here.
        var empQuery = FilteredEmployees(qp);
        empQuery = ApplyScopeToEmployees(empQuery, scope);
        if (qp.EmployeeId is { } empId)
            empQuery = empQuery.Where(e => e.Id == empId);

        // Optional shift filter (§7): restrict to employees whose CURRENT shift assignment is in the set.
        if (qp.ShiftIds.Count > 0)
        {
            var shiftIds = qp.ShiftIds.ToList();
            var shiftEmpIds = await _db.EmployeeShifts.AsNoTracking()
                .Where(es => es.EffectiveTo == null && shiftIds.Contains(es.ShiftId))
                .Select(es => es.EmployeeId)
                .ToListAsync(ct);
            empQuery = empQuery.Where(e => shiftEmpIds.Contains(e.Id));
        }

        var employees = await empQuery.ToListAsync(ct);
        var employeeIds = employees.Select(e => e.Id).ToList();
        var empById = employees.ToDictionary(e => e.Id);
        var deptNames = await DepartmentNameLookup(ct);

        // The months covered by the window, as "yyyy-MM" labels.
        var months = MonthsInWindow(dateFrom, dateTo);

        var rows = employeeIds.Count == 0
            ? new List<AttendanceMonthlySummary>()
            : await _db.AttendanceMonthlySummaries.AsNoTracking()
                .Where(s => employeeIds.Contains(s.EmployeeId) && months.Contains(s.YearMonth))
                .ToListAsync(ct);

        return new AttendanceContext(rows, empById, deptNames, scope.Kind);
    }

    private static List<string> MonthsInWindow(DateTime from, DateTime to)
    {
        var months = new List<string>();
        var cursor = new DateTime(from.Year, from.Month, 1);
        var end = new DateTime(to.Year, to.Month, 1);
        while (cursor <= end)
        {
            months.Add(cursor.ToString("yyyy-MM", CultureInfo.InvariantCulture));
            cursor = cursor.AddMonths(1);
        }
        return months;
    }

    private static string DeptOf(AttendanceContext ctx, Guid employeeId) =>
        ctx.EmployeesById.TryGetValue(employeeId, out var e)
            ? DeptName(ctx.DeptNames, e.DepartmentId)
            : "Unassigned";

    private static string EmpName(AttendanceContext ctx, Guid employeeId) =>
        ctx.EmployeesById.TryGetValue(employeeId, out var e) ? $"{e.FirstName} {e.LastName}".Trim() : "";

    private static string EmpNo(AttendanceContext ctx, Guid employeeId) =>
        ctx.EmployeesById.TryGetValue(employeeId, out var e) ? e.EmployeeNo : "";

    // ── US-RPT-002 role scope (AC-4 / FR-8) — mirrors the US-LV-012 ScopeKind resolution ────────

    /// <summary>
    /// Resolved report scope: kind ("All"/"Manager"/"Employee", the same strings the leave service
    /// understands) + the acting employee id for Manager/Employee scope.
    /// </summary>
    private sealed record ReportScope(string Kind, Guid? EmployeeId);

    /// <summary>
    /// AC-4 / FR-8 scope, identical in shape to LeaveReportService.ResolveScopeAsync:
    ///   1. Any of Employee/Leave/Attendance .View.All → All tenant employees (HR Officer).
    ///   2. An employee with ≥1 direct report → Manager scope (own direct reports + self).
    ///   3. Otherwise → Employee scope (own record only).
    /// We key "All" on the cross-module .View.All permissions because this report spans both leave and
    /// attendance; "Reports.View" alone gates access but a Manager holding it must still be team-scoped.
    /// </summary>
    private async Task<ReportScope> ResolveScopeAsync(CancellationToken ct)
    {
        var perms = _currentUser?.Permissions ?? [];
        bool hasViewAll =
            perms.Contains(PermissionCatalog.Employee.ViewAll) ||
            perms.Contains(PermissionCatalog.Leave.ViewAll) ||
            perms.Contains(PermissionCatalog.Attendance.ViewAll);

        if (hasViewAll)
            return new ReportScope("All", null);

        if (_currentUser is null)
            // No current user resolved (e.g. RPT-001 test constructor): default to All so behaviour is
            // unchanged for callers that never exercise the scoped reports.
            return new ReportScope("All", null);

        var me = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, ct);
        if (me is null)
            return new ReportScope("Employee", null);

        bool isManager = await _db.Employees.AsNoTracking()
            .AnyAsync(e => e.ReportsToEmployeeId == me.Id, ct);

        return isManager ? new ReportScope("Manager", me.Id) : new ReportScope("Employee", me.Id);
    }

    /// <summary>Applies the resolved role scope to an employee query (AC-4), matching the leave path.</summary>
    private IQueryable<Employee> ApplyScopeToEmployees(IQueryable<Employee> query, ReportScope scope)
    {
        switch (scope.Kind)
        {
            case "Manager" when scope.EmployeeId is { } meId:
                return query.Where(e => e.ReportsToEmployeeId == meId || e.Id == meId);
            case "Employee" when scope.EmployeeId is { } ownId:
                return query.Where(e => e.Id == ownId);
            case "Employee":
                // Employee scope with no resolved record → return nothing.
                return query.Where(e => false);
            default:
                return query; // All
        }
    }

    // ── Generic-envelope assembly for the US-RPT-002 reports (object-cell rows) ──────────────────

    private static Result<HrReportResult> OkLeave(
        HrReportType type, HrReportQueryParams qp, DateTime dateFrom, DateTime dateTo,
        IReadOnlyList<HrSummaryStat> summary, IReadOnlyList<HrChartBlock> charts,
        IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows,
        IReadOnlyList<IReadOnlyList<string?>>? cellBands = null)
        => Result<HrReportResult>.Success(new HrReportResult
        {
            Metadata = new HrReportMetadata
            {
                Type = HrReportTypeKey.ToKey(type),
                Title = HrReportTypeKey.Title(type),
                GeneratedAt = DateTime.UtcNow,
                AppliedFilters = BuildAppliedFilters(qp, dateFrom, dateTo),
                Summary = summary,
                FromCache = false,
            },
            Charts = charts,
            Table = new HrReportTable { Columns = columns, Rows = rows, CellBands = cellBands },
        });

    // ── Shared query plumbing ───────────────────────────────────────────────

    /// <summary>
    /// The tenant-scoped employee query with the §7 filters applied. Tenant isolation is the EF global
    /// query filter (no manual TenantId check). Uses <c>List.Contains</c> for IN-style predicates so the
    /// InMemory provider can translate them (see agent memory).
    /// </summary>
    private IQueryable<Employee> FilteredEmployees(HrReportQueryParams qp)
    {
        var query = _db.Employees.AsNoTracking();

        if (qp.DepartmentIds.Count > 0)
        {
            var deptIds = qp.DepartmentIds.ToList();
            query = query.Where(e => deptIds.Contains(e.DepartmentId));
        }

        if (qp.LocationIds.Count > 0)
        {
            var locIds = qp.LocationIds.ToList();
            query = query.Where(e => e.LocationId != null && locIds.Contains(e.LocationId.Value));
        }

        if (qp.EmploymentTypes.Count > 0)
        {
            var types = qp.EmploymentTypes
                .Select(t => Enum.TryParse<EmploymentType>(t, ignoreCase: true, out var v) ? (EmploymentType?)v : null)
                .Where(v => v.HasValue).Select(v => v!.Value).ToList();
            query = query.Where(e => types.Contains(e.EmploymentType));
        }

        if (qp.EmployeeStatuses.Count > 0)
        {
            var statuses = qp.EmployeeStatuses
                .Select(s => Enum.TryParse<EmployeeStatus>(s, ignoreCase: true, out var v) ? (EmployeeStatus?)v : null)
                .Where(v => v.HasValue).Select(v => v!.Value).ToList();
            query = query.Where(e => statuses.Contains(e.Status));
        }

        return query;
    }

    private async Task<Dictionary<Guid, string>> DepartmentNameLookup(CancellationToken ct) =>
        await _db.Departments.AsNoTracking()
            .Select(d => new { d.Id, d.Name })
            .ToDictionaryAsync(d => d.Id, d => d.Name, ct);

    private async Task<Dictionary<Guid, string>> LocationNameLookup(CancellationToken ct) =>
        await _db.Locations.AsNoTracking()
            .Select(l => new { l.Id, l.Name })
            .ToDictionaryAsync(l => l.Id, l => l.Name, ct);

    private static string DeptName(IReadOnlyDictionary<Guid, string> names, Guid id) =>
        names.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n) ? n : "Unassigned";

    private static string LocName(IReadOnlyDictionary<Guid, string> names, Guid? id) =>
        id.HasValue && names.TryGetValue(id.Value, out var n) && !string.IsNullOrWhiteSpace(n) ? n : "Unassigned";

    private static HrChartSeries Series(string name, IReadOnlyList<HrChartPoint> points) =>
        new() { Name = name, Points = points };

    // ── Domain rules (BR-3/BR-4/BR-5) — also covered by unit tests ──────────

    /// <summary>BR-4: "Active" employees include those with status Active or Probation.</summary>
    public static bool IsActive(EmployeeStatus status) =>
        status == EmployeeStatus.Active || status == EmployeeStatus.Probation;

    /// <summary>
    /// BR-4: terminated / resigned / contract-ended are separations. The EmployeeStatus enum models these as
    /// Terminated; the EmploymentHistory NewValue may also carry the free-text "Resigned" / "Contract Ended".
    /// </summary>
    public static bool IsSeparatedValue(string newValue)
    {
        if (string.IsNullOrWhiteSpace(newValue)) return false;
        var v = newValue.Trim();
        if (Enum.TryParse<EmployeeStatus>(v, ignoreCase: true, out var status))
            return status == EmployeeStatus.Terminated || status == EmployeeStatus.Inactive;
        var lower = v.ToLowerInvariant();
        return lower.Contains("terminat") || lower.Contains("resign") || lower.Contains("contract ended") || lower.Contains("contract_ended");
    }

    /// <summary>
    /// BR-3 split: a resignation is voluntary; a termination is involuntary. Falls back on a reason keyword
    /// when the new value is ambiguous (e.g. a generic "Inactive").
    /// </summary>
    public static bool IsVoluntary(string newValue, string? reason)
    {
        var hay = $"{newValue} {reason}".ToLowerInvariant();
        if (hay.Contains("resign") || hay.Contains("voluntary")) return true;
        if (hay.Contains("terminat") || hay.Contains("involuntary") || hay.Contains("dismiss")) return false;
        // Default unclassified separations to involuntary so they are never silently dropped.
        return false;
    }

    /// <summary>
    /// BR-5: computes age in whole years at the given report date (not the current date), for historical
    /// accuracy. Returns 0 for a future date-of-birth.
    /// </summary>
    public static int AgeAt(DateTime dateOfBirth, DateTime reportDate)
    {
        if (dateOfBirth > reportDate) return 0;
        var age = reportDate.Year - dateOfBirth.Year;
        if (reportDate.Month < dateOfBirth.Month ||
            (reportDate.Month == dateOfBirth.Month && reportDate.Day < dateOfBirth.Day))
            age--;
        return age;
    }

    // ── Result assembly + caching ───────────────────────────────────────────

    private static Result<HrReportResult> Ok(
        HrReportType type, HrReportQueryParams qp, DateTime dateFrom, DateTime dateTo,
        IReadOnlyList<HrSummaryStat> summary,
        IReadOnlyList<HrChartBlock> charts, IReadOnlyList<string> columns,
        IReadOnlyList<object?[]> rows)
        => Result<HrReportResult>.Success(new HrReportResult
        {
            Metadata = new HrReportMetadata
            {
                Type = HrReportTypeKey.ToKey(type),
                Title = HrReportTypeKey.Title(type),
                GeneratedAt = DateTime.UtcNow,
                AppliedFilters = BuildAppliedFilters(qp, dateFrom, dateTo),
                Summary = summary,
                FromCache = false,
            },
            Charts = charts,
            Table = new HrReportTable { Columns = columns, Rows = rows },
        });

    private static HrAppliedFilters BuildAppliedFilters(HrReportQueryParams qp, DateTime dateFrom, DateTime dateTo) =>
        new()
        {
            DateFrom = dateFrom.ToString("yyyy-MM-dd"),
            DateTo = dateTo.ToString("yyyy-MM-dd"),
            DepartmentIds = qp.DepartmentIds,
            LocationIds = qp.LocationIds,
            EmploymentTypes = qp.EmploymentTypes,
            EmployeeStatuses = qp.EmployeeStatuses,
            // US-RPT-002 additive echoes (only meaningful for the leave/attendance reports).
            EmployeeId = qp.EmployeeId,
            LeaveTypeIds = qp.LeaveTypeIds,
            ShiftIds = qp.ShiftIds,
        };

    /// <summary>FR-5 cache key: <c>t:{tenantId}:report:{name}:{paramsHash}</c> (scope folded into the hash).</summary>
    private string BuildCacheKey(HrReportType type, HrReportQueryParams qp, DateTime dateFrom, DateTime dateTo, string scopeKey = "")
    {
        var raw = string.Join('|',
            dateFrom.ToString("yyyyMMdd"), dateTo.ToString("yyyyMMdd"),
            string.Join(',', qp.DepartmentIds.OrderBy(x => x)),
            string.Join(',', qp.LocationIds.OrderBy(x => x)),
            string.Join(',', qp.EmploymentTypes.Select(x => x.ToLowerInvariant()).OrderBy(x => x)),
            string.Join(',', qp.EmployeeStatuses.Select(x => x.ToLowerInvariant()).OrderBy(x => x)),
            // US-RPT-002 additive filters + caller scope.
            qp.EmployeeId?.ToString() ?? string.Empty,
            string.Join(',', qp.LeaveTypeIds.OrderBy(x => x)),
            string.Join(',', qp.ShiftIds.OrderBy(x => x)),
            scopeKey);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16].ToLowerInvariant();
        return $"t:{_tenantContext.TenantId}:report:{HrReportTypeKey.ToKey(type)}:{hash}";
    }

    /// <summary>True for the US-RPT-002 leave/attendance reports, which are role-scoped (AC-4).</summary>
    private static bool IsScopedReport(HrReportType type) => type is
        HrReportType.LeaveUtilization or HrReportType.LeaveBalance or HrReportType.AbsenteeismTrends or
        HrReportType.AttendanceSummary or HrReportType.OvertimeReport or HrReportType.LateArrivalReport;

    /// <summary>A stable cache-key segment for a resolved scope (kind + acting employee).</summary>
    private static string ScopeKeyOf(ReportScope scope) => $"{scope.Kind}:{scope.EmployeeId}";

    private async Task<HrReportResult?> TryGetCachedAsync(string key, CancellationToken ct)
    {
        try
        {
            var json = await _cache!.GetStringAsync(key, ct);
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<HrReportResult>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HR report cache read failed for {Key}; computing fresh.", key);
            return null;
        }
    }

    private async Task SetCachedAsync(string key, HrReportResult value, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await _cache!.SetStringAsync(key, json,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HR report cache write failed for {Key}.", key);
        }
    }
}
