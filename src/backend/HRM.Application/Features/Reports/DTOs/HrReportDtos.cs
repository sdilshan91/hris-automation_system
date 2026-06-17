namespace HRM.Application.Features.Reports.DTOs;

/// <summary>
/// The pre-built HR report types (US-RPT-001 FR-1). The wire form is a KEBAB key (see
/// <see cref="HrReportTypeKey"/>) — this enum is the internal switch key. All reports read over the existing
/// employee / department / location / employment_history data and are tenant-scoped via the EF global query
/// filter (AC-5 / FR-7).
/// </summary>
public enum HrReportType
{
    /// <summary>"headcount" — AC-2: total headcount, active vs inactive, breakdown by employment type, by department.</summary>
    HeadcountSummary,
    /// <summary>"turnover" — AC-3: separations, voluntary/involuntary, monthly trend, by department, average tenure (BR-3).</summary>
    EmployeeTurnover,
    /// <summary>"demographics" — AC-4: gender distribution, age histogram (5-yr buckets, BR-5), department + location distribution.</summary>
    Demographics,
    /// <summary>"joiners-leavers" — FR-1: employees who joined and separated within the period.</summary>
    JoinersAndLeavers,
    /// <summary>"department-distribution" — FR-1: active headcount by department.</summary>
    DepartmentDistribution,
    /// <summary>"employment-type-breakdown" — FR-1: active headcount by employment type (FullTime / PartTime / Contract / Intern).</summary>
    EmploymentTypeBreakdown,
}

/// <summary>
/// Kebab ⇄ <see cref="HrReportType"/> mapping. The FE addresses reports by their kebab key (route value and
/// catalog identifier); this is the single source of truth for that translation and the per-type icon hint.
/// </summary>
public static class HrReportTypeKey
{
    private static readonly IReadOnlyList<(HrReportType Type, string Key, string Title, string Icon)> Map =
    [
        (HrReportType.HeadcountSummary,        "headcount",                 "Headcount Summary",        "groups"),
        (HrReportType.EmployeeTurnover,        "turnover",                  "Employee Turnover",        "trending_down"),
        (HrReportType.Demographics,            "demographics",              "Demographics",             "diversity_3"),
        (HrReportType.JoinersAndLeavers,       "joiners-leavers",           "Joiners & Leavers",        "swap_horiz"),
        (HrReportType.DepartmentDistribution,  "department-distribution",   "Department Distribution",  "apartment"),
        (HrReportType.EmploymentTypeBreakdown, "employment-type-breakdown", "Employment Type Breakdown", "badge"),
    ];

    /// <summary>The kebab key for a report type (the value emitted in catalog + metadata).</summary>
    public static string ToKey(HrReportType type) => Map.First(m => m.Type == type).Key;

    /// <summary>The icon token for a report type (catalog only).</summary>
    public static string Icon(HrReportType type) => Map.First(m => m.Type == type).Icon;

    /// <summary>The literal English title for a report type (rendered as-is by the FE).</summary>
    public static string Title(HrReportType type) => Map.First(m => m.Type == type).Title;

    /// <summary>Parses a kebab key (case-insensitive) → enum. Returns false for an unknown key.</summary>
    public static bool TryParse(string? key, out HrReportType type)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            var trimmed = key.Trim();
            foreach (var m in Map)
            {
                if (string.Equals(m.Key, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    type = m.Type;
                    return true;
                }
            }
        }
        type = default;
        return false;
    }

    /// <summary>The catalog descriptors (kebab key + icon) for every report type.</summary>
    public static IReadOnlyList<HrReportDescriptorDto> Catalog() =>
        Map.Select(m => new HrReportDescriptorDto { Type = m.Key, Icon = m.Icon }).ToList();
}

/// <summary>
/// Common filter parameters shared by all HR reports (US-RPT-001 FR-2 / §7). Filters that don't apply to a
/// given report are ignored by that report's query. Empty collections mean "no filter". Property names
/// serialize (System.Text.Json camelCase) to exactly: dateFrom, dateTo, departmentIds, locationIds,
/// employmentTypes, employeeStatuses.
/// </summary>
public sealed record HrReportQueryParams
{
    /// <summary>Report-window start. Defaults to the start of the current month when omitted (§7).</summary>
    public DateTime? DateFrom { get; init; }

    /// <summary>Report-window end. Defaults to today (UTC) when omitted (§7).</summary>
    public DateTime? DateTo { get; init; }

    /// <summary>Restrict to these departments (multi-select). Empty = all.</summary>
    public IReadOnlyList<Guid> DepartmentIds { get; init; } = [];

    /// <summary>Restrict to these locations (multi-select). Empty = all.</summary>
    public IReadOnlyList<Guid> LocationIds { get; init; } = [];

    /// <summary>Restrict to these employment types (FullTime / PartTime / Contract / Intern). Empty = all.</summary>
    public IReadOnlyList<string> EmploymentTypes { get; init; } = [];

    /// <summary>Restrict to these statuses (Active / Probation / Inactive / Terminated / Suspended). Empty = all.</summary>
    public IReadOnlyList<string> EmployeeStatuses { get; init; } = [];
}

/// <summary>A single labelled data point in a chart series (chart-library-agnostic).</summary>
public sealed record HrChartPoint
{
    public string Label { get; init; } = string.Empty;
    public decimal Value { get; init; }
}

/// <summary>One named series within a chart block (multi-series capable). Single-series charts = one entry.</summary>
public sealed record HrChartSeries
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<HrChartPoint> Points { get; init; } = [];
}

/// <summary>
/// A typed chart block (US-RPT-001 §7, FR-3). Carries a <see cref="Kind"/> the FE viewer renders directly
/// (bar | horizontal-bar | line | pie | histogram | stacked-bar), a title and one-or-more series.
/// </summary>
public sealed record HrChartBlock
{
    /// <summary>One of: bar | horizontal-bar | line | pie | histogram | stacked-bar.</summary>
    public string Kind { get; init; } = "bar";
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<HrChartSeries> Series { get; init; } = [];
    public string? XAxisLabel { get; init; }
    public string? YAxisLabel { get; init; }
}

/// <summary>A headline KPI card (AC-2). <see cref="Value"/> is a number or string; <see cref="Tone"/> is neutral|positive|negative.</summary>
public sealed record HrSummaryStat
{
    public string Label { get; init; } = string.Empty;
    public object? Value { get; init; }
    public string Tone { get; init; } = "neutral";
}

/// <summary>
/// Echo of the resolved filters (US-RPT-001 §7) — SAME field names as the request filter body. Dates are
/// emitted as ISO yyyy-MM-dd strings; collections are echoed verbatim (resolved values).
/// </summary>
public sealed record HrAppliedFilters
{
    public string? DateFrom { get; init; }
    public string? DateTo { get; init; }
    public IReadOnlyList<Guid> DepartmentIds { get; init; } = [];
    public IReadOnlyList<Guid> LocationIds { get; init; } = [];
    public IReadOnlyList<string> EmploymentTypes { get; init; } = [];
    public IReadOnlyList<string> EmployeeStatuses { get; init; } = [];
}

/// <summary>Report metadata block (US-RPT-001 §7): type/title/timestamp + applied filters + KPI summary + cache flag.</summary>
public sealed record HrReportMetadata
{
    /// <summary>The kebab report key (e.g. "headcount").</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>The literal English title, rendered as-is by the FE (e.g. "Headcount Summary").</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>UTC timestamp the report was generated (or, when served from cache, first generated).</summary>
    public DateTime GeneratedAt { get; init; }

    /// <summary>Echo of the resolved filters (same field names as the request body).</summary>
    public HrAppliedFilters AppliedFilters { get; init; } = new();

    /// <summary>Headline KPI cards (AC-2 etc.) computed by the report.</summary>
    public IReadOnlyList<HrSummaryStat> Summary { get; init; } = [];

    /// <summary>True when served from the Redis cache rather than freshly computed (FR-5).</summary>
    public bool FromCache { get; init; }
}

/// <summary>
/// Tabular data block. <see cref="Rows"/> is a plain 2D array whose cells keep their natural JSON type
/// (numbers as numbers, strings as strings, null permitted) — NOT an all-strings wrapper.
/// </summary>
public sealed record HrReportTable
{
    public IReadOnlyList<string> Columns { get; init; } = [];
    public IReadOnlyList<object?[]> Rows { get; init; } = [];
}

/// <summary>
/// Generic HR-report envelope (US-RPT-001 §7) used by ALL six report types — the exact nested shape the FE
/// viewer consumes: metadata / charts / table.
/// </summary>
public sealed record HrReportResult
{
    public HrReportMetadata Metadata { get; init; } = new();
    public IReadOnlyList<HrChartBlock> Charts { get; init; } = [];
    public HrReportTable Table { get; init; } = new();
}

/// <summary>
/// One report-type descriptor for the catalog endpoint (US-RPT-001 AC-1): the FE owns i18n and derives the
/// title/description from <see cref="Type"/>, so the catalog carries only the kebab key + an icon token.
/// </summary>
public sealed record HrReportDescriptorDto
{
    /// <summary>The stable kebab report key (e.g. "headcount").</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>A Material icon token for the FE card grid.</summary>
    public string Icon { get; init; } = string.Empty;
}
