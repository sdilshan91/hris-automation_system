using HRM.Application.Common.Models;
using HRM.Application.DTOs;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Domain.Enums;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Input for a single onboarding template task on create/clone (US-ONB-001 FR-3). Bundled into one
/// record to keep service signatures readable.
/// </summary>
public sealed record OnboardingTaskInput(
    string Title,
    string? Description,
    string? Category,
    OnboardingResponsibleRole ResponsibleRole,
    Guid? ResponsibleUserId,
    int DueOffsetDays,
    bool IsMandatory,
    int SortOrder);

/// <summary>Input for creating an onboarding checklist template (US-ONB-001 AC-1/AC-2).</summary>
public sealed record CreateOnboardingTemplateInput(
    string TemplateName,
    string? Description,
    IReadOnlyList<Guid> ApplicableDepartments,
    IReadOnlyList<Guid> ApplicableJobTitles,
    bool IsActive,
    IReadOnlyList<OnboardingTaskInput> Tasks);

/// <summary>
/// Onboarding checklist template service (US-ONB-001). All operations are tenant-scoped via
/// ITenantContext + the EF global query filter (AC-5 cross-tenant isolation). The tenant_id is always
/// taken from the resolved session context, never from user input (FR-5).
/// </summary>
public interface IOnboardingTemplateService
{
    /// <summary>Creates a template with its tasks (AC-1/AC-2). Enforces unique name (BR-1) + &gt;=1 task (BR-2).</summary>
    Task<Result<OnboardingTemplateDto>> CreateAsync(CreateOnboardingTemplateInput input, CancellationToken cancellationToken = default);

    /// <summary>Clones an existing template incl. all its tasks under a new name (FR-6).</summary>
    Task<Result<OnboardingTemplateDto>> CloneAsync(Guid templateId, string? newName, CancellationToken cancellationToken = default);

    /// <summary>Activates a template (FR-7).</summary>
    Task<Result<OnboardingTemplateDto>> ActivateAsync(Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>Deactivates a template without deletion (FR-7, BR-4).</summary>
    Task<Result<OnboardingTemplateDto>> DeactivateAsync(Guid templateId, CancellationToken cancellationToken = default);

    Task<Result<OnboardingTemplateDto>> GetByIdAsync(Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// B6: the department / job-title / user options the template builder's pickers need.
    /// </summary>
    /// <remarks>
    /// Users are the tenant's ACTIVE users, keyed by USER id — <c>OnboardingTemplateTask.ResponsibleUserId</c>
    /// is an FK to <c>users</c>, so returning employee ids would produce a picker whose every selection is
    /// rejected (or silently mis-bound) on save.
    /// </remarks>
    Task<Result<OnboardingLookupsDto>> GetLookupsAsync(CancellationToken cancellationToken = default);

    Task<Result<PagedResult<OnboardingTemplateListItemDto>>> ListAsync(
        bool? isActive, string? search, int page, int pageSize, CancellationToken cancellationToken = default);
}
