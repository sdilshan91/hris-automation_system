using HRM.Application.Common.Models;
using HRM.Application.DTOs;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Enums;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Input payload for creating/updating a vacancy (US-REC-001 FR-1). Bundled into one record to keep
/// the service signatures readable. HTML fields (Description/Qualifications) are sanitized inside the
/// service before persistence (NFR-4).
/// </summary>
public sealed record VacancyInput(
    string Title,
    Guid? DepartmentId,
    Guid? JobTitleId,
    Guid? LocationId,
    Guid? HiringManagerId,
    EmploymentType EmploymentType,
    int Headcount,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryCurrency,
    string Description,
    string? Qualifications,
    DateOnly? ApplicationDeadline,
    bool PublishToPublicCareers);

/// <summary>
/// Vacancy lifecycle service (US-REC-001). All operations are tenant-scoped via ITenantContext and the
/// EF global query filter. Public (anonymous) careers reads are separate, see <see cref="IPublicCareersService"/>.
/// </summary>
public interface IVacancyService
{
    Task<Result<VacancyDto>> CreateAsync(VacancyInput input, CancellationToken cancellationToken = default);

    Task<Result<VacancyDto>> UpdateAsync(Guid vacancyId, VacancyInput input, CancellationToken cancellationToken = default);

    /// <summary>Publishes a Draft vacancy (Draft → Open, AC-2). Enforces BR-2 required fields and generates the SEO slug (FR-5).</summary>
    Task<Result<VacancyDto>> PublishAsync(Guid vacancyId, CancellationToken cancellationToken = default);

    /// <summary>Changes status with FR-2 transition validation. Closing routes through the same path (AC-5).</summary>
    Task<Result<VacancyDto>> ChangeStatusAsync(Guid vacancyId, VacancyStatus newStatus, CancellationToken cancellationToken = default);

    /// <summary>Convenience for AC-5 close (Open/OnHold → Closed).</summary>
    Task<Result<VacancyDto>> CloseAsync(Guid vacancyId, CancellationToken cancellationToken = default);

    /// <summary>Bulk status change (FR-6); per-item partial results.</summary>
    Task<Result<BulkStatusChangeResult>> BulkChangeStatusAsync(IReadOnlyList<Guid> vacancyIds, VacancyStatus newStatus, CancellationToken cancellationToken = default);

    Task<Result<VacancyDto>> GetByIdAsync(Guid vacancyId, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<VacancyListItemDto>>> ListAsync(
        VacancyStatus? status, Guid? departmentId, string? search, int page, int pageSize,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Anonymous public careers-page reads (US-REC-001 FR-4/FR-5/NFR-5). Scoped to the tenant resolved
/// by subdomain (TenantResolutionMiddleware runs before auth). Only Open, public-enabled vacancies of
/// a tenant whose PublicCareersEnabled toggle is on are returned.
/// </summary>
public interface IPublicCareersService
{
    Task<Result<IReadOnlyList<PublicVacancyListItemDto>>> ListOpenAsync(CancellationToken cancellationToken = default);

    Task<Result<PublicVacancyDetailDto>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
}
