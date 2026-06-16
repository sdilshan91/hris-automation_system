namespace HRM.Domain.Enums;

/// <summary>
/// Progress assessment recorded at a PIP checkpoint (US-PRF-008 AC-3/FR-4). Rendered by the FE as a
/// traffic-light selector (green/amber/red, §8). Stored as a string column.
/// </summary>
public enum PipCheckpointStatus
{
    /// <summary>The employee is on track to meet the PIP objectives (green).</summary>
    OnTrack = 0,

    /// <summary>The employee is at risk of not meeting the objectives (amber).</summary>
    AtRisk = 1,

    /// <summary>The objectives are not being met (red).</summary>
    NotMet = 2,
}
