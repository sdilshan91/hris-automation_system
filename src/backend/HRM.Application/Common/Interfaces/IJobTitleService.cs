using HRM.Application.Common.Models;
using HRM.Application.Features.JobTitles.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Service interface for job title CRUD operations (US-CHR-005).
/// All operations are tenant-scoped via ITenantContext.
/// </summary>
public interface IJobTitleService
{
    Task<Result<JobTitleDto>> CreateAsync(
        string titleName, string? description, Guid? gradeId,
        CancellationToken cancellationToken = default);

    Task<Result<JobTitleDto>> UpdateAsync(
        Guid jobTitleId, string titleName, string? description, Guid? gradeId,
        CancellationToken cancellationToken = default);

    Task<Result> DeactivateAsync(
        Guid jobTitleId,
        CancellationToken cancellationToken = default);

    Task<Result<JobTitleDto>> GetByIdAsync(
        Guid jobTitleId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<JobTitleDto>>> GetAllAsync(
        bool? activeOnly = null,
        CancellationToken cancellationToken = default);
}
