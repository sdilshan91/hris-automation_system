using HRM.Domain.Enums;

namespace HRM.Application.Features.Payroll.DTOs;

// ── Read DTOs ─────────────────────────────────────────────────────────

/// <summary>A single tax slab on an income-tax rule (US-PAY-006 FR-1).</summary>
public sealed record TaxSlabDto
{
    public Guid Id { get; init; }
    public decimal SlabFrom { get; init; }
    public decimal? SlabTo { get; init; }
    public decimal RatePercentage { get; init; }
    public int OrderIndex { get; init; }
}

/// <summary>The social-security parameters on an EPF/ETF/etc. rule (US-PAY-006 FR-2).</summary>
public sealed record SocialSecurityRuleDto
{
    public Guid Id { get; init; }
    public decimal EmployeeRate { get; init; }
    public decimal EmployerRate { get; init; }
    public decimal? WageCeilingAnnual { get; init; }
    public StatutoryApplicableOn ApplicableOn { get; init; }
    public string ApplicableOnName { get; init; } = string.Empty;
    public IReadOnlyList<Guid> ApplicableComponentIds { get; init; } = [];
}

/// <summary>Full statutory-rule representation for detail + create/update responses (US-PAY-006 §7).</summary>
public sealed record StatutoryRuleDto
{
    public Guid Id { get; init; }
    public StatutoryRuleType RuleType { get; init; }
    public string RuleTypeName { get; init; } = string.Empty;
    public string RuleName { get; init; } = string.Empty;
    public string CountryCode { get; init; } = string.Empty;
    public string FiscalYear { get; init; } = string.Empty;
    public DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<TaxSlabDto> TaxSlabs { get; init; } = [];
    public SocialSecurityRuleDto? SocialSecurity { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>Lightweight rule row for the paged list view.</summary>
public sealed record StatutoryRuleListItemDto
{
    public Guid Id { get; init; }
    public StatutoryRuleType RuleType { get; init; }
    public string RuleTypeName { get; init; } = string.Empty;
    public string RuleName { get; init; } = string.Empty;
    public string CountryCode { get; init; } = string.Empty;
    public string FiscalYear { get; init; } = string.Empty;
    public DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public bool IsActive { get; init; }
    public int SlabCount { get; init; }
}

// ── Request bodies ────────────────────────────────────────────────────

/// <summary>Request body for a tax slab within a create/update statutory-rule request (FR-1).</summary>
public sealed record TaxSlabRequest
{
    public decimal SlabFrom { get; init; }
    public decimal? SlabTo { get; init; }
    public decimal RatePercentage { get; init; }
    public int OrderIndex { get; init; }
}

/// <summary>Request body for the social-security parameters of a create/update statutory-rule request (FR-2).</summary>
public sealed record SocialSecurityRuleRequest
{
    public decimal EmployeeRate { get; init; }
    public decimal EmployerRate { get; init; }
    public decimal? WageCeilingAnnual { get; init; }
    public StatutoryApplicableOn ApplicableOn { get; init; }
    public IReadOnlyList<Guid>? ApplicableComponentIds { get; init; }
}

/// <summary>
/// Request body for creating a statutory rule (FR-1/FR-2/FR-3). For <c>IncomeTax</c> supply
/// <see cref="TaxSlabs"/>; for EPF/ETF/ProfessionalTax/Custom supply <see cref="SocialSecurity"/>.
/// </summary>
public sealed record CreateStatutoryRuleRequest
{
    public StatutoryRuleType RuleType { get; init; }
    public string RuleName { get; init; } = string.Empty;
    public string CountryCode { get; init; } = string.Empty;
    public string FiscalYear { get; init; } = string.Empty;
    public DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public bool IsActive { get; init; } = true;
    public IReadOnlyList<TaxSlabRequest> TaxSlabs { get; init; } = [];
    public SocialSecurityRuleRequest? SocialSecurity { get; init; }
}

/// <summary>Request body for updating a statutory rule (FR-1/FR-2). Replaces the rule's slabs / social-security wholesale.</summary>
public sealed record UpdateStatutoryRuleRequest
{
    public string RuleName { get; init; } = string.Empty;
    public string CountryCode { get; init; } = string.Empty;
    public string FiscalYear { get; init; } = string.Empty;
    public DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public bool IsActive { get; init; } = true;
    public IReadOnlyList<TaxSlabRequest> TaxSlabs { get; init; } = [];
    public SocialSecurityRuleRequest? SocialSecurity { get; init; }
}

/// <summary>Request body for cloning all of one fiscal year's rules into a new fiscal year (AC-3).</summary>
public sealed record CloneFiscalYearRequest
{
    public string SourceFiscalYear { get; init; } = string.Empty;
    public string TargetFiscalYear { get; init; } = string.Empty;
    public DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
}

/// <summary>
/// Request body for the test-calculation feature (FR-5): given a sample monthly gross (and optional basic +
/// declared exemptions), return the computed statutory deductions WITHOUT persisting anything.
/// </summary>
public sealed record TestCalculationRequest
{
    public decimal MonthlyGross { get; init; }
    public decimal? MonthlyBasic { get; init; }
    public decimal DeclaredMonthlyExemptions { get; init; }
    public decimal ExemptEarnings { get; init; }
    public string? FiscalYear { get; init; }
}

/// <summary>The computed statutory deductions for a test calculation (FR-5). All amounts are per-period (monthly).</summary>
public sealed record StatutoryCalculationResultDto
{
    public decimal TaxableIncome { get; init; }
    public decimal IncomeTax { get; init; }
    public decimal EmployeeEpf { get; init; }
    public decimal EmployerEpf { get; init; }
    public decimal Etf { get; init; }
    public decimal ProfessionalTax { get; init; }
    public decimal OtherStatutory { get; init; }
    /// <summary>Total deductions that reduce the employee's net pay (income tax + employee EPF + professional + other).</summary>
    public decimal TotalEmployeeDeductions { get; init; }
    /// <summary>Total employer-side statutory cost (employer EPF + ETF) — informational, not deducted from the employee.</summary>
    public decimal TotalEmployerContributions { get; init; }
    public IReadOnlyList<StatutoryLineDto> Lines { get; init; } = [];
    public string FiscalYear { get; init; } = string.Empty;
}

/// <summary>One labelled statutory deduction/contribution line in a test calculation.</summary>
public sealed record StatutoryLineDto
{
    public string Label { get; init; } = string.Empty;
    public StatutoryRuleType RuleType { get; init; }
    public decimal Amount { get; init; }
    /// <summary>True when this line is an employer-side contribution (not deducted from the employee).</summary>
    public bool IsEmployerContribution { get; init; }
    public string Basis { get; init; } = string.Empty;
}
