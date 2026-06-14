namespace HRM.Application.Features.LeaveCarryForward.DTOs;

/// <summary>
/// One row of the year-end carry-forward preview (US-LV-008 FR-5, AC-5): the projected
/// carry-forward and forfeiture for a single employee × leave type for the closing leave year.
/// Computed by the SAME pure logic the year-end job uses, so the preview matches the job exactly.
/// </summary>
public sealed record CarryForwardPreviewDto
{
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string? EmployeeNo { get; init; }
    public Guid LeaveTypeId { get; init; }
    public string LeaveTypeName { get; init; } = string.Empty;

    /// <summary>The closing leave year this projection is for.</summary>
    public int FromYear { get; init; }

    /// <summary>The new leave year days would be carried into (= FromYear + 1).</summary>
    public int ToYear { get; init; }

    /// <summary>Unused balance for the closing year before processing.</summary>
    public decimal UnusedBalance { get; init; }

    /// <summary>Projected carry-forward = MIN(unused, limit) (BR-1).</summary>
    public decimal CarryForward { get; init; }

    /// <summary>Projected forfeiture = unused − carry-forward (BR-2).</summary>
    public decimal Forfeited { get; init; }

    /// <summary>
    /// True when the forfeitable balance would be encashed instead of expired (BR-5): the leave
    /// type is Encashable. Informational for the preview.
    /// </summary>
    public bool WouldEncash { get; init; }
}
