namespace HRM.Domain.Enums;

/// <summary>
/// Built-in offboarding clearance categories (US-ONB-005 FR-3). Each maps to a responsible department/role
/// that signs off before the employee's last working day. Stored as a string column.
/// </summary>
public enum ClearanceCategory
{
    /// <summary>IT: access revocation, asset return (FR-3).</summary>
    IT = 0,

    /// <summary>Finance: advances, loans, Full &amp; Final settlement readiness (FR-3).</summary>
    Finance = 1,

    /// <summary>Admin: ID card, parking, keys (FR-3).</summary>
    Admin = 2,

    /// <summary>Manager: knowledge transfer, handover (FR-3).</summary>
    Manager = 3,

    /// <summary>The departing employee: personal handover items (FR-3).</summary>
    Employee = 4,

    /// <summary>HR: overall coordination / exit interview (FR-3).</summary>
    HR = 5,
}
