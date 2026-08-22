namespace HRM.Application.Features.Onboarding.DTOs;

/// <summary>One selectable option in the template builder's pickers.</summary>
public sealed record LookupOptionDto(Guid Id, string Name);

/// <summary>
/// B6 — the scope/responsibility lookups the onboarding template builder needs.
/// </summary>
/// <remarks>
/// <para>
/// The frontend has called <c>GET /onboarding/templates/lookups</c> since the builder shipped; no controller
/// served it and it was absent from the OpenAPI contract, so every picker in the builder was empty.
/// </para>
/// <para>
/// <b>Users are USER ids, not employee ids — and that distinction is the whole risk here.</b>
/// <c>OnboardingTemplateTask.ResponsibleUserId</c> is a nullable FK to <c>users</c>, so a picker populated
/// with employee ids would look completely correct and then fail on save, or worse, bind to whichever
/// unrelated row shared that id. Listing the wrong entity is precisely the FE↔BE contract-drift class this
/// codebase keeps paying for.
/// </para>
/// </remarks>
public sealed record OnboardingLookupsDto
{
    public IReadOnlyList<LookupOptionDto> Departments { get; init; } = [];
    public IReadOnlyList<LookupOptionDto> JobTitles { get; init; } = [];

    /// <summary>Active users of the current tenant — the candidates for a task's responsible user.</summary>
    public IReadOnlyList<LookupOptionDto> Users { get; init; } = [];
}
