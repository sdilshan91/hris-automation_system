namespace HRM.Domain.Payroll;

/// <summary>
/// The fully-resolved, denormalized input to the payslip PDF renderer (US-PAY-004 FR-2). Built by the
/// generation service from <c>payroll_slip</c> + <c>payroll_slip_detail</c> (point-in-time component names,
/// BR-2), the employee record, and tenant branding (logo/name/address/footer via ITenantContext). The
/// renderer is a PURE function of this model — no DB, no tenant context — so it is trivially unit-testable.
/// </summary>
public sealed record PayslipDocumentModel
{
    // ── Header: company branding (FR-2 / BR-3) ─────────────────────────────────
    public required string CompanyName { get; init; }
    public string? CompanyAddress { get; init; }
    public string? CompanyLogoUrl { get; init; }
    /// <summary>
    /// Tenant logo image bytes (PNG/JPEG) rendered in the header (ISSUE-158). Resolved from the tenant's
    /// stored branding asset via IFileStorage by the generation service — NOT fetched over HTTP by the pure
    /// renderer. Null = render without a logo (graceful degrade).
    /// </summary>
    public byte[]? CompanyLogoBytes { get; init; }
    /// <summary>Tenant primary colour (hex, e.g. "#1d4ed8") applied to header/footer accents (§8). Null = default.</summary>
    public string? BrandPrimaryColor { get; init; }
    /// <summary>BR-3 disclaimer/footer text. Defaults to the system disclaimer when the tenant has none.</summary>
    public required string FooterDisclaimer { get; init; }

    // ── Pay period ─────────────────────────────────────────────────────────────
    public required int PayMonth { get; init; }
    public required int PayYear { get; init; }

    // ── Employee section (FR-2) ────────────────────────────────────────────────
    public required string EmployeeName { get; init; }
    public required string EmployeeNo { get; init; }
    public string? Department { get; init; }
    public string? Designation { get; init; }
    public DateTime? DateOfJoining { get; init; }

    // ── Earnings / deductions tables (FR-2) ────────────────────────────────────
    /// <summary>Earning + reimbursement lines (positive contributions to gross).</summary>
    public required IReadOnlyList<PayslipLine> Earnings { get; init; }
    /// <summary>Deduction + statutory lines (positive amounts that reduce net). Statutory itemized (FR-2).</summary>
    public required IReadOnlyList<PayslipLine> Deductions { get; init; }

    // ── Summary (FR-2) ─────────────────────────────────────────────────────────
    public required decimal GrossEarnings { get; init; }
    public required decimal TotalDeductions { get; init; }
    public required decimal NetSalary { get; init; }

    // ── Footer days (FR-2) ─────────────────────────────────────────────────────
    public required decimal WorkingDays { get; init; }
    public required decimal PaidDays { get; init; }
    public required decimal LopDays { get; init; }

    /// <summary>BR-4/FR-2: include the YTD column on the earnings/deductions tables when the tenant enables it.</summary>
    public bool ShowYtd { get; init; }
}

/// <summary>
/// One line on the payslip earnings/deductions table (US-PAY-004 FR-2). Component name is denormalized at
/// run time (BR-2). <see cref="YtdAmount"/> is the sum of this component across prior months in the same
/// fiscal/calendar year (BR-4), shown only when <see cref="PayslipDocumentModel.ShowYtd"/> is true.
/// </summary>
public sealed record PayslipLine(string ComponentName, bool IsStatutory, decimal Amount, decimal? YtdAmount);
