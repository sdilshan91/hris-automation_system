using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A job offer extended to an <see cref="Applicant"/> for a <see cref="Vacancy"/> (US-REC-007).
/// Tenant-scoped via <see cref="BaseEntity.TenantId"/> and the EF global query filter +
/// <c>TenantInterceptor</c> (this codebase enforces tenant isolation with EF query filters, not Postgres
/// RLS — RLS/NFR-2 is a separate deferred story; see the module note). Maps to the "offer" table.
///
/// An applicant can have only one ACTIVE offer (Draft/Sent) per vacancy at a time (BR-2): generating a
/// new offer supersedes any prior active offer (it is withdrawn) and the new offer's <see cref="Version"/>
/// is bumped (FR-9 — supports salary renegotiation). The generated letter is rendered from a default
/// template with variable substitution and stored as a PDF via <c>IFileStorage</c> at the tenant-scoped
/// path <c>{tenantId}/recruitment/{vacancyId}/{applicantId}/offers/{uuid}.pdf</c> (FR-2/FR-3).
/// </summary>
public sealed class Offer : BaseEntity
{
    /// <summary>FK to the <see cref="Applicant"/> the offer is for (required). Plain UUID column.</summary>
    public Guid ApplicantId { get; set; }

    /// <summary>FK to the owning <see cref="Vacancy"/> (required). Plain UUID column.</summary>
    public Guid VacancyId { get; set; }

    /// <summary>
    /// Human-readable, tenant+year sequential reference, e.g. "OFR-2026-0001" (§7). Auto-generated on
    /// generate; unique per tenant. Mirrors the Vacancy/Applicant reference-number approach.
    /// </summary>
    public string OfferReferenceNumber { get; set; } = string.Empty;

    /// <summary>Offer lifecycle status (FR-6). Always <see cref="OfferStatus.Draft"/> on generation.</summary>
    public OfferStatus Status { get; set; } = OfferStatus.Draft;

    /// <summary>The offered position / job title (FR-1, required). Substituted as {{position}}.</summary>
    public string OfferedPosition { get; set; } = string.Empty;

    /// <summary>FK to the offered department (FR-1, optional). Plain UUID column.</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>FK to the reporting manager's <see cref="Employee"/> record (FR-1, optional). Plain UUID column.</summary>
    public Guid? ReportingManagerEmployeeId { get; set; }

    /// <summary>The offered salary amount (FR-1, required, &gt; 0). Substituted as {{salary}}.</summary>
    public decimal SalaryAmount { get; set; }

    /// <summary>ISO-4217 currency code for the salary (FR-1, required, e.g. "USD").</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Pay frequency of the offered salary (FR-1).</summary>
    public SalaryFrequency SalaryFrequency { get; set; } = SalaryFrequency.Monthly;

    /// <summary>Free-text benefits summary (FR-1, optional). Substituted as {{benefits}}.</summary>
    public string? BenefitsSummary { get; set; }

    /// <summary>Proposed start date (FR-1, required). Substituted as {{start_date}}.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>
    /// Offer expiry date (FR-1, mandatory — BR-6). Defaults to generation date + 7 days when not supplied.
    /// After this date passes with no response the Hangfire job auto-expires the offer (FR-7/AC-4).
    /// </summary>
    public DateOnly ExpiryDate { get; set; }

    /// <summary>Probation period in months (FR-1, optional).</summary>
    public int? ProbationMonths { get; set; }

    /// <summary>Optional custom clauses appended to the letter (FR-1). Plain text.</summary>
    public string? CustomClauses { get; set; }

    /// <summary>
    /// Tenant-scoped storage key/path of the generated offer-letter PDF (FR-2/FR-3). Set on generation.
    /// Path convention: <c>recruitment/{vacancyId}/{applicantId}/offers/{uuid}.pdf</c> (the tenant id
    /// prefix is added by <c>IFileStorage</c>).
    /// </summary>
    public string PdfStorageKey { get; set; } = string.Empty;

    /// <summary>
    /// Offer version (FR-9). 1 for the first offer; incremented when a new offer supersedes a prior active
    /// one for the same applicant/vacancy (e.g. salary renegotiation).
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>UTC instant the offer was sent to the applicant (AC-2). Null until sent.</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>UTC instant the applicant responded (AC-3). Null until accepted/declined.</summary>
    public DateTime? RespondedAt { get; set; }

    /// <summary>
    /// The recorded response when answered (AC-3) — the <see cref="OfferStatus"/> name "Accepted" or
    /// "Declined". Null until a response is recorded. Stored separately from <see cref="Status"/> so the
    /// recorded response survives later status transitions.
    /// </summary>
    public string? Response { get; set; }

    /// <summary>
    /// Id of the scheduled Hangfire expiry job (FR-7/AC-4). Set when the offer is sent (the expiry
    /// follow-up is scheduled), cleared on response/withdraw. Null when no job is active (e.g. the
    /// scheduler seam is unavailable in tests, or the fire-time is already past).
    /// </summary>
    public string? ReminderJobId { get; set; }

    /// <summary>
    /// Id of the scheduled Hangfire expiry-<em>reminder</em> job (US-REC-007 FR-7/AC-4). SEPARATE from
    /// <see cref="ReminderJobId"/> (which holds the auto-expire job) so cancelling one never clobbers the
    /// other. Set when the offer is sent (a reminder is scheduled N days before expiry), cleared on
    /// response/withdraw/supersede or once the reminder has fired. Null when no reminder job is active
    /// (scheduler seam unavailable in tests).
    /// </summary>
    public string? ExpiryReminderJobId { get; set; }

    /// <summary>True for the active states used by the one-active-offer rule (BR-2): Draft or Sent.</summary>
    public bool IsActive => Status == OfferStatus.Draft || Status == OfferStatus.Sent;
}
