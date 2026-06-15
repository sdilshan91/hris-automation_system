namespace HRM.Domain.Enums;

/// <summary>
/// The wage base a social-security contribution is applied on (US-PAY-006 FR-2). Serialized as a string
/// in the API and stored as varchar via HasConversion&lt;string&gt;().
/// </summary>
public enum StatutoryApplicableOn
{
    /// <summary>Applied on the employee's BASIC component (the most common social-security base).</summary>
    Basic,

    /// <summary>Applied on total gross earnings.</summary>
    Gross,

    /// <summary>Applied on the sum of a tenant-selected set of components (<c>applicable_component_ids</c>).</summary>
    Custom,
}
