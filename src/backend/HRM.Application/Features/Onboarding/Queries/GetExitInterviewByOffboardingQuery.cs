using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Queries;

/// <summary>
/// US-ONB-006 FR-3: gets the active exit interview for an offboarding instance (or 404).
/// <see cref="IncludeFreeText"/> includes PII free-text answers/comments only for detail-permitted callers
/// (FR-5/NFR-6) — the controller sets it from the ExitInterview.ViewDetail permission.
/// </summary>
public sealed record GetExitInterviewByOffboardingQuery(Guid OffboardingId, bool IncludeFreeText)
    : IRequest<Result<ExitInterviewDto>>;

public sealed class GetExitInterviewByOffboardingQueryHandler
    : IRequestHandler<GetExitInterviewByOffboardingQuery, Result<ExitInterviewDto>>
{
    private readonly IExitInterviewService _service;

    public GetExitInterviewByOffboardingQueryHandler(IExitInterviewService service) => _service = service;

    public Task<Result<ExitInterviewDto>> Handle(
        GetExitInterviewByOffboardingQuery request, CancellationToken cancellationToken)
        => _service.GetByOffboardingAsync(request.OffboardingId, request.IncludeFreeText, cancellationToken);
}
