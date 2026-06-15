using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>
/// US-REC-008 (FR-5/AC-3/BR-2): the candidate accepts or declines an offer via the portal. Anonymous —
/// authenticated by the magic-link token, which must own the offer's applicant. Accept advances the applicant
/// to Hired (reuses the OfferService respond path, BR-3). One-time (BR-2).
/// </summary>
public sealed record RespondToPortalOfferCommand(string Token, Guid OfferId, bool Accept)
    : IRequest<Result<PortalOfferDto>>;

public sealed class RespondToPortalOfferCommandHandler
    : IRequestHandler<RespondToPortalOfferCommand, Result<PortalOfferDto>>
{
    private readonly IApplicantPortalService _service;

    public RespondToPortalOfferCommandHandler(IApplicantPortalService service) => _service = service;

    public Task<Result<PortalOfferDto>> Handle(RespondToPortalOfferCommand request, CancellationToken cancellationToken)
        => _service.RespondToOfferAsync(request.Token, request.OfferId, request.Accept, cancellationToken);
}
