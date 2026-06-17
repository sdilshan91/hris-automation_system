using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Commands;

/// <summary>Creates an onboarding checklist template with its tasks (US-ONB-001 AC-1/AC-2).</summary>
public sealed record CreateChecklistTemplateCommand(
    string TemplateName,
    string? Description,
    IReadOnlyList<Guid> ApplicableDepartments,
    IReadOnlyList<Guid> ApplicableJobTitles,
    bool IsActive,
    IReadOnlyList<CreateChecklistTemplateTask> Tasks
) : IRequest<Result<OnboardingTemplateDto>>;

/// <summary>A task line on the create command (US-ONB-001 FR-3).</summary>
public sealed record CreateChecklistTemplateTask(
    string Title,
    string? Description,
    string? Category,
    Domain.Enums.OnboardingResponsibleRole ResponsibleRole,
    Guid? ResponsibleUserId,
    int DueOffsetDays,
    bool IsMandatory,
    int SortOrder);

public sealed class CreateChecklistTemplateCommandHandler
    : IRequestHandler<CreateChecklistTemplateCommand, Result<OnboardingTemplateDto>>
{
    private readonly IOnboardingTemplateService _service;

    public CreateChecklistTemplateCommandHandler(IOnboardingTemplateService service) => _service = service;

    public Task<Result<OnboardingTemplateDto>> Handle(CreateChecklistTemplateCommand request, CancellationToken cancellationToken)
        => _service.CreateAsync(new CreateOnboardingTemplateInput(
            request.TemplateName,
            request.Description,
            request.ApplicableDepartments,
            request.ApplicableJobTitles,
            request.IsActive,
            request.Tasks.Select(t => new OnboardingTaskInput(
                t.Title, t.Description, t.Category, t.ResponsibleRole, t.ResponsibleUserId,
                t.DueOffsetDays, t.IsMandatory, t.SortOrder)).ToList()),
            cancellationToken);
}
