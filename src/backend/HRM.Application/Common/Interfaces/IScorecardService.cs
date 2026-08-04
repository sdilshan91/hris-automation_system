using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Structured interview scorecard submission + reads (US-REC-006). All operations are tenant-scoped via
/// ITenantContext + the EF global query filter (AC-4 cross-tenant isolation). Submission enforces: the
/// submitter is the assigned interviewer (BR-1), exactly one scorecard per interviewer per interview with
/// edits allowed until the lock period (FR-2/BR-4), a mandatory overall recommendation (BR-3), and a
/// computed average (FR-3). When all assigned interviewers have submitted the interview is marked Completed
/// (FR-4); the recruiter is notified via the log-only seam (FR-5) and the submission is audit-logged (FR-7).
/// Reads apply the anti-bias rule (FR-6/BR-5): an interviewer who has not submitted cannot see peers' cards.
/// </summary>
public interface IScorecardService
{
    /// <summary>
    /// Submits or edits the current interviewer's scorecard for an interview (AC-1/FR-1/FR-2/FR-3/BR-1/BR-3).
    /// Marks the interview Completed when all assigned interviewers have submitted (FR-4), notifies the
    /// recruiter (FR-5) and writes an audit log (FR-7). Returns the saved scorecard.
    /// </summary>
    Task<Result<ScorecardDto>> SubmitAsync(SubmitScorecardInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the scorecards for one interview the caller is allowed to see (AC-2/AC-3), with the aggregate
    /// average across the VISIBLE cards (FR-3). Applies the anti-bias rule (FR-6/BR-5): a recruiter sees all;
    /// an assigned interviewer who has not submitted sees none of the peers' cards.
    /// </summary>
    Task<Result<ScorecardSummaryDto>> GetForInterviewAsync(Guid interviewId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the scorecards across ALL of an applicant's interviews the caller is allowed to see (AC-2/AC-3),
    /// with the aggregate average across the visible cards (FR-3). Same anti-bias rule as the per-interview
    /// read (FR-6/BR-5).
    /// </summary>
    Task<Result<ScorecardSummaryDto>> GetForApplicantAsync(Guid applicantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// US-REC-006 AC-K1: one scorecard by id, with its full edit history. Applies the SAME anti-bias
    /// visibility rule as the summary views — a caller who cannot see a scorecard in the list cannot fetch it
    /// directly either, and gets 404 rather than 403 so the endpoint does not confirm it exists.
    /// </summary>
    Task<Result<ScorecardDetailDto>> GetByIdAsync(Guid scorecardId, CancellationToken cancellationToken = default);

    /// <summary>Returns the default evaluation criteria (US-REC-006 FR-1/BR-2). Tenant-agnostic in Phase 1.</summary>
    Result<IReadOnlyList<ScorecardCriterionDto>> GetCriteria();
}
