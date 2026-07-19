using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.JobTitles.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Job title CRUD service (US-CHR-005).
/// All queries are tenant-scoped via ITenantContext and EF global query filters.
/// </summary>
public sealed class JobTitleService : IJobTitleService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<JobTitleService> _logger;

    public JobTitleService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<JobTitleService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new job title (AC-2). Validates title_name uniqueness (AC-3, FR-2, BR-1).
    /// </summary>
    public async Task<Result<JobTitleDto>> CreateAsync(
        string titleName, string? description, Guid? gradeId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<JobTitleDto>.Failure("Tenant context is not resolved.", 400);

        // BUG-016: trim once and store the trimmed value; the duplicate pre-check is case-INsensitive
        // (LOWER(title_name) = LOWER(@p)) so "Engineer" and "engineer" cannot both persist.
        titleName = titleName.Trim();

        // FR-2 / BR-1: title_name uniqueness within tenant (trimmed + case-insensitive)
        var nameExists = await _dbContext.JobTitles
            .AnyAsync(j => j.TitleName.ToLower() == titleName.ToLower(), cancellationToken);

        if (nameExists)
            return Result<JobTitleDto>.Failure("A job title with this name already exists.", 400);

        // ISSUE-021: when a grade is linked, it must resolve to an active, in-tenant SalaryGrade.
        var gradeError = await ValidateGradeAsync(gradeId, cancellationToken);
        if (gradeError is not null)
            return Result<JobTitleDto>.Failure(gradeError, 422, "invalid_grade");

        var jobTitle = new JobTitle
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            TitleName = titleName,
            Description = description,
            GradeId = gradeId,
            IsActive = true,
            IsDeleted = false,
        };

        _dbContext.JobTitles.Add(jobTitle);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Job title created. Id={JobTitleId}, TitleName={TitleName}, TenantId={TenantId}, By={User}",
            jobTitle.Id, jobTitle.TitleName, _tenantContext.TenantId, _currentUser.Email);

        return Result<JobTitleDto>.Success(ToDto(jobTitle));
    }

    /// <summary>
    /// Updates a job title (AC-2, AC-3). Validates title_name uniqueness excluding self.
    /// </summary>
    public async Task<Result<JobTitleDto>> UpdateAsync(
        Guid jobTitleId, string titleName, string? description, Guid? gradeId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<JobTitleDto>.Failure("Tenant context is not resolved.", 400);

        var jobTitle = await _dbContext.JobTitles
            .FirstOrDefaultAsync(j => j.Id == jobTitleId, cancellationToken);

        if (jobTitle is null)
            return Result<JobTitleDto>.Failure("Job title not found.", 404);

        // BUG-016: trim once and store the trimmed value; the duplicate pre-check is case-INsensitive.
        titleName = titleName.Trim();

        // Title name uniqueness (excluding self) — trimmed + case-insensitive
        var nameExists = await _dbContext.JobTitles
            .AnyAsync(j => j.TitleName.ToLower() == titleName.ToLower() && j.Id != jobTitleId, cancellationToken);

        if (nameExists)
            return Result<JobTitleDto>.Failure("A job title with this name already exists.", 400);

        // ISSUE-021: when a grade is linked, it must resolve to an active, in-tenant SalaryGrade.
        var gradeError = await ValidateGradeAsync(gradeId, cancellationToken);
        if (gradeError is not null)
            return Result<JobTitleDto>.Failure(gradeError, 422, "invalid_grade");

        jobTitle.TitleName = titleName;
        jobTitle.Description = description;
        jobTitle.GradeId = gradeId;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Job title updated. Id={JobTitleId}, TitleName={TitleName}, TenantId={TenantId}, By={User}",
            jobTitle.Id, jobTitle.TitleName, _tenantContext.TenantId, _currentUser.Email);

        return Result<JobTitleDto>.Success(ToDto(jobTitle));
    }

    /// <summary>
    /// Deactivates (soft-deletes) a job title (AC-5, FR-5, FR-7).
    /// NOTE: Employee assignment check (AC-5 / FR-7) is deferred to US-CHR-001.
    /// </summary>
    public async Task<Result> DeactivateAsync(
        Guid jobTitleId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var jobTitle = await _dbContext.JobTitles
            .FirstOrDefaultAsync(j => j.Id == jobTitleId, cancellationToken);

        if (jobTitle is null)
            return Result.Failure("Job title not found.", 404);

        if (!jobTitle.IsActive)
            return Result.Failure("Job title is already deactivated.", 400);

        // AC-5 / FR-7: Cannot deactivate if there are active employees assigned (US-CHR-001 wiring).
        var activeEmployeeCount = await _dbContext.Employees
            .CountAsync(e => e.JobTitleId == jobTitleId && e.IsActive, cancellationToken);

        if (activeEmployeeCount > 0)
            return Result.Failure(
                $"This job title is assigned to {activeEmployeeCount} active employee(s). Reassign them before deactivating.",
                400);

        jobTitle.IsActive = false;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Job title deactivated. Id={JobTitleId}, TitleName={TitleName}, TenantId={TenantId}, By={User}",
            jobTitle.Id, jobTitle.TitleName, _tenantContext.TenantId, _currentUser.Email);

        return Result.Success();
    }

    public async Task<Result<JobTitleDto>> GetByIdAsync(
        Guid jobTitleId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<JobTitleDto>.Failure("Tenant context is not resolved.", 400);

        var jobTitle = await _dbContext.JobTitles
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobTitleId, cancellationToken);

        if (jobTitle is null)
            return Result<JobTitleDto>.Failure("Job title not found.", 404);

        // US-CHR-001: Real employee count
        var employeeCount = await _dbContext.Employees
            .CountAsync(e => e.JobTitleId == jobTitleId && e.IsActive, cancellationToken);

        // ISSUE-021: resolve the linked grade's name for display (by id — a since-deactivated grade still labels).
        string? gradeName = null;
        if (jobTitle.GradeId.HasValue)
            gradeName = await _dbContext.SalaryGrades.AsNoTracking()
                .Where(g => g.Id == jobTitle.GradeId.Value)
                .Select(g => g.Name)
                .FirstOrDefaultAsync(cancellationToken);

        return Result<JobTitleDto>.Success(ToDto(jobTitle, employeeCount, gradeName));
    }

    public async Task<Result<IReadOnlyList<JobTitleDto>>> GetAllAsync(
        bool? activeOnly = null,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<JobTitleDto>>.Failure("Tenant context is not resolved.", 400);

        var query = _dbContext.JobTitles.AsNoTracking();

        if (activeOnly == true)
            query = query.Where(j => j.IsActive);

        var jobTitles = await query
            .OrderBy(j => j.TitleName)
            .ToListAsync(cancellationToken);

        // US-CHR-001: Batch-load employee counts for all job titles in one query
        var jobTitleIds = jobTitles.Select(j => j.Id).ToList();
        var employeeCounts = await _dbContext.Employees
            .Where(e => jobTitleIds.Contains(e.JobTitleId) && e.IsActive)
            .GroupBy(e => e.JobTitleId)
            .Select(g => new { JobTitleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.JobTitleId, g => g.Count, cancellationToken);

        // ISSUE-021: batch-load linked grade names in one query (no N+1), same pattern as employee counts.
        var gradeIds = jobTitles.Where(j => j.GradeId.HasValue).Select(j => j.GradeId!.Value).Distinct().ToList();
        var gradeNames = gradeIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.SalaryGrades.AsNoTracking()
                .Where(g => gradeIds.Contains(g.Id))
                .Select(g => new { g.Id, g.Name })
                .ToDictionaryAsync(g => g.Id, g => g.Name, cancellationToken);

        var dtos = jobTitles.Select(j =>
            ToDto(j, employeeCounts.GetValueOrDefault(j.Id, 0),
                j.GradeId.HasValue ? gradeNames.GetValueOrDefault(j.GradeId.Value) : null)).ToList();
        return Result<IReadOnlyList<JobTitleDto>>.Success(dtos);
    }

    // ── Private helpers ──────────────────────────────────────────────

    /// <summary>
    /// ISSUE-021: validates the optional GradeId link. A null grade is always allowed (a job title can
    /// exist without a linked grade). When a grade is provided, it must resolve to an ACTIVE, in-tenant
    /// SalaryGrade (tenant scoping via the EF global query filter). Returns an error message, or null if
    /// the link is valid. No hard DB FK constraint is used — existing rows may hold arbitrary free-form
    /// GUIDs — so the check is enforced service-side on write only.
    /// </summary>
    private async Task<string?> ValidateGradeAsync(Guid? gradeId, CancellationToken cancellationToken)
    {
        if (!gradeId.HasValue)
            return null;

        var gradeExists = await _dbContext.SalaryGrades
            .AnyAsync(g => g.Id == gradeId.Value && g.IsActive, cancellationToken);

        return gradeExists
            ? null
            : "The selected salary grade does not exist or is not active.";
    }

    private JobTitleDto ToDto(JobTitle j, int? employeeCount = null, string? gradeName = null) => new()
    {
        Id = j.Id,
        TitleName = j.TitleName,
        Description = j.Description,
        GradeId = j.GradeId,
        GradeName = gradeName,
        EmployeeCount = employeeCount ?? 0,
        IsActive = j.IsActive,
        CreatedAt = j.CreatedAt,
        UpdatedAt = j.UpdatedAt,
    };
}
