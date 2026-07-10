using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Offer-letter generation, send, response recording, withdrawal, and reads (US-REC-007). All operations
/// are tenant-scoped via ITenantContext + the EF global query filter (AC-5 cross-tenant isolation).
/// Generation enforces one active offer per applicant/vacancy (BR-2) by superseding any prior active
/// offer (version bump, FR-9); renders the default offer-letter template with variable substitution and
/// stores it as a PDF via IFileStorage (FR-2/FR-3). Sending logs the applicant email (log-only seam, FR-5)
/// and schedules the Hangfire expiry job (FR-7). Acceptance advances the applicant to Hired (BR-3/AC-3).
/// </summary>
public interface IOfferService
{
    /// <summary>
    /// Generates a new offer (AC-1): supersedes any prior active offer for the applicant/vacancy (BR-2),
    /// bumps the version (FR-9), renders + stores the offer-letter PDF (FR-2/FR-3), and saves it as Draft.
    /// </summary>
    Task<Result<OfferDto>> GenerateAsync(GenerateOfferInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a Draft offer to the applicant (AC-2): status Draft→Sent, logs the applicant email (FR-5),
    /// and schedules the Hangfire expiry follow-up job (FR-7). A withdrawn offer cannot be sent (BR-7).
    /// </summary>
    Task<Result<OfferDto>> SendAsync(Guid offerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the applicant's response (AC-3): Accepted → applicant advances to Hired (BR-3); Declined →
    /// applicant stays in the Offer stage. Cancels the expiry job. Only a Sent offer can be responded to.
    /// </summary>
    Task<Result<OfferDto>> RespondAsync(Guid offerId, RespondToOfferInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Withdraws an offer before acceptance (FR-8): status → Withdrawn, cancels the expiry job. A withdrawn
    /// offer cannot be re-sent (BR-7) — a new offer must be generated.
    /// </summary>
    Task<Result<OfferDto>> WithdrawAsync(Guid offerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// US-ADM-011c FR-11 / US-REC-007 FR-10/BR-5: applies an approver's decision to the offer's approval-workflow
    /// instance via the generic runtime. Offer approval has no domain side-effect at approval time (Send is a
    /// separate recruiter action gated on the instance being Approved), so this just advances/completes the
    /// instance and maps the outcome. Returns 400 when the offer is not workflow-driven.
    /// </summary>
    Task<Result<OfferApprovalDecisionDto>> DecideApprovalAsync(
        Guid offerId, WorkflowDecisionAction action, string? comment, CancellationToken cancellationToken = default);

    /// <summary>Gets a single offer by id (tenant-scoped). 404 if not found / cross-tenant.</summary>
    Task<Result<OfferDto>> GetByIdAsync(Guid offerId, CancellationToken cancellationToken = default);

    /// <summary>Lists all offers for an applicant, newest version first (FR-9). Tenant-scoped.</summary>
    Task<Result<IReadOnlyList<OfferDto>>> ListByApplicantAsync(Guid applicantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams the generated offer-letter PDF through the API (AC-1/FR-4 preview + download). Returns the
    /// bytes + filename + content type; tenant-scoped. 404 if the offer or file is gone.
    /// </summary>
    Task<Result<OfferDocumentDto>> GetDocumentAsync(Guid offerId, CancellationToken cancellationToken = default);
}
