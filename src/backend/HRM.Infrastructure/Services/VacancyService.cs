using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.DTOs;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Vacancy lifecycle service (US-REC-001). All queries are tenant-scoped via ITenantContext + the EF
/// global query filter (AC-4 cross-tenant isolation). HTML is sanitized before persist (NFR-4). Audit
/// of create/update/publish/close (FR-7) is provided by the AuditInterceptor (CreatedBy/UpdatedBy
/// stamping on every SaveChanges) plus structured Serilog entries on each lifecycle action.
/// </summary>
public sealed class VacancyService : IVacancyService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IHtmlSanitizer _sanitizer;
    private readonly ILogger<VacancyService> _logger;

    private const int MaxPageSize = 100;

    public VacancyService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IHtmlSanitizer sanitizer,
        ILogger<VacancyService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _sanitizer = sanitizer;
        _logger = logger;
    }

    public async Task<Result<VacancyDto>> CreateAsync(VacancyInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<VacancyDto>.Failure("Tenant context is not resolved.", 400);

        // Validate referenced master data belongs to this tenant (global filter ⇒ a found row is in-tenant).
        var refCheck = await ValidateReferencesAsync(input, cancellationToken);
        if (refCheck is not null)
            return Result<VacancyDto>.Failure(refCheck.Value.error, 400, refCheck.Value.code);

        var reference = await GenerateReferenceNumberAsync(cancellationToken);

        var vacancy = new Vacancy
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            ReferenceNumber = reference,
            Title = input.Title.Trim(),
            Status = VacancyStatus.Draft,
            DepartmentId = input.DepartmentId,
            JobTitleId = input.JobTitleId,
            LocationId = input.LocationId,
            HiringManagerId = input.HiringManagerId,
            EmploymentType = input.EmploymentType,
            Headcount = input.Headcount,
            FilledCount = 0,
            SalaryMin = input.SalaryMin,
            SalaryMax = input.SalaryMax,
            SalaryCurrency = NormalizeCurrency(input.SalaryCurrency),
            Description = _sanitizer.Sanitize(input.Description) ?? string.Empty,
            Qualifications = _sanitizer.Sanitize(input.Qualifications),
            ApplicationDeadline = input.ApplicationDeadline,
            PublishToPublicCareers = input.PublishToPublicCareers,
            IsDeleted = false,
        };

        _dbContext.Vacancies.Add(vacancy);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Vacancy created (Draft). Id={VacancyId}, Ref={Ref}, TenantId={TenantId}, By={User}",
            vacancy.Id, vacancy.ReferenceNumber, _tenantContext.TenantId, _currentUser.Email);

        return Result<VacancyDto>.Success(await ToDtoAsync(vacancy, cancellationToken));
    }

    public async Task<Result<VacancyDto>> UpdateAsync(Guid vacancyId, VacancyInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<VacancyDto>.Failure("Tenant context is not resolved.", 400);

        var vacancy = await _dbContext.Vacancies.FirstOrDefaultAsync(v => v.Id == vacancyId, cancellationToken);
        if (vacancy is null)
            return Result<VacancyDto>.Failure("Vacancy not found.", 404);

        if (vacancy.Status is VacancyStatus.Closed or VacancyStatus.Cancelled)
            return Result<VacancyDto>.Failure(
                "A closed or cancelled vacancy cannot be edited.", 409, "vacancy_terminal");

        var refCheck = await ValidateReferencesAsync(input, cancellationToken);
        if (refCheck is not null)
            return Result<VacancyDto>.Failure(refCheck.Value.error, 400, refCheck.Value.code);

        vacancy.Title = input.Title.Trim();
        vacancy.DepartmentId = input.DepartmentId;
        vacancy.JobTitleId = input.JobTitleId;
        vacancy.LocationId = input.LocationId;
        vacancy.HiringManagerId = input.HiringManagerId;
        vacancy.EmploymentType = input.EmploymentType;
        vacancy.Headcount = input.Headcount;
        vacancy.SalaryMin = input.SalaryMin;
        vacancy.SalaryMax = input.SalaryMax;
        vacancy.SalaryCurrency = NormalizeCurrency(input.SalaryCurrency);
        vacancy.Description = _sanitizer.Sanitize(input.Description) ?? string.Empty;
        vacancy.Qualifications = _sanitizer.Sanitize(input.Qualifications);
        vacancy.ApplicationDeadline = input.ApplicationDeadline;
        vacancy.PublishToPublicCareers = input.PublishToPublicCareers;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Vacancy updated. Id={VacancyId}, Ref={Ref}, TenantId={TenantId}, By={User}",
            vacancy.Id, vacancy.ReferenceNumber, _tenantContext.TenantId, _currentUser.Email);

        return Result<VacancyDto>.Success(await ToDtoAsync(vacancy, cancellationToken));
    }

    public async Task<Result<VacancyDto>> PublishAsync(Guid vacancyId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<VacancyDto>.Failure("Tenant context is not resolved.", 400);

        var vacancy = await _dbContext.Vacancies.FirstOrDefaultAsync(v => v.Id == vacancyId, cancellationToken);
        if (vacancy is null)
            return Result<VacancyDto>.Failure("Vacancy not found.", 404);

        if (vacancy.Status != VacancyStatus.Draft)
            return Result<VacancyDto>.Failure(
                "Only a draft vacancy can be published.", 409, "invalid_transition");

        // BR-2: publish requires title, department, job title, hiring manager, headcount, description.
        var missing = MissingPublishRequirements(vacancy);
        if (missing.Count > 0)
            return Result<VacancyDto>.Failure(
                $"Cannot publish: the following are required — {string.Join(", ", missing)}.",
                400, "publish_requirements");

        vacancy.Status = VacancyStatus.Open;
        vacancy.PublishedAt = DateTime.UtcNow;

        // FR-5: generate a unique-per-tenant SEO slug on first publish (keep an existing slug stable).
        if (string.IsNullOrEmpty(vacancy.Slug))
            vacancy.Slug = await GenerateUniqueSlugAsync(vacancy.Title, vacancy.Id, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Vacancy published (Draft->Open). Id={VacancyId}, Ref={Ref}, Slug={Slug}, TenantId={TenantId}, By={User}",
            vacancy.Id, vacancy.ReferenceNumber, vacancy.Slug, _tenantContext.TenantId, _currentUser.Email);

        return Result<VacancyDto>.Success(await ToDtoAsync(vacancy, cancellationToken));
    }

    public async Task<Result<VacancyDto>> ChangeStatusAsync(Guid vacancyId, VacancyStatus newStatus, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<VacancyDto>.Failure("Tenant context is not resolved.", 400);

        var vacancy = await _dbContext.Vacancies.FirstOrDefaultAsync(v => v.Id == vacancyId, cancellationToken);
        if (vacancy is null)
            return Result<VacancyDto>.Failure("Vacancy not found.", 404);

        var transition = ApplyTransition(vacancy, newStatus);
        if (transition.IsFailure)
            return Result<VacancyDto>.Failure(transition.Error!, transition.StatusCode ?? 400, transition.ErrorCode);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Vacancy status changed to {Status}. Id={VacancyId}, Ref={Ref}, TenantId={TenantId}, By={User}",
            newStatus, vacancy.Id, vacancy.ReferenceNumber, _tenantContext.TenantId, _currentUser.Email);

        return Result<VacancyDto>.Success(await ToDtoAsync(vacancy, cancellationToken));
    }

    public Task<Result<VacancyDto>> CloseAsync(Guid vacancyId, CancellationToken cancellationToken = default)
        => ChangeStatusAsync(vacancyId, VacancyStatus.Closed, cancellationToken);

    public async Task<Result<BulkStatusChangeResult>> BulkChangeStatusAsync(
        IReadOnlyList<Guid> vacancyIds, VacancyStatus newStatus, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<BulkStatusChangeResult>.Failure("Tenant context is not resolved.", 400);

        var distinctIds = vacancyIds.Distinct().ToList();
        var vacancies = await _dbContext.Vacancies
            .Where(v => distinctIds.Contains(v.Id))
            .ToListAsync(cancellationToken);
        var byId = vacancies.ToDictionary(v => v.Id);

        var results = new List<BulkStatusChangeItemResult>(distinctIds.Count);
        foreach (var id in distinctIds)
        {
            if (!byId.TryGetValue(id, out var vacancy))
            {
                results.Add(new BulkStatusChangeItemResult { VacancyId = id, Success = false, Error = "Vacancy not found.", ErrorCode = "not_found" });
                continue;
            }

            var transition = ApplyTransition(vacancy, newStatus);
            results.Add(transition.IsSuccess
                ? new BulkStatusChangeItemResult { VacancyId = id, Success = true }
                : new BulkStatusChangeItemResult { VacancyId = id, Success = false, Error = transition.Error, ErrorCode = transition.ErrorCode });
        }

        // Single atomic SaveChanges for all successful in-memory mutations.
        await _dbContext.SaveChangesAsync(cancellationToken);

        var succeeded = results.Count(r => r.Success);
        _logger.LogInformation(
            "Bulk vacancy status change to {Status}: {Succeeded} succeeded, {Failed} failed. TenantId={TenantId}, By={User}",
            newStatus, succeeded, results.Count - succeeded, _tenantContext.TenantId, _currentUser.Email);

        return Result<BulkStatusChangeResult>.Success(new BulkStatusChangeResult
        {
            SucceededCount = succeeded,
            FailedCount = results.Count - succeeded,
            Results = results,
        });
    }

    public async Task<Result<VacancyDto>> GetByIdAsync(Guid vacancyId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<VacancyDto>.Failure("Tenant context is not resolved.", 400);

        var vacancy = await _dbContext.Vacancies
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == vacancyId, cancellationToken);

        if (vacancy is null)
            return Result<VacancyDto>.Failure("Vacancy not found.", 404);

        return Result<VacancyDto>.Success(await ToDtoAsync(vacancy, cancellationToken));
    }

    public async Task<Result<PagedResult<VacancyListItemDto>>> ListAsync(
        VacancyStatus? status, Guid? departmentId, string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PagedResult<VacancyListItemDto>>.Failure("Tenant context is not resolved.", 400);

        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > MaxPageSize ? Math.Clamp(pageSize, 1, MaxPageSize) : pageSize;

        var query = _dbContext.Vacancies.AsNoTracking();
        if (status.HasValue)
            query = query.Where(v => v.Status == status.Value);
        if (departmentId.HasValue)
            query = query.Where(v => v.DepartmentId == departmentId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Free-text search over title + reference number (case-insensitive). Using ToLower().Contains
            // translates to a SQL LIKE on Postgres and is also honoured by the in-memory test provider
            // (EF.Functions.ILike is Npgsql-only and would throw under InMemory).
            var term = search.Trim().ToLower();
            query = query.Where(v =>
                v.Title.ToLower().Contains(term) || v.ReferenceNumber.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var vacancies = await query
            .OrderByDescending(v => v.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var lookups = await BuildLookupsAsync(vacancies, cancellationToken);

        var items = vacancies.Select(v => new VacancyListItemDto
        {
            Id = v.Id,
            ReferenceNumber = v.ReferenceNumber,
            Title = v.Title,
            Status = v.Status,
            StatusName = v.Status.ToString(),
            DepartmentId = v.DepartmentId,
            DepartmentName = NameOrNull(lookups.Departments, v.DepartmentId),
            JobTitleName = NameOrNull(lookups.JobTitles, v.JobTitleId),
            HiringManagerName = NameOrNull(lookups.Managers, v.HiringManagerId),
            EmploymentType = v.EmploymentType,
            EmploymentTypeName = v.EmploymentType.ToString(),
            Headcount = v.Headcount,
            FilledCount = v.FilledCount,
            ApplicationDeadline = v.ApplicationDeadline,
            CreatedAt = v.CreatedAt,
        }).ToList();

        return Result<PagedResult<VacancyListItemDto>>.Success(new PagedResult<VacancyListItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        });
    }

    // ── Status transition (FR-2) ─────────────────────────────────────

    private static Result ApplyTransition(Vacancy vacancy, VacancyStatus newStatus)
    {
        if (vacancy.Status == newStatus)
            return Result.Failure($"Vacancy is already {newStatus}.", 409, "no_op_transition");

        // Draft -> Open must go through Publish (BR-2 requirements + slug). Block it here.
        if (vacancy.Status == VacancyStatus.Draft && newStatus == VacancyStatus.Open)
            return Result.Failure("Use the publish action to move a draft to Open.", 400, "use_publish");

        if (!VacancyStatusTransitions.IsAllowed(vacancy.Status, newStatus))
        {
            var allowed = VacancyStatusTransitions.AllowedFrom(vacancy.Status);
            var allowedText = allowed.Count == 0 ? "none (terminal)" : string.Join(", ", allowed);
            return Result.Failure(
                $"Cannot change status from {vacancy.Status} to {newStatus}. Allowed: {allowedText}.",
                409, "invalid_transition");
        }

        vacancy.Status = newStatus;
        if (newStatus == VacancyStatus.Closed)
            vacancy.ClosedAt = DateTime.UtcNow;

        // Re-opening from On Hold: stamp PublishedAt if it was never set (defensive).
        if (newStatus == VacancyStatus.Open && vacancy.PublishedAt is null)
            vacancy.PublishedAt = DateTime.UtcNow;

        return Result.Success();
    }

    private static List<string> MissingPublishRequirements(Vacancy v)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(v.Title)) missing.Add("title");
        if (v.DepartmentId is null) missing.Add("department");
        if (v.JobTitleId is null) missing.Add("job title");
        if (v.HiringManagerId is null) missing.Add("hiring manager");
        if (v.Headcount < 1) missing.Add("headcount");
        if (string.IsNullOrWhiteSpace(v.Description)) missing.Add("description");
        return missing;
    }

    // ── Reference number + slug generation ───────────────────────────

    private async Task<string> GenerateReferenceNumberAsync(CancellationToken cancellationToken)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"VAC-{year}-";

        // Highest existing suffix for this tenant+year (global filter scopes to tenant). IgnoreQueryFilters
        // is NOT used: we want the per-tenant max, which the filter already gives us, including soft-deleted
        // rows so a deleted ref number is never reused (collision-safe with the partial unique index).
        var existing = await _dbContext.Vacancies
            .IgnoreQueryFilters()
            .Where(v => v.TenantId == _tenantContext.TenantId && v.ReferenceNumber.StartsWith(prefix))
            .Select(v => v.ReferenceNumber)
            .ToListAsync(cancellationToken);

        var maxSeq = 0;
        foreach (var refNo in existing)
        {
            var tail = refNo[prefix.Length..];
            if (int.TryParse(tail, out var seq) && seq > maxSeq)
                maxSeq = seq;
        }

        return $"{prefix}{(maxSeq + 1):D4}";
    }

    private async Task<string> GenerateUniqueSlugAsync(string title, Guid selfId, CancellationToken cancellationToken)
    {
        var baseSlug = VacancySlugGenerator.Generate(title);

        var taken = await _dbContext.Vacancies
            .Where(v => v.Slug != null && v.Id != selfId)
            .Select(v => v.Slug!)
            .ToListAsync(cancellationToken);
        var takenSet = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);

        if (!takenSet.Contains(baseSlug))
            return baseSlug;

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseSlug}-{i}";
            if (!takenSet.Contains(candidate))
                return candidate;
        }
    }

    // ── Reference validation + DTO projection helpers ────────────────

    private async Task<(string error, string code)?> ValidateReferencesAsync(VacancyInput input, CancellationToken cancellationToken)
    {
        if (input.DepartmentId is { } deptId &&
            !await _dbContext.Departments.AnyAsync(d => d.Id == deptId, cancellationToken))
            return ("The specified department does not exist.", "department_not_found");

        if (input.JobTitleId is { } jtId &&
            !await _dbContext.JobTitles.AnyAsync(j => j.Id == jtId, cancellationToken))
            return ("The specified job title does not exist.", "job_title_not_found");

        if (input.LocationId is { } locId &&
            !await _dbContext.Locations.AnyAsync(l => l.Id == locId, cancellationToken))
            return ("The specified location does not exist.", "location_not_found");

        if (input.HiringManagerId is { } mgrId &&
            !await _dbContext.Employees.AnyAsync(e => e.Id == mgrId, cancellationToken))
            return ("The specified hiring manager does not exist.", "hiring_manager_not_found");

        return null;
    }

    private sealed record Lookups(
        Dictionary<Guid, string> Departments,
        Dictionary<Guid, string> JobTitles,
        Dictionary<Guid, string> Locations,
        Dictionary<Guid, string> Managers);

    private async Task<Lookups> BuildLookupsAsync(IReadOnlyCollection<Vacancy> vacancies, CancellationToken cancellationToken)
    {
        var deptIds = vacancies.Where(v => v.DepartmentId.HasValue).Select(v => v.DepartmentId!.Value).Distinct().ToList();
        var jtIds = vacancies.Where(v => v.JobTitleId.HasValue).Select(v => v.JobTitleId!.Value).Distinct().ToList();
        var locIds = vacancies.Where(v => v.LocationId.HasValue).Select(v => v.LocationId!.Value).Distinct().ToList();
        var mgrIds = vacancies.Where(v => v.HiringManagerId.HasValue).Select(v => v.HiringManagerId!.Value).Distinct().ToList();

        var departments = deptIds.Count == 0 ? new() : await _dbContext.Departments
            .Where(d => deptIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);
        var jobTitles = jtIds.Count == 0 ? new() : await _dbContext.JobTitles
            .Where(j => jtIds.Contains(j.Id)).ToDictionaryAsync(j => j.Id, j => j.TitleName, cancellationToken);
        var locations = locIds.Count == 0 ? new() : await _dbContext.Locations
            .Where(l => locIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);
        var managers = mgrIds.Count == 0 ? new() : await _dbContext.Employees
            .Where(e => mgrIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => (e.FirstName + " " + e.LastName).Trim(), cancellationToken);

        return new Lookups(departments, jobTitles, locations, managers);
    }

    private async Task<VacancyDto> ToDtoAsync(Vacancy v, CancellationToken cancellationToken)
    {
        var lookups = await BuildLookupsAsync([v], cancellationToken);
        return new VacancyDto
        {
            Id = v.Id,
            ReferenceNumber = v.ReferenceNumber,
            Title = v.Title,
            Status = v.Status,
            StatusName = v.Status.ToString(),
            DepartmentId = v.DepartmentId,
            DepartmentName = NameOrNull(lookups.Departments, v.DepartmentId),
            JobTitleId = v.JobTitleId,
            JobTitleName = NameOrNull(lookups.JobTitles, v.JobTitleId),
            LocationId = v.LocationId,
            LocationName = NameOrNull(lookups.Locations, v.LocationId),
            HiringManagerId = v.HiringManagerId,
            HiringManagerName = NameOrNull(lookups.Managers, v.HiringManagerId),
            EmploymentType = v.EmploymentType,
            EmploymentTypeName = v.EmploymentType.ToString(),
            Headcount = v.Headcount,
            FilledCount = v.FilledCount,
            SalaryMin = v.SalaryMin,
            SalaryMax = v.SalaryMax,
            SalaryCurrency = v.SalaryCurrency,
            Description = v.Description,
            Qualifications = v.Qualifications,
            ApplicationDeadline = v.ApplicationDeadline,
            Slug = v.Slug,
            PublishToPublicCareers = v.PublishToPublicCareers,
            PublishedAt = v.PublishedAt,
            ClosedAt = v.ClosedAt,
            CreatedAt = v.CreatedAt,
            UpdatedAt = v.UpdatedAt,
        };
    }

    private static string? NameOrNull(Dictionary<Guid, string> map, Guid? id)
        => id.HasValue && map.TryGetValue(id.Value, out var name) ? name : null;

    private static string? NormalizeCurrency(string? currency)
        => string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant();
}
