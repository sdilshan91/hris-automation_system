namespace HRM.Domain.Recruitment;

/// <summary>
/// Standardized offer-lifecycle audit action names (US-REC-007 / ISSUE-124) and the resource-type string they
/// target. String constants (NOT an enum) so the persisted/wire value is exactly the literal an auditor filters
/// on and never drifts with a C# rename — the same convention as
/// <see cref="HRM.Domain.Payroll.PayrollAuditAction"/>.
///
/// <para>Before ISSUE-124 no offer mutation wrote an <see cref="HRM.Domain.Entities.AuditLog"/> row at all: the
/// only trail for an offer's salary and status was the Serilog file, which the in-app audit-search surface
/// cannot read. Every offer write now picks its action from this catalog.</para>
/// </summary>
public static class OfferAuditAction
{
    /// <summary>The <c>audit_logs.resource_type</c> value every offer event targets.</summary>
    public const string ResourceType = "Offer";

    /// <summary>An offer letter was generated for an applicant (FR-2, status → Draft).</summary>
    public const string Generated = "Offer.Generated";

    /// <summary>A Draft offer was sent to the applicant (FR-5, status → Sent).</summary>
    public const string Sent = "Offer.Sent";

    /// <summary>The applicant's response was recorded (FR-6, status → Accepted or Declined).</summary>
    public const string Responded = "Offer.Responded";

    /// <summary>An offer was withdrawn before acceptance (FR-8, status → Withdrawn).</summary>
    public const string Withdrawn = "Offer.Withdrawn";

    /// <summary>
    /// An offer lapsed and was auto-expired by the background job (FR-7, status → Expired). Written with a
    /// null <c>user_id</c> — this is the system actor, not a person.
    /// </summary>
    public const string Expired = "Offer.Expired";
}
