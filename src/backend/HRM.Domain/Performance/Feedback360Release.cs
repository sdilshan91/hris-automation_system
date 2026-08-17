using HRM.Domain.Entities;

namespace HRM.Domain.Performance;

/// <summary>
/// Records that a reviewee's aggregated 360-degree feedback results have been RELEASED to them for a cycle
/// (US-PRF-005 BR-4/FR-3). One row per reviewee per cycle — its existence is what flips the reviewee-facing
/// read path (<c>GET .../360/cycles/{cycleId}/my-results</c>) from 404 <c>not_released</c> to 200, and what
/// makes the HR results payload report <c>Released = true</c>.
///
/// <para>There is no per-reviewee 360 aggregate entity — "this person's 360 results" is a COMPUTED view
/// (<c>Feedback360Service.BuildResultsAsync</c>), so this release marker is the only durable place to hang the
/// release timestamp. Releasing is HARD-BLOCKED below the cycle's <see cref="AppraisalCycle.Min360PeerReviewers"/>
/// peer threshold (BR-4), enforced in the service before a row is written. Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c>. Maps to the
/// "feedback_360_release" table.</para>
/// </summary>
public sealed class Feedback360Release : BaseEntity
{
    /// <summary>The appraisal cycle whose 360 results are being released (required).</summary>
    public Guid CycleId { get; set; }

    /// <summary>The employee whose aggregated 360 results are released to them (required).</summary>
    public Guid RevieweeEmployeeId { get; set; }

    /// <summary>UTC instant the results were released.</summary>
    public DateTime ReleasedAt { get; set; }

    /// <summary>
    /// The employee who performed the release (HR officer or the reviewee's reporting manager). May be
    /// <see cref="System.Guid.Empty"/> when the releasing HR user has no linked employee record.
    /// <para>
    /// <b>Deliberately NOT nullable, and Guid.Empty is not an audit hole.</b> The durable "who released this"
    /// answer is <see cref="BaseEntity.CreatedBy"/>, stamped with the acting user id by
    /// <c>AuditInterceptor</c> on insert — which is populated even for a releaser with no employee record,
    /// precisely the case where this field falls back to empty. This property exists to answer
    /// "which EMPLOYEE released it" for in-app display; the compliance answer lives on the base entity.
    /// </para>
    /// </summary>
    public Guid ReleasedByEmployeeId { get; set; }

    // ── Navigation ─────────────────────────────────────────────────────
    public AppraisalCycle? Cycle { get; set; }
    public Employee? Reviewee { get; set; }
}
