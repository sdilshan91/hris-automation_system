using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Queries;

/// <summary>US-ONB-006 FR-1/AC-1: gets the tenant's exit-interview questionnaire template (seeds DEFAULT if none).</summary>
public sealed record GetExitInterviewTemplateQuery : IRequest<Result<ExitInterviewTemplateDto>>;

public sealed class GetExitInterviewTemplateQueryHandler
    : IRequestHandler<GetExitInterviewTemplateQuery, Result<ExitInterviewTemplateDto>>
{
    private readonly IExitInterviewService _service;

    public GetExitInterviewTemplateQueryHandler(IExitInterviewService service) => _service = service;

    public Task<Result<ExitInterviewTemplateDto>> Handle(
        GetExitInterviewTemplateQuery request, CancellationToken cancellationToken)
        => _service.GetTemplateAsync(cancellationToken);
}
