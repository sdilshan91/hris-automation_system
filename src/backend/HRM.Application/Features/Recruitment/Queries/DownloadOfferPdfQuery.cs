using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>
/// US-REC-007 (AC-1/FR-4): streams the generated offer-letter PDF through the API (no direct blob URL —
/// NFR-3). Returns the bytes + filename + content type; tenant-scoped. 404 if the offer or file is gone.
/// </summary>
public sealed record DownloadOfferPdfQuery(Guid OfferId) : IRequest<Result<OfferDocumentDto>>;

public sealed class DownloadOfferPdfQueryHandler
    : IRequestHandler<DownloadOfferPdfQuery, Result<OfferDocumentDto>>
{
    private readonly IOfferService _service;

    public DownloadOfferPdfQueryHandler(IOfferService service) => _service = service;

    public Task<Result<OfferDocumentDto>> Handle(DownloadOfferPdfQuery request, CancellationToken cancellationToken)
        => _service.GetDocumentAsync(request.OfferId, cancellationToken);
}
