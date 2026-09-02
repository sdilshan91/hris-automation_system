using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Queries;

/// <summary>
/// US-ONB-002 FR-2/BR-4: previews the task instances a template WOULD create for an employee — server
/// calculated due dates and resolved responsible parties — without assigning anything.
///
/// <para>This is a QUERY, not a command, and the distinction is load-bearing: the handler must not create a
/// checklist instance, task instances or outbox rows as a side effect of being looked at. The assignment
/// screen calls it every time the HR officer picks a template from the dropdown.</para>
/// </summary>
public sealed record GetChecklistPreviewQuery(Guid EmployeeId, Guid TemplateId)
    : IRequest<Result<ChecklistPreviewDto>>;

public sealed class GetChecklistPreviewQueryHandler
    : IRequestHandler<GetChecklistPreviewQuery, Result<ChecklistPreviewDto>>
{
    private readonly IOnboardingChecklistService _service;

    public GetChecklistPreviewQueryHandler(IOnboardingChecklistService service) => _service = service;

    public Task<Result<ChecklistPreviewDto>> Handle(
        GetChecklistPreviewQuery request, CancellationToken cancellationToken)
        => _service.PreviewAsync(request.EmployeeId, request.TemplateId, cancellationToken);
}
