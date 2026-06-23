using HRM.Application.Common.Models;
using HRM.Application.DTOs;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Enums;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Input payload for creating/updating a salary component (US-PAY-001 FR-1). Bundled into one record to
/// keep service signatures readable.
/// </summary>
public sealed record SalaryComponentInput(
    string Name,
    string Code,
    SalaryComponentType Type,
    CalculationMethod CalculationMethod,
    decimal? DefaultValue,
    string? FormulaExpression,
    bool IsTaxable,
    bool IsStatutory,
    bool IsActive,
    int ProcessingOrder);

/// <summary>
/// Salary-component CRUD service (US-PAY-001). All operations are tenant-scoped via ITenantContext and
/// the EF global query filter (AC-6). Formula syntax + circular-ref validation (BR-6), unique code per
/// tenant (BR-2), and the delete-guard when a component is in use (AC-5) are enforced here.
/// </summary>
public interface ISalaryComponentService
{
    Task<Result<SalaryComponentDto>> CreateAsync(SalaryComponentInput input, CancellationToken cancellationToken = default);

    Task<Result<SalaryComponentDto>> UpdateAsync(Guid componentId, SalaryComponentInput input, CancellationToken cancellationToken = default);

    Task<Result<SalaryComponentDto>> GetByIdAsync(Guid componentId, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<SalaryComponentListItemDto>>> ListAsync(
        SalaryComponentType? type, bool? isActive, string? search, int page, int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a component. Blocked (409) when the component is linked to any structure (AC-5).</summary>
    Task<Result> DeleteAsync(Guid componentId, CancellationToken cancellationToken = default);
}
