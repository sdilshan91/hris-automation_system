using HRM.Application.Common.Models;
using HRM.Application.DTOs;
using HRM.Application.Features.Payroll.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>One component link in a structure create/update (US-PAY-001 FR-3).</summary>
public sealed record SalaryStructureComponentInput(
    Guid SalaryComponentId,
    decimal? OverrideValue,
    string? OverrideFormula,
    int ProcessingOrder,
    bool IsMandatory);

/// <summary>Input payload for creating/updating a salary structure (US-PAY-001 FR-2/FR-3).</summary>
public sealed record SalaryStructureInput(
    string Name,
    string Code,
    string? Description,
    DateOnly EffectiveFrom,
    bool IsDefault,
    bool IsActive,
    IReadOnlyList<SalaryStructureComponentInput> Components);

/// <summary>
/// Salary-structure service (US-PAY-001). All operations are tenant-scoped via ITenantContext and the EF
/// global query filter (AC-6). Enforces: unique code per tenant (BR-2), single default per tenant (BR-1),
/// at-least-one-earning before active (FR-5), formula validation on overrides (BR-6), clone (FR-6),
/// reorder (AC-4).
/// </summary>
public interface ISalaryStructureService
{
    Task<Result<SalaryStructureDto>> CreateAsync(SalaryStructureInput input, CancellationToken cancellationToken = default);

    Task<Result<SalaryStructureDto>> UpdateAsync(Guid structureId, SalaryStructureInput input, CancellationToken cancellationToken = default);

    Task<Result<SalaryStructureDto>> GetByIdAsync(Guid structureId, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<SalaryStructureListItemDto>>> ListAsync(
        bool? isActive, string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a structure (and its links).</summary>
    Task<Result> DeleteAsync(Guid structureId, CancellationToken cancellationToken = default);

    /// <summary>Links a single component to a structure (FR-3).</summary>
    Task<Result<SalaryStructureDto>> LinkComponentAsync(Guid structureId, SalaryStructureComponentInput input, CancellationToken cancellationToken = default);

    /// <summary>Removes a component link from a structure (FR-3). Blocked for mandatory statutory links (BR-3).</summary>
    Task<Result<SalaryStructureDto>> UnlinkComponentAsync(Guid structureId, Guid structureComponentId, CancellationToken cancellationToken = default);

    /// <summary>Reorders component processing priority within a structure (AC-4).</summary>
    Task<Result<SalaryStructureDto>> ReorderComponentsAsync(Guid structureId, IReadOnlyList<(Guid StructureComponentId, int ProcessingOrder)> order, CancellationToken cancellationToken = default);

    /// <summary>Clones an existing structure into a new one with a fresh name + code (FR-6).</summary>
    Task<Result<SalaryStructureDto>> CloneAsync(Guid structureId, string newName, string newCode, CancellationToken cancellationToken = default);
}
