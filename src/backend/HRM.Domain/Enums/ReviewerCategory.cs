namespace HRM.Domain.Enums;

/// <summary>
/// The 360-degree reviewer category (US-PRF-005 FR-1). Each assigned reviewer falls into exactly one of
/// four perspectives. Self and Manager are auto-assigned; Peer and DirectReport are auto-suggested and
/// manually adjustable (AC-1/FR-2). Stored as a string column.
/// </summary>
public enum ReviewerCategory
{
    /// <summary>The employee reviewing themselves — the self-assessment perspective (US-PRF-002).</summary>
    Self = 0,

    /// <summary>The employee's reporting manager (auto-assigned from the org tree).</summary>
    Manager = 1,

    /// <summary>A peer in the same department (auto-suggested; BR-2 the employee can never be their own peer).</summary>
    Peer = 2,

    /// <summary>A direct report of the employee (auto-suggested from the org tree).</summary>
    DirectReport = 3,
}
