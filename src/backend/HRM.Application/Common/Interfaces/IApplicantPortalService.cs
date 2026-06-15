using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// The candidate-portal application service (US-REC-008). Serves the anonymous, magic-link-authenticated
/// portal: building the sanitized dashboard (FR-2/AC-1), responding to offers (FR-5/AC-3), and streaming the
/// resume + offer-letter downloads (FR-4). Every operation takes a presented magic-link token, validated via
/// <see cref="IApplicantPortalTokenService"/> against the CURRENT (subdomain-resolved) tenant (AC-4/BR-4);
/// the EF global query filter provides the second isolation layer. NOTHING sensitive (rejection reasons,
/// scorecards, internal notes) is ever projected into the portal DTOs (NFR-5/BR-3).
/// </summary>
public interface IApplicantPortalService
{
    /// <summary>
    /// Builds the sanitized dashboard for the email behind a valid token (FR-2/AC-1/AC-2/AC-3/FR-6). 401 if
    /// the token is invalid/expired/cross-tenant.
    /// </summary>
    Task<Result<PortalDashboardDto>> GetDashboardAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the candidate's accept/decline of an offer via the portal (FR-5/AC-3). The token must own the
    /// offer's applicant. Accept advances the applicant to Hired (reuses the OfferService respond path, BR-3).
    /// One-time (BR-2): only a Sent offer can be responded to. 401 invalid token; 403 if the offer is not the
    /// token-holder's; 404/409 per the offer lifecycle.
    /// </summary>
    Task<Result<PortalOfferDto>> RespondToOfferAsync(
        string token, Guid offerId, bool accept, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams the candidate's own resume (FR-4). The token must own the applicant. 401/403/404 as above.
    /// </summary>
    Task<Result<ResumeDownloadDto>> DownloadResumeAsync(
        string token, Guid applicantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams the offer-letter PDF for the candidate's own offer (FR-4). The token must own the offer's
    /// applicant. 401/403/404 as above.
    /// </summary>
    Task<Result<OfferDocumentDto>> DownloadOfferDocumentAsync(
        string token, Guid offerId, CancellationToken cancellationToken = default);
}
