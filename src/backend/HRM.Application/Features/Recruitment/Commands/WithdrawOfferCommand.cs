using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>
/// US-REC-007 (FR-8/BR-7): withdraws an offer before acceptance (status → Withdrawn) and cancels the
/// expiry job. A withdrawn offer cannot be re-sent — a new offer must be generated. Tenant-scoped.
/// </summary>
public sealed record WithdrawOfferCommand(Guid OfferId) : IRequest<Result<OfferDto>>;

public sealed class WithdrawOfferCommandHandler
    : IRequestHandler<WithdrawOfferCommand, Result<OfferDto>>
{
    private readonly IOfferService _service;

    public WithdrawOfferCommandHandler(IOfferService service) => _service = service;

    public Task<Result<OfferDto>> Handle(WithdrawOfferCommand request, CancellationToken cancellationToken)
        => _service.WithdrawAsync(request.OfferId, cancellationToken);
}
