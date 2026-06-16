namespace HRM.Domain.Enums;

/// <summary>
/// The completion status of a single 360-degree reviewer assignment (US-PRF-005 AC-3). A reviewer starts
/// Pending and becomes Completed once they submit their feedback; the completion tracker (AC-3/AC-4) counts
/// Completed vs Pending per category. Stored as a string column.
/// </summary>
public enum ReviewerAssignmentStatus
{
    /// <summary>The reviewer has been assigned but has not yet submitted their feedback.</summary>
    Pending = 0,

    /// <summary>The reviewer has submitted their feedback.</summary>
    Completed = 1,
}
