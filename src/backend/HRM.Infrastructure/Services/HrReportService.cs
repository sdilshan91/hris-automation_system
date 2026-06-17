using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Reports.DTOs;
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

    public HrReportService(
        AppDbContext db,
        ITenantContext tenantContext,
        ILogger<HrReportService> logger,
        IDistributedCache? cache = null)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
        _cache = cache;
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

        var cacheKey = BuildCacheKey(reportType, queryParams, dateFrom, dateTo);

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
        };

    /// <summary>FR-5 cache key: <c>t:{tenantId}:report:{name}:{paramsHash}</c>.</summary>
    private string BuildCacheKey(HrReportType type, HrReportQueryParams qp, DateTime dateFrom, DateTime dateTo)
    {
        var raw = string.Join('|',
            dateFrom.ToString("yyyyMMdd"), dateTo.ToString("yyyyMMdd"),
            string.Join(',', qp.DepartmentIds.OrderBy(x => x)),
            string.Join(',', qp.LocationIds.OrderBy(x => x)),
            string.Join(',', qp.EmploymentTypes.Select(x => x.ToLowerInvariant()).OrderBy(x => x)),
            string.Join(',', qp.EmployeeStatuses.Select(x => x.ToLowerInvariant()).OrderBy(x => x)));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16].ToLowerInvariant();
        return $"t:{_tenantContext.TenantId}:report:{HrReportTypeKey.ToKey(type)}:{hash}";
    }

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
