using HRM.Application.Common.Models;
using HRM.Application.Features.SalaryGrades.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Service interface for salary grade CRUD operations (Payroll domain, ISSUE-021).
/// All operations are tenant-scoped via ITenantContext.
/// </summary>
public interface ISalaryGradeService
{
    Task<Result<SalaryGradeDto>> CreateAsync(
        CreateSalaryGradeRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<SalaryGradeDto>> UpdateAsync(
        Guid salaryGradeId,
        UpdateSalaryGradeRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> DeactivateAsync(
        Guid salaryGradeId,
        CancellationToken cancellationToken = default);

    Task<Result<SalaryGradeDto>> GetByIdAsync(
        Guid salaryGradeId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SalaryGradeDto>>> GetAllAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);
}
