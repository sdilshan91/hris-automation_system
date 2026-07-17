namespace HRM.Domain.Payroll;

/// <summary>
/// Standardized payroll audit action names (US-PAY-012 §7) and the resource-type strings they target.
/// String constants (NOT an enum) so the persisted/wire value is exactly the data-spec literal and never
/// drifts with a rename. Every payroll write that emits an <see cref="HRM.Domain.Entities.AuditLog"/> picks
/// its action from this catalog.
/// </summary>
public static class PayrollAuditAction
{
    // Salary configuration
    public const string SalaryComponentCreated = "SalaryComponent.Created";
    public const string SalaryComponentUpdated = "SalaryComponent.Updated";
    public const string SalaryComponentDeleted = "SalaryComponent.Deleted";
    public const string SalaryStructureCreated = "SalaryStructure.Created";
    public const string SalaryStructureUpdated = "SalaryStructure.Updated";
    // ISSUE-108 (NFR-5): structure delete + component link/unlink/reorder must also be audited.
    public const string SalaryStructureDeleted = "SalaryStructure.Deleted";
    public const string SalaryStructureComponentLinked = "SalaryStructure.ComponentLinked";
    public const string SalaryStructureComponentUnlinked = "SalaryStructure.ComponentUnlinked";
    public const string SalaryStructureComponentsReordered = "SalaryStructure.ComponentsReordered";

    // Employee salary
    public const string EmployeeSalaryAssigned = "EmployeeSalary.Assigned";
    public const string EmployeeSalaryRevised = "EmployeeSalary.Revised";

    // Statutory
    public const string StatutoryRuleCreated = "StatutoryRule.Created";
    public const string StatutoryRuleUpdated = "StatutoryRule.Updated";

    // Payroll run lifecycle
    public const string PayrollRunInitiated = "PayrollRun.Initiated";
    public const string PayrollRunCompleted = "PayrollRun.Completed";
    public const string PayrollRunSubmittedForApproval = "PayrollRun.SubmittedForApproval";
    public const string PayrollRunApproved = "PayrollRun.Approved";
    public const string PayrollRunRejected = "PayrollRun.Rejected";
    public const string PayrollRunFinalized = "PayrollRun.Finalized";
    public const string PayrollRunCancelled = "PayrollRun.Cancelled";
    // ISSUE-154: HR re-ran a processed-but-not-approved run in place (re-enqueued the processing job).
    public const string PayrollRunReprocessed = "PayrollRun.Reprocessed";

    // Adjustments
    public const string PayrollAdjustmentCreated = "PayrollAdjustment.Created";
    public const string PayrollAdjustmentCancelled = "PayrollAdjustment.Cancelled";

    // Payslip distribution
    public const string PayslipPdfGenerated = "PayslipPDF.Generated";
    public const string PayslipEmailSent = "PayslipEmail.Sent";

    // Reports (US-RPT-003 NFR-3): sensitive-PII reveal on a payroll report (e.g. unmasked bank account).
    public const string PayrollReportViewSensitive = "PayrollReport.ViewSensitive";

    // BUG-083 (PII-read audit): a deliberate sensitive-reveal READ of full salary / compensation. Follows the
    // shipped ".ViewSensitive" convention (same suffix as PayrollReportViewSensitive) so every PII-access audit
    // is queryable together. The After JSON NAMES the accessed fields but stores NO values.
    public const string PayslipViewSensitive = "Payslip.ViewSensitive";
    public const string RecommendationViewSensitive = "Recommendation.ViewSensitive";

    /// <summary>The set of resource-type strings used by payroll audit entries (§7 resource_type).</summary>
    public static class ResourceType
    {
        public const string SalaryComponent = "SalaryComponent";
        public const string SalaryStructure = "SalaryStructure";
        public const string EmployeeSalary = "EmployeeSalary";
        public const string StatutoryRule = "StatutoryRule";
        public const string PayrollRun = "PayrollRun";
        public const string PayrollAdjustment = "PayrollAdjustment";
        public const string PayrollSlip = "PayrollSlip";
        public const string PayrollReport = "PayrollReport";
        // BUG-083 sensitive-reveal resource types.
        public const string Payslip = "Payslip";
        public const string Recommendation = "Recommendation";
    }

    /// <summary>
    /// The "system actor" id used for system/Hangfire-job-initiated audit entries (BR-7) — a fixed,
    /// well-known UUID so job-driven actions are attributable to a non-null actor rather than the empty Guid.
    /// </summary>
    public static readonly Guid SystemActorUserId = Guid.Parse("00000000-0000-0000-0000-0000000515E5");
}
