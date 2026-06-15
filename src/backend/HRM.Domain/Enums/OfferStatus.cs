namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of a job offer (US-REC-007 FR-6). A generated offer starts at <see cref="Draft"/>;
/// the recruiter sends it (→ <see cref="Sent"/>), the applicant accepts/declines (→ <see cref="Accepted"/>
/// / <see cref="Declined"/>), the Hangfire expiry job auto-expires an unanswered Draft/Sent offer
/// (→ <see cref="Expired"/>), or the recruiter withdraws it (→ <see cref="Withdrawn"/>). The "active"
/// states for the one-active-offer rule (BR-2) are Draft + Sent. Stored as a string column for
/// readability/forward-compat (the codebase's varchar-enum pattern). API uses a global
/// JsonStringEnumConverter, so the FE consumes these exact PascalCase member names (US-PLT-003).
/// </summary>
public enum OfferStatus
{
    /// <summary>Generated + stored, not yet sent (AC-1). Default on generation. An active offer (BR-2).</summary>
    Draft = 0,

    /// <summary>Sent to the applicant; awaiting response (AC-2). An active offer (BR-2).</summary>
    Sent = 1,

    /// <summary>The applicant accepted; the applicant advances to Hired (AC-3/BR-3).</summary>
    Accepted = 2,

    /// <summary>The applicant declined; the applicant remains in the Offer stage (AC-3).</summary>
    Declined = 3,

    /// <summary>The expiry date passed with no response; auto-set by the Hangfire job (AC-4/FR-7).</summary>
    Expired = 4,

    /// <summary>The recruiter withdrew the offer before acceptance (FR-8). Cannot be re-sent (BR-7).</summary>
    Withdrawn = 5,
}
