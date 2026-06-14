using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Anonymous public careers-page reads (US-REC-001 FR-4/FR-5/NFR-5). The tenant is resolved by
/// subdomain in TenantResolutionMiddleware (before auth), so ITenantContext is populated even for
/// unauthenticated requests; the EF global query filter then scopes every read to that tenant. Only
/// Open + public-enabled vacancies of a tenant whose PublicCareersEnabled toggle is on are exposed
/// (BR-5). Returns empty / not-found when the toggle is off — never an internal/Draft vacancy.
/// </summary>
public sealed class PublicCareersService : IPublicCareersService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<PublicCareersService> _logger;

    public PublicCareersService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ILogger<PublicCareersService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<PublicVacancyListItemDto>>> ListOpenAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<PublicVacancyListItemDto>>.Failure("Careers page is not available.", 404, "careers_unavailable");

        if (!await IsCareersEnabledAsync(cancellationToken))
            // Toggle off ⇒ nothing public. Return an empty list (page renders "no openings") rather than 404.
            return Result<IReadOnlyList<PublicVacancyListItemDto>>.Success([]);

        var vacancies = await _dbContext.Vacancies
            .AsNoTracking()
            .Where(v => v.Status == VacancyStatus.Open && v.PublishToPublicCareers && v.Slug != null)
            .OrderByDescending(v => v.PublishedAt)
            .ToListAsync(cancellationToken);

        var deptIds = vacancies.Where(v => v.DepartmentId.HasValue).Select(v => v.DepartmentId!.Value).Distinct().ToList();
        var locIds = vacancies.Where(v => v.LocationId.HasValue).Select(v => v.LocationId!.Value).Distinct().ToList();
        var departments = deptIds.Count == 0 ? new() : await _dbContext.Departments
            .Where(d => deptIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);
        var locations = locIds.Count == 0 ? new() : await _dbContext.Locations
            .Where(l => locIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);

        var items = vacancies.Select(v => new PublicVacancyListItemDto
        {
            Slug = v.Slug!,
            ReferenceNumber = v.ReferenceNumber,
            Title = v.Title,
            DepartmentName = NameOrNull(departments, v.DepartmentId),
            LocationName = NameOrNull(locations, v.LocationId),
            EmploymentType = v.EmploymentType,
            EmploymentTypeName = v.EmploymentType.ToString(),
            ApplicationDeadline = v.ApplicationDeadline,
            PublishedAt = v.PublishedAt,
        }).ToList();

        return Result<IReadOnlyList<PublicVacancyListItemDto>>.Success(items);
    }

    public async Task<Result<PublicVacancyDetailDto>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PublicVacancyDetailDto>.Failure("Careers page is not available.", 404, "careers_unavailable");

        if (string.IsNullOrWhiteSpace(slug))
            return Result<PublicVacancyDetailDto>.Failure("Vacancy not found.", 404);

        if (!await IsCareersEnabledAsync(cancellationToken))
            return Result<PublicVacancyDetailDto>.Failure("Vacancy not found.", 404);

        var v = await _dbContext.Vacancies
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Slug == slug && x.Status == VacancyStatus.Open && x.PublishToPublicCareers,
                cancellationToken);

        if (v is null)
            return Result<PublicVacancyDetailDto>.Failure("Vacancy not found.", 404);

        string? deptName = v.DepartmentId is { } d
            ? await _dbContext.Departments.Where(x => x.Id == d).Select(x => x.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        string? locName = v.LocationId is { } l
            ? await _dbContext.Locations.Where(x => x.Id == l).Select(x => x.Name).FirstOrDefaultAsync(cancellationToken)
            : null;

        return Result<PublicVacancyDetailDto>.Success(new PublicVacancyDetailDto
        {
            Slug = v.Slug!,
            ReferenceNumber = v.ReferenceNumber,
            Title = v.Title,
            DepartmentName = deptName,
            LocationName = locName,
            EmploymentType = v.EmploymentType,
            EmploymentTypeName = v.EmploymentType.ToString(),
            SalaryMin = v.SalaryMin,
            SalaryMax = v.SalaryMax,
            SalaryCurrency = v.SalaryCurrency,
            Description = v.Description,
            Qualifications = v.Qualifications,
            ApplicationDeadline = v.ApplicationDeadline,
            PublishedAt = v.PublishedAt,
        });
    }

    private async Task<bool> IsCareersEnabledAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        // Tenant has no tenant-scoped global filter (it IS the tenant); look it up directly.
        return await _dbContext.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.PublicCareersEnabled)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string? NameOrNull(Dictionary<Guid, string> map, Guid? id)
        => id.HasValue && map.TryGetValue(id.Value, out var name) ? name : null;
}
