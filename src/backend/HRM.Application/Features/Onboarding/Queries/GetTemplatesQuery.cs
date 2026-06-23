using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.DTOs;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Queries;

/// <summary>Lists onboarding checklist templates for the current tenant, paged + filterable (US-ONB-001).</summary>
public sealed record GetTemplatesQuery(bool? IsActive, string? Search, int Page, int PageSize)
    : IRequest<Result<PagedResult<OnboardingTemplateListItemDto>>>;

public sealed class GetTemplatesQueryHandler
    : IRequestHandler<GetTemplatesQuery, Result<PagedResult<OnboardingTemplateListItemDto>>>
{
    private readonly IOnboardingTemplateService _service;

    public GetTemplatesQueryHandler(IOnboardingTemplateService service) => _service = service;

    public Task<Result<PagedResult<OnboardingTemplateListItemDto>>> Handle(GetTemplatesQuery request, CancellationToken cancellationToken)
        => _service.ListAsync(request.IsActive, request.Search, request.Page, request.PageSize, cancellationToken);
}
