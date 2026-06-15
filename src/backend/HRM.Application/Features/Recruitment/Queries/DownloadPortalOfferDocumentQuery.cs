using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>
/// US-REC-008 (FR-4/AC-3): streams the offer-letter PDF for the candidate's own offer through the portal.
/// Anonymous — authenticated by the magic-link token, which must own the offer's applicant. Tenant-scoped.
/// </summary>
public sealed record DownloadPortalOfferDocumentQuery(string Token, Guid OfferId)
    : IRequest<Result<OfferDocumentDto>>;

public sealed class DownloadPortalOfferDocumentQueryHandler
    : IRequestHandler<DownloadPortalOfferDocumentQuery, Result<OfferDocumentDto>>
{
    private readonly IApplicantPortalService _service;

    public DownloadPortalOfferDocumentQueryHandler(IApplicantPortalService service) => _service = service;

    public Task<Result<OfferDocumentDto>> Handle(DownloadPortalOfferDocumentQuery request, CancellationToken cancellationToken)
        => _service.DownloadOfferDocumentAsync(request.Token, request.OfferId, cancellationToken);
}
