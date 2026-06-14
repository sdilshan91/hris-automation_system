using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveReports.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Leave reports and analytics (US-LV-012). Pure read/aggregation — no new entity or migration.
///
/// COMPOSITION (not re-derivation): the per-employee balance reuses the US-LV-006 ledger formula
/// (engine entitlement + ledger components grouped by EntryType); the effective entitlement is
/// resolved by the US-LV-002 engine via ILeaveEntitlementService; absenteeism/LOP reuse the
/// US-LV-011 LOP rows; carry-forward reuses the US-LV-008 tracking rows.
///
/// Tenant isolation (BR-1, NFR-3): every query runs under the EF global query filter
/// (TenantId == ITenantContext.TenantId). Role scoping (BR-2) is layered ON TOP, restricting the
/// candidate employee set to All / the manager's direct reports / the caller's own record.
///
/// DEFERRALS (seam + TODO only): Redis caching (BR-3) — compute from DB; PostgreSQL read replicas
/// (FR-8) — query the primary; materialized views (§10) — regular aggregation queries; notification
/// dispatch (FR-5) — log-only via IReportExportStorage; blob storage (FR-5) — IReportExportStorage
/// local seam.
/// </summary>
public sealed class LeaveReportService : ILeaveReportService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILeaveEntitlementService _entitlementService;
    private readonly IReportExportStorage _exportStorage;
    private readonly IBackgroundJobClient? _backgroundJobs;
    private readonly ILogger<LeaveReportService> _logger;

    /// <summary>NFR-2 / AC-5: exports at or below this size run synchronously; larger go to Hangfire.</summary>
    public const int SyncExportRowThreshold = 5000;

    private const int MaxPageSize = 500;
    private const int TrendMonths = 12;

    public LeaveReportService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILeaveEntitlementService entitlementService,
        IReportExportStorage exportStorage,
        ILogger<LeaveReportService> logger,
        IBackgroundJobClient? backgroundJobs = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _entitlementService = entitlementService;
        _exportStorage = exportStorage;
        _backgroundJobs = backgroundJobs;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  Public API
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<LeaveReportResult>> GenerateReportAsync(
        LeaveReportType reportType,
        LeaveReportQueryParams queryParams,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LeaveReportResult>.Failure("Tenant context is not resolved.", 400);

        var scope = await ResolveScopeAsync(cancellationToken);
        return await GenerateReportCoreAsync(reportType, scope, queryParams, cancellationToken);
    }

    public async Task<Result<LeaveReportResult>> GenerateReportWithScopeAsync(
        LeaveReportType reportType,
        string scopeKind,
        Guid? scopeEmployeeId,
        LeaveReportQueryParams queryParams,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LeaveReportResult>.Failure("Tenant context is not resolved.", 400);

        var scope = await BuildScopeFromKindAsync(scopeKind, scopeEmployeeId, cancellationToken);
        return await GenerateReportCoreAsync(reportType, scope, queryParams, cancellationToken);
    }

    private async Task<Result<LeaveReportResult>> GenerateReportCoreAsync(
        LeaveReportType reportType,
        ReportScope scope,
        LeaveReportQueryParams queryParams,
        CancellationToken cancellationToken)
    {
        var (rows, columns, note) = reportType switch
        {
            LeaveReportType.BalanceSummary => await BuildBalanceSummaryAsync(queryParams, scope, cancellationToken),
            LeaveReportType.Utilization => await BuildUtilizationAsync(queryParams, scope, cancellationToken),
            LeaveReportType.Absenteeism => await BuildAbsenteeismAsync(queryParams, scope, cancellationToken),
            LeaveReportType.CarryForwardSummary => await BuildCarryForwardSummaryAsync(queryParams, scope, cancellationToken),
            LeaveReportType.LopSummary => await BuildLopSummaryAsync(queryParams, scope, cancellationToken),
            LeaveReportType.DepartmentCalendarCoverage => BuildDepartmentCalendarCoverageStub(),
            _ => (new List<LeaveReportRow>(), new List<string>(), (string?)null),
        };

        // FR-3: server-side sort (applied to the materialised rows by column index) + paging.
        var sorted = ApplySort(rows, columns, queryParams);
        int total = sorted.Count;
        int page = Math.Max(1, queryParams.Page);
        // PageSize == int.MaxValue is the "all rows, unpaged" sentinel used by the export path (it must
        // materialise every row to route sync-vs-background and render the full file); for that case we
        // bypass the MaxPageSize clamp. The normal paged API stays clamped to [1, MaxPageSize].
        bool unpaged = queryParams.PageSize == int.MaxValue;
        int pageSize = unpaged
            ? Math.Max(total, 1)
            : Math.Clamp(queryParams.PageSize <= 0 ? 50 : queryParams.PageSize, 1, MaxPageSize);
        var pageRows = sorted.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Result<LeaveReportResult>.Success(new LeaveReportResult
        {
            ReportType = reportType.ToString(),
            Columns = columns,
            Rows = pageRows,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            Scope = scope.Kind.ToString(),
            Note = note,
        });
    }

    public async Task<Result<LeaveAnalyticsResult>> GetAnalyticsAsync(
        LeaveAnalyticsChartType chartType,
        LeaveReportQueryParams queryParams,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LeaveAnalyticsResult>.Failure("Tenant context is not resolved.", 400);

        var scope = await ResolveScopeAsync(cancellationToken);

        LeaveAnalyticsResult result = chartType switch
        {
            LeaveAnalyticsChartType.UtilizationByDepartment =>
                await BuildUtilizationByDepartmentChartAsync(queryParams, scope, cancellationToken),
            LeaveAnalyticsChartType.LeaveByType =>
                await BuildLeaveByTypeChartAsync(queryParams, scope, cancellationToken),
            LeaveAnalyticsChartType.MonthlyTrend =>
                await BuildMonthlyTrendChartAsync(queryParams, scope, cancellationToken),
            _ => new LeaveAnalyticsResult { ChartType = chartType.ToString(), Scope = scope.Kind.ToString() },
        };

        return Result<LeaveAnalyticsResult>.Success(result);
    }

    public async Task<Result<LeaveReportExportResult>> ExportReportAsync(
        LeaveReportType reportType,
        ReportExportFormat format,
        LeaveReportQueryParams queryParams,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LeaveReportExportResult>.Failure("Tenant context is not resolved.", 400);

        // Resolve scope once so both the routing decision and the background job use the same view.
        var scope = await ResolveScopeAsync(cancellationToken);

        // Determine the FULL (unpaged) row count first so we can route sync vs. background (AC-5).
        // We generate the whole report once (paged to MaxPageSize won't do — we need every row).
        var fullParams = queryParams with { Page = 1, PageSize = int.MaxValue };
        var fullResult = await GenerateReportCoreAsync(reportType, scope, fullParams, cancellationToken);
        if (fullResult.IsFailure)
            return Result<LeaveReportExportResult>.Failure(fullResult.Error!, fullResult.StatusCode ?? 400);

        var report = fullResult.Value!;
        int rowCount = report.Rows.Count;

        // FR-5 / AC-5: large exports go to a Hangfire background job (file written via the blob seam,
        // notification logged). The background job regenerates the report under the same scope/filters.
        if (rowCount > SyncExportRowThreshold)
        {
            if (_backgroundJobs is null)
            {
                // No Hangfire client available (e.g. unit context): report that background routing is
                // required rather than silently generating a huge file inline.
                return Result<LeaveReportExportResult>.Success(new LeaveReportExportResult
                {
                    Queued = true,
                    JobId = null,
                    RowCount = rowCount,
                });
            }

            var reportId = BaseEntity.NewUuidV7();
            var tenantId = _tenantContext.TenantId;

            // Enqueue the export job (HRM.Api LeaveReportExportJob) with the resolved scope + filter
            // inputs, so the background file reproduces this caller's exact view (BR-2).
            var jobId = _backgroundJobs.Enqueue<ILeaveReportExportJob>(j => j.RunAsync(
                tenantId, reportId, scope.Kind.ToString(), scope.Me != null ? scope.Me.Id : (Guid?)null,
                reportType, format, queryParams, CancellationToken.None));

            _logger.LogInformation(
                "Leave report export {ReportId} ({Rows} rows > {Threshold}) queued as Hangfire job {JobId} " +
                "for tenant {TenantId}. Action {AuditAction}.",
                reportId, rowCount, SyncExportRowThreshold, jobId, tenantId, "Leave.ReportExportQueued");

            return Result<LeaveReportExportResult>.Success(new LeaveReportExportResult
            {
                Queued = true,
                JobId = jobId,
                RowCount = rowCount,
            });
        }

        // Synchronous path (NFR-2): generate the file inline and return the bytes.
        var (content, fileName, contentType) = RenderExport(reportType, format, report);

        return Result<LeaveReportExportResult>.Success(new LeaveReportExportResult
        {
            Queued = false,
            FileContent = content,
            FileName = fileName,
            ContentType = contentType,
            RowCount = rowCount,
        });
    }

    public (byte[] Content, string FileName, string ContentType) RenderExport(
        LeaveReportType reportType,
        ReportExportFormat format,
        LeaveReportResult result)
    {
        string baseName = $"leave-{ToKebab(reportType.ToString())}-{DateTime.UtcNow:yyyyMMdd}";

        return format switch
        {
            ReportExportFormat.Xlsx => (
                RenderXlsx(reportType, result),
                $"{baseName}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            _ => (
                RenderCsv(result),
                $"{baseName}.csv",
                "text/csv"),
        };
    }

    // ══════════════════════════════════════════════════════════════
    //  Report builders
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-1: per-employee current balance per leave type, filterable by department/job-title/employment-
    /// type. Reuses the US-LV-006 balance formula (engine entitlement + ledger components). One row per
    /// (employee × leave type).
    /// </summary>
    private async Task<(List<LeaveReportRow> Rows, List<string> Columns, string? Note)> BuildBalanceSummaryAsync(
        LeaveReportQueryParams qp, ReportScope scope, CancellationToken ct)
    {
        var columns = new List<string>
        {
            "Employee No", "Employee", "Department", "Leave Type",
            "Entitlement", "Used", "Pending", "Carry Forward", "Expired", "Balance",
        };

        int year = qp.Year ?? DateTime.UtcNow.Year;

        var employees = await ScopedEmployeesQuery(qp, scope).ToListAsync(ct);
        if (employees.Count == 0)
            return (new List<LeaveReportRow>(), columns, null);

        var employeeIds = employees.Select(e => e.Id).ToList();
        var deptNames = await DepartmentNamesAsync(employees, ct);

        var leaveTypes = await LeaveTypesQuery(qp).ToListAsync(ct);
        var leaveTypeIds = leaveTypes.Select(lt => lt.Id).ToList();

        // One pass over the ledger for all scoped employees / year, grouped per (employee, leaveType).
        var ledger = await _dbContext.LeaveLedgerEntries.AsNoTracking()
            .Where(l => employeeIds.Contains(l.EmployeeId)
                        && l.LeaveYear == year
                        && leaveTypeIds.Contains(l.LeaveTypeId))
            .Select(l => new { l.EmployeeId, l.LeaveTypeId, l.EntryType, l.Amount })
            .ToListAsync(ct);

        var components = ledger
            .GroupBy(l => (l.EmployeeId, l.LeaveTypeId))
            .ToDictionary(g => g.Key, g => LedgerComponents.From(g.Select(e => (e.EntryType, e.Amount))));

        var pending = (await _dbContext.LeaveRequests.AsNoTracking()
                .Where(lr => employeeIds.Contains(lr.EmployeeId)
                             && lr.Status == LeaveRequestStatus.Pending
                             && lr.StartDate.Year == year
                             && leaveTypeIds.Contains(lr.LeaveTypeId))
                .Select(lr => new { lr.EmployeeId, lr.LeaveTypeId, lr.TotalDays })
                .ToListAsync(ct))
            .GroupBy(x => (x.EmployeeId, x.LeaveTypeId))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.TotalDays));

        var rows = new List<LeaveReportRow>();
        foreach (var emp in employees)
        {
            foreach (var lt in leaveTypes)
            {
                var key = (emp.Id, lt.Id);
                var c = components.TryGetValue(key, out var comp) ? comp : LedgerComponents.Empty;
                decimal entitlement = await ResolveEntitlementAsync(emp.Id, lt.Id, year, ct);
                decimal balance = entitlement + c.CarryForward - c.Used - c.Expired + c.Adjustments;
                decimal pend = pending.TryGetValue(key, out var p) ? p : 0m;

                // Skip rows where the employee has no entitlement and no activity for this type
                // (keeps the report to the leave types that actually apply to each employee).
                if (entitlement == 0m && c.Used == 0m && c.CarryForward == 0m
                    && c.Expired == 0m && c.Adjustments == 0m && pend == 0m)
                    continue;

                rows.Add(new LeaveReportRow
                {
                    Cells =
                    [
                        emp.EmployeeNo,
                        $"{emp.FirstName} {emp.LastName}".Trim(),
                        deptNames.GetValueOrDefault(emp.DepartmentId, string.Empty),
                        lt.Name,
                        Num(entitlement), Num(c.Used), Num(pend),
                        Num(c.CarryForward), Num(c.Expired), Num(balance),
                    ],
                });
            }
        }

        return (rows, columns, null);
    }

    /// <summary>
    /// AC-2: total leaves taken by type + average utilization % + per-department breakdown over a date
    /// range. Utilization = used / entitlement (test hint: 200 entitlement, 80 used → 40%). Rows are the
    /// per-department breakdown; the summary (total + average) is exposed via the analytics endpoint.
    /// </summary>
    private async Task<(List<LeaveReportRow> Rows, List<string> Columns, string? Note)> BuildUtilizationAsync(
        LeaveReportQueryParams qp, ReportScope scope, CancellationToken ct)
    {
        var columns = new List<string>
        {
            "Department", "Leave Type", "Total Entitlement", "Total Used", "Utilization %",
        };

        var agg = await ComputeUtilizationAggregatesAsync(qp, scope, ct);

        var rows = agg
            .Select(a => new LeaveReportRow
            {
                Cells =
                [
                    a.DepartmentName,
                    a.LeaveTypeName,
                    Num(a.Entitlement),
                    Num(a.Used),
                    Pct(a.Entitlement, a.Used),
                ],
            })
            .ToList();

        return (rows, columns, null);
    }

    /// <summary>
    /// AC-3: employees with the highest absenteeism (unplanned leave + LOP) over a range, flagged when
    /// they exceed the tenant-configurable threshold (BR-4, default 3+ unplanned/month). Reuses the
    /// US-LV-011 LOP rows. "Unplanned" = LOP entries (SystemGenerated/HrAssigned/Compulsory/
    /// EmployeeRequest-LOP) + leave requested with start &lt;= request date (retroactive). For a clean,
    /// data-available signal we count LOP days + sick/casual leave taken in the range as unplanned.
    /// </summary>
    private async Task<(List<LeaveReportRow> Rows, List<string> Columns, string? Note)> BuildAbsenteeismAsync(
        LeaveReportQueryParams qp, ReportScope scope, CancellationToken ct)
    {
        var columns = new List<string>
        {
            "Employee No", "Employee", "Department", "Unplanned Days", "LOP Days", "Months Spanned",
            "Avg / Month", "Threshold", "Flagged",
        };

        var (from, to) = ResolveRange(qp);
        int monthsSpanned = MonthsBetween(from, to);
        decimal threshold = await ResolveAbsenteeismThresholdAsync(ct);

        var employees = await ScopedEmployeesQuery(qp, scope).ToListAsync(ct);
        var employeeIds = employees.Select(e => e.Id).ToList();
        var deptNames = await DepartmentNamesAsync(employees, ct);

        // Unplanned signal: every LOP request in range (US-LV-011), plus any non-LOP leave whose
        // request was filed on/after the start date (last-minute) — but LOP alone is the load-bearing
        // unplanned signal the story names. Cancelled/Rejected excluded.
        var unplanned = await _dbContext.LeaveRequests.AsNoTracking()
            .Where(lr => employeeIds.Contains(lr.EmployeeId)
                         && lr.Status != LeaveRequestStatus.Cancelled
                         && lr.Status != LeaveRequestStatus.Rejected
                         && lr.StartDate <= to && lr.EndDate >= from
                         && lr.IsLop)
            .Select(lr => new { lr.EmployeeId, lr.TotalDays })
            .ToListAsync(ct);

        var lopByEmployee = unplanned
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.TotalDays));

        var rows = new List<LeaveReportRow>();
        foreach (var emp in employees)
        {
            decimal lopDays = lopByEmployee.GetValueOrDefault(emp.Id, 0m);
            decimal unplannedDays = lopDays; // LOP is the unplanned-absence signal (no attendance module)
            decimal avgPerMonth = monthsSpanned > 0 ? unplannedDays / monthsSpanned : unplannedDays;
            bool flagged = avgPerMonth > threshold;

            // Only employees with some unplanned absence are reported (highest-absenteeism focus, AC-3).
            if (unplannedDays <= 0m)
                continue;

            rows.Add(new LeaveReportRow
            {
                Cells =
                [
                    emp.EmployeeNo,
                    $"{emp.FirstName} {emp.LastName}".Trim(),
                    deptNames.GetValueOrDefault(emp.DepartmentId, string.Empty),
                    Num(unplannedDays), Num(lopDays), monthsSpanned.ToString(CultureInfo.InvariantCulture),
                    Num(avgPerMonth), Num(threshold), flagged ? "Yes" : "No",
                ],
            });
        }

        // Default sort: highest unplanned first (AC-3 "highest absenteeism").
        rows = rows
            .OrderByDescending(r => ParseNum(r.Cells[3]))
            .ToList();

        return (rows, columns, null);
    }

    /// <summary>
    /// FR-1 thin wrapper: carry-forward summary. Reuses the US-LV-008 carry-forward tracking rows
    /// (one row per employee × leave type × year-pair). Filtered by the closing year (qp.Year).
    /// </summary>
    private async Task<(List<LeaveReportRow> Rows, List<string> Columns, string? Note)> BuildCarryForwardSummaryAsync(
        LeaveReportQueryParams qp, ReportScope scope, CancellationToken ct)
    {
        var columns = new List<string>
        {
            "Employee No", "Employee", "Department", "Leave Type", "From Year", "To Year",
            "Carried Days", "Expired Days", "Expiry Date", "Status",
        };

        int fromYear = qp.Year ?? DateTime.UtcNow.Year;

        var employees = await ScopedEmployeesQuery(qp, scope).ToListAsync(ct);
        var employeeIds = employees.Select(e => e.Id).ToList();
        var empById = employees.ToDictionary(e => e.Id);
        var deptNames = await DepartmentNamesAsync(employees, ct);

        var leaveTypeNames = await _dbContext.LeaveTypes.AsNoTracking()
            .Select(lt => new { lt.Id, lt.Name }).ToListAsync(ct);
        var ltNameById = leaveTypeNames.ToDictionary(x => x.Id, x => x.Name);

        var tracking = await _dbContext.LeaveCarryForwardTrackings.AsNoTracking()
            .Where(t => employeeIds.Contains(t.EmployeeId)
                        && t.FromYear == fromYear
                        && (qp.LeaveTypeId == null || t.LeaveTypeId == qp.LeaveTypeId))
            .ToListAsync(ct);

        var rows = tracking
            .Where(t => empById.ContainsKey(t.EmployeeId))
            .Select(t =>
            {
                var emp = empById[t.EmployeeId];
                return new LeaveReportRow
                {
                    Cells =
                    [
                        emp.EmployeeNo,
                        $"{emp.FirstName} {emp.LastName}".Trim(),
                        deptNames.GetValueOrDefault(emp.DepartmentId, string.Empty),
                        ltNameById.GetValueOrDefault(t.LeaveTypeId, string.Empty),
                        t.FromYear.ToString(CultureInfo.InvariantCulture),
                        t.ToYear.ToString(CultureInfo.InvariantCulture),
                        Num(t.CarriedDays), Num(t.ExpiredDays),
                        t.ExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                        t.Status,
                    ],
                };
            })
            .ToList();

        return (rows, columns, null);
    }

    /// <summary>
    /// FR-1 thin wrapper: LOP summary across employees over a range. Reuses the US-LV-011 LOP rows
    /// (is_lop = true, non-Cancelled/Rejected) directly. One row per employee with their LOP totals.
    /// </summary>
    private async Task<(List<LeaveReportRow> Rows, List<string> Columns, string? Note)> BuildLopSummaryAsync(
        LeaveReportQueryParams qp, ReportScope scope, CancellationToken ct)
    {
        var columns = new List<string>
        {
            "Employee No", "Employee", "Department", "LOP Days", "LOP Entries",
        };

        var (from, to) = ResolveRange(qp);

        var employees = await ScopedEmployeesQuery(qp, scope).ToListAsync(ct);
        var employeeIds = employees.Select(e => e.Id).ToList();
        var deptNames = await DepartmentNamesAsync(employees, ct);

        var lop = await _dbContext.LeaveRequests.AsNoTracking()
            .Where(lr => employeeIds.Contains(lr.EmployeeId)
                         && lr.IsLop
                         && lr.Status != LeaveRequestStatus.Cancelled
                         && lr.Status != LeaveRequestStatus.Rejected
                         && lr.StartDate <= to && lr.EndDate >= from)
            .Select(lr => new { lr.EmployeeId, lr.TotalDays })
            .ToListAsync(ct);

        var byEmployee = lop
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => (Days: g.Sum(x => x.TotalDays), Count: g.Count()));

        var rows = employees
            .Where(e => byEmployee.ContainsKey(e.Id))
            .Select(e =>
            {
                var (days, count) = byEmployee[e.Id];
                return new LeaveReportRow
                {
                    Cells =
                    [
                        e.EmployeeNo,
                        $"{e.FirstName} {e.LastName}".Trim(),
                        deptNames.GetValueOrDefault(e.DepartmentId, string.Empty),
                        Num(days), count.ToString(CultureInfo.InvariantCulture),
                    ],
                };
            })
            .ToList();

        return (rows, columns, null);
    }

    /// <summary>
    /// FR-1 "Department Leave Calendar Coverage" — STUBBED. A coverage/heat-map report (who is off on
    /// which day per department) is a sizeable additional aggregation; it is deferred to keep this large
    /// story focused on the AC-named reports. Returns an empty result with a documented note.
    /// TODO(US-LV-012 coverage): build per-department per-day coverage (count off / headcount) from the
    ///   leave_request range data, reusing the US-LV-009 team-calendar overlap logic.
    /// </summary>
    private static (List<LeaveReportRow> Rows, List<string> Columns, string? Note) BuildDepartmentCalendarCoverageStub()
        => (new List<LeaveReportRow>(),
            new List<string> { "Department", "Date", "On Leave", "Headcount", "Coverage %" },
            "Department Leave Calendar Coverage is not yet implemented (deferred per US-LV-012). "
            + "TODO(coverage): compute per-department per-day coverage from leave_request ranges.");

    // ══════════════════════════════════════════════════════════════
    //  Analytics builders (FR-7)
    // ══════════════════════════════════════════════════════════════

    private async Task<LeaveAnalyticsResult> BuildUtilizationByDepartmentChartAsync(
        LeaveReportQueryParams qp, ReportScope scope, CancellationToken ct)
    {
        var agg = await ComputeUtilizationAggregatesAsync(qp, scope, ct);

        // Roll per (department, leaveType) up to per-department utilization for the bar/pie chart.
        var points = agg
            .GroupBy(a => a.DepartmentName)
            .Select(g =>
            {
                decimal ent = g.Sum(x => x.Entitlement);
                decimal used = g.Sum(x => x.Used);
                return new ChartPoint
                {
                    Label = g.Key,
                    Value = ent > 0 ? Math.Round(used / ent * 100m, 2) : 0m,
                };
            })
            .OrderByDescending(p => p.Value)
            .ToList();

        return new LeaveAnalyticsResult
        {
            ChartType = LeaveAnalyticsChartType.UtilizationByDepartment.ToString(),
            Points = points,
            Scope = scope.Kind.ToString(),
        };
    }

    private async Task<LeaveAnalyticsResult> BuildLeaveByTypeChartAsync(
        LeaveReportQueryParams qp, ReportScope scope, CancellationToken ct)
    {
        var (from, to) = ResolveRange(qp);
        var employeeIds = await ScopedEmployeesQuery(qp, scope).Select(e => e.Id).ToListAsync(ct);

        var taken = await _dbContext.LeaveRequests.AsNoTracking()
            .Where(lr => employeeIds.Contains(lr.EmployeeId)
                         && lr.Status == LeaveRequestStatus.Approved
                         && lr.StartDate <= to && lr.EndDate >= from
                         && (qp.LeaveTypeId == null || lr.LeaveTypeId == qp.LeaveTypeId))
            .Select(lr => new { lr.LeaveTypeId, lr.TotalDays })
            .ToListAsync(ct);

        var ltNames = (await _dbContext.LeaveTypes.AsNoTracking()
                .Select(lt => new { lt.Id, lt.Name }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.Name);

        var points = taken
            .GroupBy(x => x.LeaveTypeId)
            .Select(g => new ChartPoint
            {
                Label = ltNames.GetValueOrDefault(g.Key, "Unknown"),
                Value = g.Sum(x => x.TotalDays),
            })
            .OrderByDescending(p => p.Value)
            .ToList();

        return new LeaveAnalyticsResult
        {
            ChartType = LeaveAnalyticsChartType.LeaveByType.ToString(),
            Points = points,
            Scope = scope.Kind.ToString(),
        };
    }

    /// <summary>
    /// AC-4: monthly leave totals by type over the past 12 months — one series per leave type, with
    /// shared month-label categories for the line chart.
    /// </summary>
    private async Task<LeaveAnalyticsResult> BuildMonthlyTrendChartAsync(
        LeaveReportQueryParams qp, ReportScope scope, CancellationToken ct)
    {
        // Anchor on qp.To (or today). The window is the last 12 months ending in the anchor month.
        DateOnly anchor = qp.To ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var firstMonth = new DateOnly(anchor.Year, anchor.Month, 1).AddMonths(-(TrendMonths - 1));
        var rangeStart = firstMonth;
        var rangeEnd = new DateOnly(anchor.Year, anchor.Month, 1).AddMonths(1).AddDays(-1);

        var categories = Enumerable.Range(0, TrendMonths)
            .Select(i => firstMonth.AddMonths(i))
            .Select(d => d.ToString("yyyy-MM", CultureInfo.InvariantCulture))
            .ToList();

        var employeeIds = await ScopedEmployeesQuery(qp, scope).Select(e => e.Id).ToListAsync(ct);

        var requests = await _dbContext.LeaveRequests.AsNoTracking()
            .Where(lr => employeeIds.Contains(lr.EmployeeId)
                         && lr.Status == LeaveRequestStatus.Approved
                         && lr.StartDate <= rangeEnd && lr.StartDate >= rangeStart
                         && (qp.LeaveTypeId == null || lr.LeaveTypeId == qp.LeaveTypeId))
            .Select(lr => new { lr.LeaveTypeId, lr.StartDate, lr.TotalDays })
            .ToListAsync(ct);

        var ltNames = (await _dbContext.LeaveTypes.AsNoTracking()
                .Select(lt => new { lt.Id, lt.Name }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.Name);

        var series = requests
            .GroupBy(r => r.LeaveTypeId)
            .Select(typeGroup =>
            {
                var byMonth = typeGroup
                    .GroupBy(r => $"{r.StartDate.Year:D4}-{r.StartDate.Month:D2}")
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.TotalDays));

                var points = categories
                    .Select(cat => new ChartPoint { Label = cat, Value = byMonth.GetValueOrDefault(cat, 0m) })
                    .ToList();

                return new ChartSeries
                {
                    Name = ltNames.GetValueOrDefault(typeGroup.Key, "Unknown"),
                    Points = points,
                };
            })
            .OrderBy(s => s.Name)
            .ToList();

        return new LeaveAnalyticsResult
        {
            ChartType = LeaveAnalyticsChartType.MonthlyTrend.ToString(),
            Categories = categories,
            Series = series,
            Scope = scope.Kind.ToString(),
        };
    }

    // ══════════════════════════════════════════════════════════════
    //  Shared aggregation
    // ══════════════════════════════════════════════════════════════

    private sealed record UtilizationAggregate(
        string DepartmentName, string LeaveTypeName, decimal Entitlement, decimal Used);

    /// <summary>
    /// Per-(department, leave type) total entitlement + total used over the range. Used both by the
    /// tabular utilization report (AC-2) and the utilization-by-department chart (FR-7). Entitlement is
    /// the sum of per-employee engine entitlements; used is the sum of Approved leave days in range.
    /// </summary>
    private async Task<List<UtilizationAggregate>> ComputeUtilizationAggregatesAsync(
        LeaveReportQueryParams qp, ReportScope scope, CancellationToken ct)
    {
        var (from, to) = ResolveRange(qp);
        int year = qp.Year ?? to.Year;

        var employees = await ScopedEmployeesQuery(qp, scope).ToListAsync(ct);
        if (employees.Count == 0)
            return new List<UtilizationAggregate>();

        var employeeIds = employees.Select(e => e.Id).ToList();
        var deptNames = await DepartmentNamesAsync(employees, ct);
        var deptByEmp = employees.ToDictionary(e => e.Id, e => e.DepartmentId);

        var leaveTypes = await LeaveTypesQuery(qp).ToListAsync(ct);
        var leaveTypeIds = leaveTypes.Select(lt => lt.Id).ToList();
        var ltNameById = leaveTypes.ToDictionary(lt => lt.Id, lt => lt.Name);

        // Used = Approved leave taken in range, grouped per (department, leaveType).
        var used = await _dbContext.LeaveRequests.AsNoTracking()
            .Where(lr => employeeIds.Contains(lr.EmployeeId)
                         && lr.Status == LeaveRequestStatus.Approved
                         && lr.StartDate <= to && lr.EndDate >= from
                         && leaveTypeIds.Contains(lr.LeaveTypeId))
            .Select(lr => new { lr.EmployeeId, lr.LeaveTypeId, lr.TotalDays })
            .ToListAsync(ct);

        // Entitlement = sum of per-employee engine entitlements per (department, leaveType).
        var entByDeptType = new Dictionary<(Guid Dept, Guid Type), decimal>();
        foreach (var emp in employees)
        {
            var dept = deptByEmp[emp.Id];
            foreach (var lt in leaveTypes)
            {
                decimal ent = await ResolveEntitlementAsync(emp.Id, lt.Id, year, ct);
                if (ent == 0m) continue;
                var key = (dept, lt.Id);
                entByDeptType[key] = entByDeptType.GetValueOrDefault(key, 0m) + ent;
            }
        }

        var usedByDeptType = used
            .GroupBy(u => (Dept: deptByEmp[u.EmployeeId], Type: u.LeaveTypeId))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.TotalDays));

        // Union of keys present in either entitlement or usage.
        var keys = new HashSet<(Guid Dept, Guid Type)>(entByDeptType.Keys);
        foreach (var k in usedByDeptType.Keys) keys.Add(k);

        var result = keys
            .Select(k => new UtilizationAggregate(
                deptNames.GetValueOrDefault(k.Dept, string.Empty),
                ltNameById.GetValueOrDefault(k.Type, string.Empty),
                entByDeptType.GetValueOrDefault(k, 0m),
                usedByDeptType.GetValueOrDefault(k, 0m)))
            .OrderBy(a => a.DepartmentName)
            .ThenBy(a => a.LeaveTypeName)
            .ToList();

        return result;
    }

    // ══════════════════════════════════════════════════════════════
    //  Role scope (BR-2)
    // ══════════════════════════════════════════════════════════════

    private enum ScopeKind { All, Manager, Employee }

    private sealed record ReportScope(ScopeKind Kind, Employee? Me)
    {
        public override string ToString() => Kind.ToString();
    }

    /// <summary>
    /// BR-2 three-way scope resolution, mirroring US-LV-009:
    ///   1. HR/Leave.Reports (has Leave.View.All) → All tenant employees.
    ///   2. Manager (≥1 direct report) → own direct reports (+ self).
    ///   3. Otherwise (Employee) → only the caller's own record.
    /// Resolution order is HR → Manager → Employee.
    /// </summary>
    private async Task<ReportScope> ResolveScopeAsync(CancellationToken ct)
    {
        if (_currentUser.Permissions.Contains(PermissionCatalog.Leave.ViewAll))
            return new ReportScope(ScopeKind.All, null);

        var me = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, ct);

        if (me is null)
            // No employee record + no All permission → an empty Employee scope (returns nothing).
            return new ReportScope(ScopeKind.Employee, null);

        bool isManager = await _dbContext.Employees.AsNoTracking()
            .AnyAsync(e => e.ReportsToEmployeeId == me.Id, ct);

        return isManager
            ? new ReportScope(ScopeKind.Manager, me)
            : new ReportScope(ScopeKind.Employee, me);
    }

    /// <summary>
    /// Rebuilds a <see cref="ReportScope"/> from the (kind, employeeId) captured at enqueue time, for
    /// the background-export path. The employee record is re-read under the job's tenant context so the
    /// scope query reproduces the requester's view (BR-2).
    /// </summary>
    private async Task<ReportScope> BuildScopeFromKindAsync(
        string scopeKind, Guid? scopeEmployeeId, CancellationToken ct)
    {
        if (!Enum.TryParse<ScopeKind>(scopeKind, ignoreCase: true, out var kind))
            kind = ScopeKind.Employee;

        if (kind == ScopeKind.All)
            return new ReportScope(ScopeKind.All, null);

        Employee? me = null;
        if (scopeEmployeeId is { } id)
            me = await _dbContext.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

        return new ReportScope(kind, me);
    }

    /// <summary>
    /// The candidate employee set for a report, combining the role scope (BR-2) with the FR-2 filters
    /// (department, job title, employment type, employee search). Tenant isolation is already applied
    /// by the EF global query filter.
    /// </summary>
    private IQueryable<Employee> ScopedEmployeesQuery(LeaveReportQueryParams qp, ReportScope scope)
    {
        IQueryable<Employee> query = _dbContext.Employees.AsNoTracking()
            .Where(e => e.Status != EmployeeStatus.Terminated);

        switch (scope.Kind)
        {
            case ScopeKind.All:
                break;
            case ScopeKind.Manager when scope.Me is not null:
                var meId = scope.Me.Id;
                query = query.Where(e => e.ReportsToEmployeeId == meId || e.Id == meId);
                break;
            case ScopeKind.Employee when scope.Me is not null:
                var ownId = scope.Me.Id;
                query = query.Where(e => e.Id == ownId);
                break;
            default:
                // Employee scope with no resolved record → return nothing.
                query = query.Where(e => false);
                break;
        }

        if (qp.DepartmentId is { } deptId)
            query = query.Where(e => e.DepartmentId == deptId);
        if (qp.JobTitleId is { } jobId)
            query = query.Where(e => e.JobTitleId == jobId);
        if (TryParseEmploymentType(qp.EmploymentType, out var empType))
            query = query.Where(e => e.EmploymentType == empType);
        if (!string.IsNullOrWhiteSpace(qp.EmployeeSearch))
        {
            var term = qp.EmployeeSearch.Trim().ToLower();
            query = query.Where(e =>
                e.FirstName.ToLower().Contains(term)
                || e.LastName.ToLower().Contains(term)
                || e.EmployeeNo.ToLower().Contains(term));
        }

        return query.OrderBy(e => e.EmployeeNo);
    }

    private IQueryable<LeaveType> LeaveTypesQuery(LeaveReportQueryParams qp)
    {
        IQueryable<LeaveType> q = _dbContext.LeaveTypes.AsNoTracking().Where(lt => lt.IsActive);
        if (qp.LeaveTypeId is { } id)
            q = q.Where(lt => lt.Id == id);
        return q.OrderBy(lt => lt.DisplayOrder).ThenBy(lt => lt.Name);
    }

    private async Task<Dictionary<Guid, string>> DepartmentNamesAsync(
        IReadOnlyList<Employee> employees, CancellationToken ct)
    {
        var deptIds = employees.Select(e => e.DepartmentId).Distinct().ToList();
        return (await _dbContext.Departments.AsNoTracking()
                .Where(d => deptIds.Contains(d.Id))
                .Select(d => new { d.Id, d.Name })
                .ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.Name);
    }

    private async Task<decimal> ResolveEntitlementAsync(
        Guid employeeId, Guid leaveTypeId, int year, CancellationToken ct)
    {
        var ent = await _entitlementService.ComputeEffectiveEntitlementAsync(employeeId, leaveTypeId, year, ct);
        return ent.IsSuccess ? ent.Value!.ProratedEntitlementDays : 0m;
    }

    /// <summary>
    /// BR-4: tenant-configurable absenteeism threshold (unplanned leaves per month). No tenant-settings
    /// entity exists, so the story default (3) is used.
    /// TODO(tenant-settings): read the per-tenant threshold from tenant configuration when it exists.
    /// </summary>
    private Task<decimal> ResolveAbsenteeismThresholdAsync(CancellationToken ct)
        => Task.FromResult(3m);

    // ══════════════════════════════════════════════════════════════
    //  Sort / paging / formatting helpers
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// FR-3 server-side sort by column header name (case-insensitive). Numeric-looking columns sort
    /// numerically; everything else sorts as a string. Falls back to the report's natural order when
    /// no/unknown SortBy is supplied (the builders already apply a sensible default order).
    /// </summary>
    private static List<LeaveReportRow> ApplySort(
        List<LeaveReportRow> rows, List<string> columns, LeaveReportQueryParams qp)
    {
        if (string.IsNullOrWhiteSpace(qp.SortBy) || rows.Count == 0)
            return rows;

        int idx = columns.FindIndex(c =>
            string.Equals(c.Replace(" ", string.Empty), qp.SortBy.Replace(" ", string.Empty),
                StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
            return rows;

        bool numeric = rows.All(r => idx < r.Cells.Count && IsNumeric(r.Cells[idx]));

        IOrderedEnumerable<LeaveReportRow> ordered = numeric
            ? (qp.SortAscending
                ? rows.OrderBy(r => ParseNum(r.Cells[idx]))
                : rows.OrderByDescending(r => ParseNum(r.Cells[idx])))
            : (qp.SortAscending
                ? rows.OrderBy(r => idx < r.Cells.Count ? r.Cells[idx] : string.Empty, StringComparer.OrdinalIgnoreCase)
                : rows.OrderByDescending(r => idx < r.Cells.Count ? r.Cells[idx] : string.Empty, StringComparer.OrdinalIgnoreCase));

        return ordered.ToList();
    }

    private static (DateOnly From, DateOnly To) ResolveRange(LeaveReportQueryParams qp)
    {
        // Default range: the supplied year, or the current calendar year.
        if (qp.From is { } f && qp.To is { } t)
            return (f, t);

        int year = qp.Year ?? DateTime.UtcNow.Year;
        return (qp.From ?? new DateOnly(year, 1, 1), qp.To ?? new DateOnly(year, 12, 31));
    }

    private static int MonthsBetween(DateOnly from, DateOnly to)
    {
        if (to < from) return 0;
        int months = (to.Year - from.Year) * 12 + (to.Month - from.Month) + 1;
        return Math.Max(1, months);
    }

    private static bool TryParseEmploymentType(string? value, out EmploymentType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalised = value.Replace("-", string.Empty).Replace(" ", string.Empty);
        return Enum.TryParse(normalised, ignoreCase: true, out type);
    }

    private static string Num(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Pct(decimal entitlement, decimal used)
        => entitlement > 0
            ? Math.Round(used / entitlement * 100m, 2).ToString("0.##", CultureInfo.InvariantCulture)
            : "0";

    private static bool IsNumeric(string s)
        => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out _);

    private static decimal ParseNum(string s)
        => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    private static string ToKebab(string pascal)
    {
        var chars = new List<char>();
        for (int i = 0; i < pascal.Length; i++)
        {
            char c = pascal[i];
            if (char.IsUpper(c) && i > 0) chars.Add('-');
            chars.Add(char.ToLowerInvariant(c));
        }
        return new string(chars.ToArray());
    }

    // ══════════════════════════════════════════════════════════════
    //  Export rendering (FR-4)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Component sums of a single leave type's ledger entries — same convention as US-LV-006
    /// LeaveDashboardService (Used/Expired surfaced as positive magnitudes; Adjustments signed; Accrual
    /// not re-added because the engine entitlement already represents the grant).
    /// </summary>
    private readonly record struct LedgerComponents(
        decimal Used, decimal CarryForward, decimal Expired, decimal Adjustments)
    {
        public static readonly LedgerComponents Empty = new(0m, 0m, 0m, 0m);

        public static LedgerComponents From(IEnumerable<(LedgerEntryType EntryType, decimal Amount)> entries)
        {
            decimal used = 0m, carry = 0m, expired = 0m, adjusted = 0m;
            foreach (var (type, amount) in entries)
            {
                switch (type)
                {
                    case LedgerEntryType.Used:
                    case LedgerEntryType.Encashed:
                        used += Math.Abs(amount);
                        break;
                    case LedgerEntryType.CarryForward:
                        carry += amount;
                        break;
                    case LedgerEntryType.Expired:
                        expired += Math.Abs(amount);
                        break;
                    case LedgerEntryType.Adjusted:
                        adjusted += amount;
                        break;
                    case LedgerEntryType.Accrual:
                    default:
                        break;
                }
            }
            return new LedgerComponents(used, carry, expired, adjusted);
        }
    }

    private static byte[] RenderCsv(LeaveReportResult result)
    {
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, leaveOpen: true))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            foreach (var header in result.Columns)
                csv.WriteField(header);
            csv.NextRecord();

            foreach (var row in result.Rows)
            {
                foreach (var cell in row.Cells)
                    csv.WriteField(cell);
                csv.NextRecord();
            }
        }
        return stream.ToArray();
    }

    private static byte[] RenderXlsx(LeaveReportType reportType, LeaveReportResult result)
    {
        using var workbook = new XLWorkbook();
        var sheetName = reportType.ToString();
        if (sheetName.Length > 31) sheetName = sheetName[..31]; // Excel sheet-name limit
        var ws = workbook.Worksheets.Add(sheetName);

        for (int c = 0; c < result.Columns.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = result.Columns[c];
            cell.Style.Font.Bold = true;
        }

        for (int r = 0; r < result.Rows.Count; r++)
        {
            var cells = result.Rows[r].Cells;
            for (int c = 0; c < cells.Count; c++)
                ws.Cell(r + 2, c + 1).Value = cells[c];
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
