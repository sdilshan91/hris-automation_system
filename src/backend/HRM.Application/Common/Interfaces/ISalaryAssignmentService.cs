using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>A per-component override in an assignment (US-PAY-002 AC-3): a fixed annual amount by component id.</summary>
public sealed record SalaryOverrideInput(Guid SalaryComponentId, decimal AnnualAmount);

/// <summary>Input for assigning a salary structure to one employee (US-PAY-002 FR-1).</summary>
public sealed record AssignSalaryStructureInput(
    Guid EmployeeId,
    Guid SalaryStructureId,
    DateOnly EffectiveFrom,
    decimal AnnualCtc,
    string? Reason,
    IReadOnlyList<SalaryOverrideInput> Overrides);

/// <summary>Input for previewing a CTC breakdown without persisting (US-PAY-002 FR-3).</summary>
public sealed record PreviewCtcInput(
    Guid SalaryStructureId,
    decimal AnnualCtc,
    IReadOnlyList<SalaryOverrideInput> Overrides);

/// <summary>One employee in a bulk-assignment input (US-PAY-002 AC-4/FR-5).</summary>
public sealed record BulkAssignEntryInput(
    Guid EmployeeId,
    decimal AnnualCtc,
    IReadOnlyList<SalaryOverrideInput> Overrides);

/// <summary>Input for bulk-assigning one structure to many employees (US-PAY-002 AC-4/FR-5).</summary>
public sealed record BulkAssignInput(
    Guid SalaryStructureId,
    DateOnly EffectiveFrom,
    string? Reason,
    IReadOnlyList<BulkAssignEntryInput> Employees);

/// <summary>
/// Employee salary-assignment service (US-PAY-002). All operations are tenant-scoped via ITenantContext +
/// the EF global query filter (AC-5). Resolves CTC breakdowns from a structure's component rules (FR-2),
/// persists employee_salary_component + salary_revision_history rows (FR-1/FR-4/BR-3), honours
/// future-dated vs immediate supersession (BR-1/BR-2), rejects inactive structures (FR-7) and CTC/sum
/// mismatch beyond tolerance (FR-6).
/// </summary>
public interface ISalaryAssignmentService
{
    /// <summary>Resolves a CTC breakdown for a structure + CTC + overrides, WITHOUT persisting (FR-3).</summary>
    Task<Result<CtcBreakdownDto>> PreviewAsync(PreviewCtcInput input, CancellationToken cancellationToken = default);

    /// <summary>Assigns a structure to an employee, writing component rows + a revision-history row (AC-1/AC-3).</summary>
    Task<Result<CtcBreakdownDto>> AssignAsync(AssignSalaryStructureInput input, CancellationToken cancellationToken = default);

    /// <summary>Bulk-assigns one structure to many employees with individual CTCs (AC-4/FR-5).</summary>
    Task<Result<BulkAssignResultDto>> BulkAssignAsync(BulkAssignInput input, CancellationToken cancellationToken = default);

    /// <summary>Gets an employee's CURRENT compensation snapshot (FR-4 read side).</summary>
    Task<Result<EmployeeCompensationDto>> GetCurrentCompensationAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Gets an employee's salary-revision history, newest first (FR-4/BR-3).</summary>
    Task<Result<IReadOnlyList<SalaryRevisionDto>>> GetRevisionHistoryAsync(Guid employeeId, CancellationToken cancellationToken = default);
}
