using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.SalaryGrades.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Salary grade CRUD service (Payroll domain, ISSUE-021).
/// All queries are tenant-scoped via ITenantContext and EF global query filters.
/// </summary>
public sealed class SalaryGradeService : ISalaryGradeService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<SalaryGradeService> _logger;

    public SalaryGradeService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<SalaryGradeService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new salary grade. Validates code uniqueness (case-insensitive, 409) and the
    /// Min &lt;= Mid &lt;= Max amount ordering (422).
    /// </summary>
    public async Task<Result<SalaryGradeDto>> CreateAsync(
        CreateSalaryGradeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<SalaryGradeDto>.Failure("Tenant context is not resolved.", 400);

        var code = request.Code.Trim();

        var rangeError = ValidateAmountRange(request.MinAmount, request.MidAmount, request.MaxAmount);
        if (rangeError is not null)
            return Result<SalaryGradeDto>.Failure(rangeError, 422, "invalid_amount_range");

        // Code uniqueness within tenant (trimmed + case-insensitive).
        var normalizedCode = code.ToLowerInvariant();
        var codeExists = await _dbContext.SalaryGrades
            .AnyAsync(g => g.Code.ToLower() == normalizedCode, cancellationToken);

        if (codeExists)
            return Result<SalaryGradeDto>.Failure(
                "A salary grade with this code already exists.", 409, "duplicate_code");

        var salaryGrade = new SalaryGrade
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            Code = code,
            Name = request.Name.Trim(),
            MinAmount = request.MinAmount,
            MidAmount = request.MidAmount,
            MaxAmount = request.MaxAmount,
            Currency = request.Currency.Trim().ToUpperInvariant(),
            Description = request.Description?.Trim(),
            IsActive = true,
            IsDeleted = false,
        };

        _dbContext.SalaryGrades.Add(salaryGrade);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Salary grade created. Id={SalaryGradeId}, Code={Code}, TenantId={TenantId}, By={User}",
            salaryGrade.Id, salaryGrade.Code, _tenantContext.TenantId, _currentUser.Email);

        return Result<SalaryGradeDto>.Success(ToDto(salaryGrade));
    }

    /// <summary>
    /// Updates a salary grade. Validates code uniqueness excluding self (409) and the amount ordering (422).
    /// </summary>
    public async Task<Result<SalaryGradeDto>> UpdateAsync(
        Guid salaryGradeId,
        UpdateSalaryGradeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<SalaryGradeDto>.Failure("Tenant context is not resolved.", 400);

        var salaryGrade = await _dbContext.SalaryGrades
            .FirstOrDefaultAsync(g => g.Id == salaryGradeId, cancellationToken);

        if (salaryGrade is null)
            return Result<SalaryGradeDto>.Failure("Salary grade not found.", 404);

        var code = request.Code.Trim();

        var rangeError = ValidateAmountRange(request.MinAmount, request.MidAmount, request.MaxAmount);
        if (rangeError is not null)
            return Result<SalaryGradeDto>.Failure(rangeError, 422, "invalid_amount_range");

        // Code uniqueness excluding self (trimmed + case-insensitive).
        var normalizedCode = code.ToLowerInvariant();
        var codeExists = await _dbContext.SalaryGrades
            .AnyAsync(g => g.Code.ToLower() == normalizedCode && g.Id != salaryGradeId, cancellationToken);

        if (codeExists)
            return Result<SalaryGradeDto>.Failure(
                "A salary grade with this code already exists.", 409, "duplicate_code");

        salaryGrade.Code = code;
        salaryGrade.Name = request.Name.Trim();
        salaryGrade.MinAmount = request.MinAmount;
        salaryGrade.MidAmount = request.MidAmount;
        salaryGrade.MaxAmount = request.MaxAmount;
        salaryGrade.Currency = request.Currency.Trim().ToUpperInvariant();
        salaryGrade.Description = request.Description?.Trim();

        // B5: the Active flag is now part of the update. Before this it was absent from the request DTO, so
        // the edit form's toggle changed nothing on save — and because DELETE was the only writer of the
        // flag, a deactivated grade could never be brought back.
        // null = leave unchanged; see UpdateSalaryGradeRequest.IsActive for why absent must not mean true.
        var wasActive = salaryGrade.IsActive;
        if (request.IsActive is bool active)
        {
            salaryGrade.IsActive = active;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (wasActive != salaryGrade.IsActive)
        {
            _logger.LogInformation(
                "Salary grade {Transition}. Id={SalaryGradeId}, Code={Code}, TenantId={TenantId}, By={User}",
                salaryGrade.IsActive ? "reactivated" : "deactivated",
                salaryGrade.Id, salaryGrade.Code, _tenantContext.TenantId, _currentUser.Email);
        }

        _logger.LogInformation(
            "Salary grade updated. Id={SalaryGradeId}, Code={Code}, TenantId={TenantId}, By={User}",
            salaryGrade.Id, salaryGrade.Code, _tenantContext.TenantId, _currentUser.Email);

        return Result<SalaryGradeDto>.Success(
            await ToDtoAsync(salaryGrade, cancellationToken));
    }

    /// <summary>
    /// Deactivates (soft-delete/deactivate) a salary grade. Hidden from the active list but retained.
    /// </summary>
    public async Task<Result> DeactivateAsync(
        Guid salaryGradeId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var salaryGrade = await _dbContext.SalaryGrades
            .FirstOrDefaultAsync(g => g.Id == salaryGradeId, cancellationToken);

        if (salaryGrade is null)
            return Result.Failure("Salary grade not found.", 404);

        if (!salaryGrade.IsActive)
            return Result.Failure("Salary grade is already deactivated.", 400);

        salaryGrade.IsActive = false;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Salary grade deactivated. Id={SalaryGradeId}, Code={Code}, TenantId={TenantId}, By={User}",
            salaryGrade.Id, salaryGrade.Code, _tenantContext.TenantId, _currentUser.Email);

        return Result.Success();
    }

    public async Task<Result<SalaryGradeDto>> GetByIdAsync(
        Guid salaryGradeId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<SalaryGradeDto>.Failure("Tenant context is not resolved.", 400);

        var salaryGrade = await _dbContext.SalaryGrades
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == salaryGradeId, cancellationToken);

        if (salaryGrade is null)
            return Result<SalaryGradeDto>.Failure("Salary grade not found.", 404);

        return Result<SalaryGradeDto>.Success(
            await ToDtoAsync(salaryGrade, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<SalaryGradeDto>>> GetAllAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<SalaryGradeDto>>.Failure("Tenant context is not resolved.", 400);

        var query = _dbContext.SalaryGrades.AsNoTracking();

        if (!includeInactive)
            query = query.Where(g => g.IsActive);

        var salaryGrades = await query
            .OrderBy(g => g.Code)
            .ThenBy(g => g.Name)
            .ToListAsync(cancellationToken);

        // B5: one GROUPED count for the whole page rather than a count per row — the field has to be
        // truthful on the list too (a hard-coded 0 would read as "nothing references this grade"), but not
        // at the cost of an N+1.
        var gradeIds = salaryGrades.Select(g => g.Id).ToList();
        var referenceCounts = await _dbContext.JobTitles
            .AsNoTracking()
            .Where(t => t.GradeId != null && gradeIds.Contains(t.GradeId.Value))
            .GroupBy(t => t.GradeId!.Value)
            .Select(grp => new { GradeId = grp.Key, Count = grp.Count() })
            .ToDictionaryAsync(x => x.GradeId, x => x.Count, cancellationToken);

        var dtos = salaryGrades
            .Select(g => ToDto(g) with
            {
                ReferencingJobTitleCount = referenceCounts.GetValueOrDefault(g.Id),
            })
            .ToList();
        return Result<IReadOnlyList<SalaryGradeDto>>.Success(dtos);
    }

    // ── Private helpers ──────────────────────────────────────────────

    /// <summary>
    /// Validates the Min &lt;= Mid (if provided) &lt;= Max ordering. Returns an error message, or null if valid.
    /// </summary>
    private static string? ValidateAmountRange(decimal minAmount, decimal? midAmount, decimal maxAmount)
    {
        if (minAmount > maxAmount)
            return "Minimum amount cannot be greater than maximum amount.";

        if (midAmount.HasValue && (midAmount.Value < minAmount || midAmount.Value > maxAmount))
            return "Midpoint amount must be between the minimum and maximum amounts.";

        return null;
    }

    /// <summary>
    /// Projects a grade plus the number of job titles pointing at it (B5), so the UI can warn before a
    /// deactivation that would break those titles' next save.
    /// </summary>
    private async Task<SalaryGradeDto> ToDtoAsync(SalaryGrade g, CancellationToken cancellationToken) =>
        ToDto(g) with
        {
            ReferencingJobTitleCount = await _dbContext.JobTitles
                .AsNoTracking()
                .CountAsync(t => t.GradeId == g.Id, cancellationToken),
        };

    private static SalaryGradeDto ToDto(SalaryGrade g) => new()
    {
        Id = g.Id,
        Code = g.Code,
        Name = g.Name,
        MinAmount = g.MinAmount,
        MidAmount = g.MidAmount,
        MaxAmount = g.MaxAmount,
        Currency = g.Currency,
        Description = g.Description,
        IsActive = g.IsActive,
        CreatedAt = g.CreatedAt,
        UpdatedAt = g.UpdatedAt,
    };
}
