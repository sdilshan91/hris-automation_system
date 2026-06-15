using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>US-REC-007: gets a single offer by id (tenant-scoped).</summary>
public sealed record GetOfferByIdQuery(Guid OfferId) : IRequest<Result<OfferDto>>;

public sealed class GetOfferByIdQueryHandler
    : IRequestHandler<GetOfferByIdQuery, Result<OfferDto>>
{
    private readonly IOfferService _service;

    public GetOfferByIdQueryHandler(IOfferService service) => _service = service;

    public Task<Result<OfferDto>> Handle(GetOfferByIdQuery request, CancellationToken cancellationToken)
        => _service.GetByIdAsync(request.OfferId, cancellationToken);
}
