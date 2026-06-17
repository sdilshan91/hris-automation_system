namespace HRM.Application.Features.Dashboard.DTOs;

// ════════════════════════════════════════════════════════════════════════════
//  US-RPT-005 — Dashboard with KPI Widgets.
//  Pinned API contract (the frontend + QA build against this EXACT shape; the
//  payload is wrapped in the standard ApiResponse<T> envelope, which the FE
//  unwraps globally).
//
//  Base: api/v1/dashboard, [Authorize] (any authenticated user). The role is
//  derived SERVER-SIDE from ICurrentUser — never from a client param (FR-8).
//
//  ONE flexible widget shape (DashboardWidget) covers KPI / donut / progress /
//  list / link widgets: populate only the relevant fields, leave the rest null.
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// The role-based dashboard payload (US-RPT-005 AC-1/AC-2/AC-3, FR-1/FR-3). The widget set + order is decided
/// server-side from the resolved role. <see cref="GreetingName"/> drives the "Good morning, [First Name]"
/// header (§8); <see cref="GeneratedAt"/> is the UTC build instant for the auto-refresh footer (FR-9).
/// </summary>
public sealed record DashboardResponse
{
    /// <summary>The resolved role: "hr" | "manager" | "employee" (BR-1, derived from ICurrentUser).</summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>The logged-in user's first name for the greeting (§8). Falls back to "" when unknown.</summary>
    public string GreetingName { get; init; } = string.Empty;

    /// <summary>UTC ISO instant the payload was generated (FR-9 refresh footer).</summary>
    public DateTime GeneratedAt { get; init; }

    /// <summary>The ordered widget set for display (FR-2). Omitted widgets are simply absent.</summary>
    public IReadOnlyList<DashboardWidget> Widgets { get; init; } = [];
}

/// <summary>
/// A single dashboard widget card (US-RPT-005 §7, FR-2). One flexible shape covers every widget kind —
/// KPI, donut, progress, list, and link — so the FE renders generically off <see cref="MiniChart"/> /
/// <see cref="Items"/> / <see cref="LinkUrl"/>. Populate only the fields the widget needs; leave the rest null.
/// </summary>
public sealed record DashboardWidget
{
    /// <summary>Stable kebab key (e.g. "headcount", "pending-leave"). The FE owns i18n off this key.</summary>
    public string WidgetKey { get; init; } = string.Empty;

    /// <summary>Display label (a sensible English default; the FE may localize off WidgetKey).</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Primary metric value — a number, a string, or null (null for pure list/link widgets).</summary>
    public object? Value { get; init; }

    /// <summary>Optional unit suffix, e.g. "%". Null when not applicable.</summary>
    public string? Unit { get; init; }

    /// <summary>Previous-period value for the trend calc (BR-2). Null when no comparison is available.</summary>
    public object? PreviousValue { get; init; }

    /// <summary>"up" | "down" | "flat" | null — the raw direction of change (BR-2).</summary>
    public string? TrendDirection { get; init; }

    /// <summary>Percentage change vs the previous period (BR-2). Null when no comparison is available.</summary>
    public decimal? TrendPercentage { get; init; }

    /// <summary>
    /// Semantic trend sentiment (BR-2 / §8): true → green, false → red. The FE colours by THIS, not by
    /// direction — e.g. headcount growth (up) is positive/green, turnover increase (up) is negative/red.
    /// Null when there is no trend.
    /// </summary>
    public bool? TrendIsPositive { get; init; }

    /// <summary>Optional mini visualization (sparkline / donut / progress). Null for KPI/list/link widgets.</summary>
    public DashboardMiniChart? MiniChart { get; init; }

    /// <summary>List rows for list / quick-actions widgets. Null for KPI widgets.</summary>
    public IReadOnlyList<DashboardListItem>? Items { get; init; }

    /// <summary>Click-through URL (AC-4 / FR-5). A real FE route where known, else a sensible module route.</summary>
    public string? LinkUrl { get; init; }

    /// <summary>Pre-applied filters for the click-through (AC-4 / FR-5), e.g. { "status": "Pending" }.</summary>
    public IReadOnlyDictionary<string, object?>? LinkFilters { get; init; }
}

/// <summary>A mini chart embedded in a widget card (§8). type ∈ "sparkline" | "donut" | "progress".</summary>
public sealed record DashboardMiniChart
{
    public string Type { get; init; } = string.Empty;

    /// <summary>The plotted points (donut segments / sparkline points / a single progress value).</summary>
    public IReadOnlyList<DashboardChartPoint> Series { get; init; } = [];

    /// <summary>Optional axis/total max — used by "progress" (e.g. 100) and donut totals. Null otherwise.</summary>
    public decimal? Max { get; init; }
}

/// <summary>One point in a widget mini chart.</summary>
public sealed record DashboardChartPoint
{
    public string Label { get; init; } = string.Empty;
    public decimal Value { get; init; }
}

/// <summary>One row in a list / quick-actions widget (AC-2/AC-3, FR-7). value/linkUrl/linkFilters optional.</summary>
public sealed record DashboardListItem
{
    public string Label { get; init; } = string.Empty;
    public object? Value { get; init; }
    public string? LinkUrl { get; init; }
    public IReadOnlyDictionary<string, object?>? LinkFilters { get; init; }
}
