using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>US-REC-007: lists all offers for an applicant, newest version first (FR-9). Tenant-scoped.</summary>
public sealed record ListOffersForApplicantQuery(Guid ApplicantId) : IRequest<Result<IReadOnlyList<OfferDto>>>;

public sealed class ListOffersForApplicantQueryHandler
    : IRequestHandler<ListOffersForApplicantQuery, Result<IReadOnlyList<OfferDto>>>
{
    private readonly IOfferService _service;

    public ListOffersForApplicantQueryHandler(IOfferService service) => _service = service;

    public Task<Result<IReadOnlyList<OfferDto>>> Handle(ListOffersForApplicantQuery request, CancellationToken cancellationToken)
        => _service.ListByApplicantAsync(request.ApplicantId, cancellationToken);
}
