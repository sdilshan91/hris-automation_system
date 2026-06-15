namespace HRM.Domain.Enums;

/// <summary>
/// Classifies a statutory deduction rule (US-PAY-006 FR-3). Serialized as a string in the API
/// (global JsonStringEnumConverter) and stored as varchar via HasConversion&lt;string&gt;() so DB +
/// wire values match the data-spec literals exactly (IncomeTax, EPF, ETF, ProfessionalTax, Custom).
/// </summary>
public enum StatutoryRuleType
{
    /// <summary>Progressive income tax computed over tax slabs (BR-3). Owns a set of <c>tax_slab</c> rows.</summary>
    IncomeTax,

    /// <summary>Employee Provident Fund — social-security contribution with a wage ceiling (AC-2/BR-8).</summary>
    EPF,

    /// <summary>Employees' Trust Fund — employer-side social-security contribution.</summary>
    ETF,

    /// <summary>Professional tax — a flat or slab-based occupational levy.</summary>
    ProfessionalTax,

    /// <summary>A tenant-defined custom statutory item (FR-3).</summary>
    Custom,
}
