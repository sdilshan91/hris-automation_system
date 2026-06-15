using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>
/// US-REC-007 (AC-2/FR-5/FR-7): sends a Draft offer to the applicant (status Draft→Sent), logs the
/// applicant email (log-only seam), and schedules the Hangfire expiry follow-up job. Tenant-scoped.
/// </summary>
public sealed record SendOfferCommand(Guid OfferId) : IRequest<Result<OfferDto>>;

public sealed class SendOfferCommandHandler
    : IRequestHandler<SendOfferCommand, Result<OfferDto>>
{
    private readonly IOfferService _service;

    public SendOfferCommandHandler(IOfferService service) => _service = service;

    public Task<Result<OfferDto>> Handle(SendOfferCommand request, CancellationToken cancellationToken)
        => _service.SendAsync(request.OfferId, cancellationToken);
}
