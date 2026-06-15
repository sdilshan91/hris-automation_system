using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>
/// US-REC-007 (AC-3/BR-3): records the applicant's response to a Sent offer. Accepted → applicant advances
/// to Hired; Declined → applicant stays in the Offer stage. The recruiter records it manually (no
/// candidate portal in Phase 1). Tenant-scoped.
/// </summary>
public sealed record RespondToOfferCommand(Guid OfferId, bool Accepted) : IRequest<Result<OfferDto>>;

public sealed class RespondToOfferCommandHandler
    : IRequestHandler<RespondToOfferCommand, Result<OfferDto>>
{
    private readonly IOfferService _service;

    public RespondToOfferCommandHandler(IOfferService service) => _service = service;

    public Task<Result<OfferDto>> Handle(RespondToOfferCommand request, CancellationToken cancellationToken)
        => _service.RespondAsync(request.OfferId,
            new RespondToOfferInput { Accepted = request.Accepted }, cancellationToken);
}
