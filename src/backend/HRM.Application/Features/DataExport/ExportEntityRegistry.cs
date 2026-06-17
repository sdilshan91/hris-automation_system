namespace HRM.Application.Features.DataExport;

/// <summary>
/// US-ADM-010 (§7 / FR-1): the CURATED registry of exportable entities. A curated, code-keyed list (rather than
/// blindly iterating every <c>BaseEntity</c> in the model) was chosen because it makes a PARTIAL export trivial
/// — the request's scope is a set of these stable codes — and it gives the schema a stable, documented surface
/// (§7) instead of leaking every internal join/history table. Each entry pairs a stable export <c>Code</c> with
/// the EF CLR entity type name and the output CSV filename.
///
/// <para>The generation service resolves the CLR type by name from the EF model at runtime, so this registry
/// stays in the Application layer (no Domain entity reference needed) and silently skips any code whose entity
/// is not yet present in the model — new modules' entities become exportable simply by being added here.</para>
///
/// <para><b>Users + Audit Log are special</b> and NOT in this registry: Users is a GLOBAL (non-tenant) entity
/// exported with a hand-picked name/email/roles projection (BR-7 — no credential columns), and the audit log is
/// emitted as <c>audit_log.jsonl</c>, not a CSV. Both are always included in a FULL export and addressable in a
/// partial export via the reserved codes "Users" / "AuditLog".</para>
/// </summary>
public static class ExportEntityRegistry
{
    /// <summary>Reserved code for the Users projection (name/email/roles only).</summary>
    public const string UsersCode = "Users";

    /// <summary>Reserved code for the audit-log JSONL export.</summary>
    public const string AuditLogCode = "AuditLog";

    /// <summary>The literal full-export scope value.</summary>
    public const string FullScope = "full";

    /// <summary>A curated entity: stable export code, the EF entity CLR type name, and the output CSV filename.</summary>
    public sealed record Entry(string Code, string ClrTypeName, string FileName);

    /// <summary>
    /// The curated CSV entities (§7). Codes are the public, stable identifiers a partial export selects. The CLR
    /// type names are resolved against the EF model at generation time; an entry whose type is absent is skipped.
    /// </summary>
    public static IReadOnlyList<Entry> Entries { get; } =
    [
        new("Employees", "Employee", "employees.csv"),
        new("Departments", "Department", "departments.csv"),
        new("JobTitles", "JobTitle", "job_titles.csv"),
        new("Locations", "Location", "locations.csv"),
        new("LeaveTypes", "LeaveType", "leave_types.csv"),
        new("LeaveRequests", "LeaveRequest", "leave_requests.csv"),
        new("LeaveBalances", "LeaveLedger", "leave_balances.csv"),
        new("Holidays", "Holiday", "holidays.csv"),
        new("AttendanceRecords", "AttendanceLog", "attendance_records.csv"),
        new("Shifts", "Shift", "shifts.csv"),
        new("PayrollRuns", "PayrollRun", "payroll_runs.csv"),
        new("Payslips", "PayrollSlip", "payslips.csv"),
        new("SalaryStructures", "SalaryStructure", "salary_structures.csv"),
        new("SalaryComponents", "SalaryComponent", "salary_components.csv"),
        new("Vacancies", "Vacancy", "recruitment_vacancies.csv"),
        new("Applicants", "Applicant", "applicants.csv"),
        new("Interviews", "Interview", "interviews.csv"),
        new("Offers", "Offer", "offers.csv"),
        new("PerformanceGoals", "Goal", "performance_goals.csv"),
        new("Appraisals", "ManagerReview", "appraisals.csv"),
        new("CustomFields", "CustomFieldDefinition", "custom_fields.csv"),
        new("WorkflowDefinitions", "WorkflowDefinition", "workflow_definitions.csv"),
    ];

    private static readonly Dictionary<string, Entry> ByCode =
        Entries.ToDictionary(e => e.Code, StringComparer.OrdinalIgnoreCase);

    /// <summary>Looks up a curated CSV entity by code (case-insensitive). Null for "Users"/"AuditLog"/unknown.</summary>
    public static Entry? Find(string code) => ByCode.GetValueOrDefault(code);

    /// <summary>True when the code addresses the Users projection.</summary>
    public static bool IsUsers(string code) => string.Equals(code, UsersCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the code addresses the audit-log JSONL.</summary>
    public static bool IsAuditLog(string code) => string.Equals(code, AuditLogCode, StringComparison.OrdinalIgnoreCase);
}
