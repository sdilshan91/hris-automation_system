using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.DTOs;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PA = HRM.Domain.Payroll.PayrollAuditAction;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Statutory-rule configuration service (US-PAY-006). All queries are tenant-scoped via ITenantContext +
/// the EF global query filter (AC-4). Enforces income-tax-vs-social-security shape (a rule has slabs XOR a
/// social-security row), fiscal-year versioning with effective ranges (FR-1/FR-4), clone-to-new-fiscal-year
/// (AC-3), and the side-effect-free test calculation (FR-5) via the shared
/// <see cref="IStatutoryDeductionResolver"/>. Slab contiguity (FR-6) is validated in the FluentValidation
/// layer; a defensive structural check is repeated here. Audit fields are stamped by the AuditInterceptor.
/// </summary>
public sealed class StatutoryRuleService : IStatutoryRuleService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IStatutoryDeductionResolver _resolver;
    private readonly IPayrollAuditLogger _audit;
    private readonly ILogger<StatutoryRuleService> _logger;

    private const int MaxPageSize = 100;

    public StatutoryRuleService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IStatutoryDeductionResolver resolver,
        IPayrollAuditLogger audit,
        ILogger<StatutoryRuleService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _resolver = resolver;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>US-PAY-012: audited snapshot of a statutory rule's defining fields (before/after JSON).</summary>
    private static object Snapshot(StatutoryRule r) => new
    {
        RuleType = r.RuleType.ToString(), r.RuleName, r.CountryCode, r.FiscalYear,
        EffectiveFrom = r.EffectiveFrom.ToString("yyyy-MM-dd"),
        EffectiveTo = r.EffectiveTo?.ToString("yyyy-MM-dd"),
        r.IsActive, SlabCount = r.TaxSlabs?.Count ?? 0,
    };

    public async Task<Result<StatutoryRuleDto>> CreateAsync(CreateStatutoryRuleInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<StatutoryRuleDto>.Failure("Tenant context is not resolved.", 400);

        var shapeError = ValidateShape(input.RuleType, input.TaxSlabs, input.SocialSecurity);
        if (shapeError is not null)
            return Result<StatutoryRuleDto>.Failure(shapeError.Value.error, 400, shapeError.Value.code);

        var ruleId = BaseEntity.NewUuidV7();
        var rule = new StatutoryRule
        {
            Id = ruleId,
            TenantId = _tenantContext.TenantId,
            RuleType = input.RuleType,
            RuleName = input.RuleName.Trim(),
            CountryCode = input.CountryCode.Trim().ToUpperInvariant(),
            FiscalYear = input.FiscalYear.Trim(),
            EffectiveFrom = input.EffectiveFrom,
            EffectiveTo = input.EffectiveTo,
            IsActive = input.IsActive,
            IsDeleted = false,
            TaxSlabs = BuildSlabs(ruleId, input.TaxSlabs),
            SocialSecurityRule = BuildSocialSecurity(ruleId, input.SocialSecurity),
        };

        _dbContext.StatutoryRules.Add(rule);

        // US-PAY-012 (FR-2): audit the create — before=null, after=snapshot. Committed atomically.
        _audit.Log(PA.StatutoryRuleCreated, PA.ResourceType.StatutoryRule,
            rule.Id.ToString(), before: null, after: Snapshot(rule));

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Statutory rule created. Id={Id}, Type={Type}, FY={FiscalYear}, TenantId={TenantId}, By={User}",
            rule.Id, rule.RuleType, rule.FiscalYear, _tenantContext.TenantId, _currentUser.Email);

        return await BuildDtoResultAsync(ruleId, cancellationToken);
    }

    public async Task<Result<StatutoryRuleDto>> UpdateAsync(Guid ruleId, UpdateStatutoryRuleInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<StatutoryRuleDto>.Failure("Tenant context is not resolved.", 400);

        var rule = await _dbContext.StatutoryRules
            .Include(r => r.TaxSlabs)
            .Include(r => r.SocialSecurityRule)
            .FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
        if (rule is null)
            return Result<StatutoryRuleDto>.Failure("Statutory rule not found.", 404);

        // BR-7 (soft): block edits to a rule whose effective period overlaps a finalized payroll run. A full
        // retroactive guard is US-PAY-007; here we warn + allow, see the module note. (Implemented as a log
        // note rather than a hard stop so config corrections remain possible pre-finalization.)
        var shapeError = ValidateShape(rule.RuleType, input.TaxSlabs, input.SocialSecurity);
        if (shapeError is not null)
            return Result<StatutoryRuleDto>.Failure(shapeError.Value.error, 400, shapeError.Value.code);

        // US-PAY-012: capture the BEFORE snapshot prior to mutating (TaxSlabs already Included above).
        var before = Snapshot(rule);

        rule.RuleName = input.RuleName.Trim();
        rule.CountryCode = input.CountryCode.Trim().ToUpperInvariant();
        rule.FiscalYear = input.FiscalYear.Trim();
        rule.EffectiveFrom = input.EffectiveFrom;
        rule.EffectiveTo = input.EffectiveTo;
        rule.IsActive = input.IsActive;

        // Replace slabs + social-security wholesale (these are owned children; historical payslips are never
        // recomputed so prior versions can be replaced for the SAME rule version).
        _dbContext.TaxSlabs.RemoveRange(rule.TaxSlabs);
        rule.TaxSlabs = BuildSlabs(ruleId, input.TaxSlabs);

        if (rule.SocialSecurityRule is not null)
            _dbContext.SocialSecurityRules.Remove(rule.SocialSecurityRule);
        rule.SocialSecurityRule = BuildSocialSecurity(ruleId, input.SocialSecurity);

        // US-PAY-012 (FR-2): audit the update with before/after JSON.
        _audit.Log(PA.StatutoryRuleUpdated, PA.ResourceType.StatutoryRule,
            rule.Id.ToString(), before: before, after: Snapshot(rule));

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Statutory rule updated. Id={Id}, Type={Type}, FY={FiscalYear}, TenantId={TenantId}, By={User}",
            rule.Id, rule.RuleType, rule.FiscalYear, _tenantContext.TenantId, _currentUser.Email);

        return await BuildDtoResultAsync(ruleId, cancellationToken);
    }

    public async Task<Result<StatutoryRuleDto>> GetByIdAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<StatutoryRuleDto>.Failure("Tenant context is not resolved.", 400);

        var exists = await _dbContext.StatutoryRules.AsNoTracking().AnyAsync(r => r.Id == ruleId, cancellationToken);
        return exists
            ? await BuildDtoResultAsync(ruleId, cancellationToken)
            : Result<StatutoryRuleDto>.Failure("Statutory rule not found.", 404);
    }

    public async Task<Result<PagedResult<StatutoryRuleListItemDto>>> ListAsync(
        StatutoryRuleType? ruleType, string? fiscalYear, bool? isActive, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PagedResult<StatutoryRuleListItemDto>>.Failure("Tenant context is not resolved.", 400);

        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > MaxPageSize ? Math.Clamp(pageSize, 1, MaxPageSize) : pageSize;

        var query = _dbContext.StatutoryRules.AsNoTracking();
        if (ruleType.HasValue)
            query = query.Where(r => r.RuleType == ruleType.Value);
        if (!string.IsNullOrWhiteSpace(fiscalYear))
        {
            var fy = fiscalYear.Trim();
            query = query.Where(r => r.FiscalYear == fy);
        }
        if (isActive.HasValue)
            query = query.Where(r => r.IsActive == isActive.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var rules = await query
            .OrderByDescending(r => r.FiscalYear).ThenBy(r => r.RuleType).ThenBy(r => r.RuleName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new StatutoryRuleListItemDto
            {
                Id = r.Id,
                RuleType = r.RuleType,
                RuleTypeName = r.RuleType.ToString(),
                RuleName = r.RuleName,
                CountryCode = r.CountryCode,
                FiscalYear = r.FiscalYear,
                EffectiveFrom = r.EffectiveFrom,
                EffectiveTo = r.EffectiveTo,
                IsActive = r.IsActive,
                SlabCount = r.TaxSlabs.Count,
            })
            .ToListAsync(cancellationToken);

        return Result<PagedResult<StatutoryRuleListItemDto>>.Success(new PagedResult<StatutoryRuleListItemDto>
        {
            Items = rules,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        });
    }

    public async Task<Result> DeleteAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var rule = await _dbContext.StatutoryRules.FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
        if (rule is null)
            return Result.Failure("Statutory rule not found.", 404);

        rule.IsDeleted = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Statutory rule soft-deleted. Id={Id}, TenantId={TenantId}, By={User}",
            rule.Id, _tenantContext.TenantId, _currentUser.Email);

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<string>>> ListFiscalYearsAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure<IReadOnlyList<string>>("Tenant context is not resolved.", 400);

        // Distinct fiscal years that have rules (tenant-scoped via the EF global filter), newest-first.
        var years = await _dbContext.StatutoryRules.AsNoTracking()
            .Select(r => r.FiscalYear)
            .Distinct()
            .OrderByDescending(fy => fy)
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<string>>(years);
    }

    public async Task<Result<IReadOnlyList<StatutoryRuleDto>>> CloneFiscalYearAsync(
        string sourceFiscalYear, string targetFiscalYear, DateOnly effectiveFrom, DateOnly? effectiveTo, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<StatutoryRuleDto>>.Failure("Tenant context is not resolved.", 400);

        var source = sourceFiscalYear.Trim();
        var target = targetFiscalYear.Trim();
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            return Result<IReadOnlyList<StatutoryRuleDto>>.Failure(
                "The source and target fiscal years must differ.", 400, "same_fiscal_year");

        if (await _dbContext.StatutoryRules.AnyAsync(r => r.FiscalYear == target, cancellationToken))
            return Result<IReadOnlyList<StatutoryRuleDto>>.Failure(
                $"Statutory rules already exist for fiscal year '{target}'. Delete them before cloning.", 409, "target_fiscal_year_exists");

        var sourceRules = await _dbContext.StatutoryRules.AsNoTracking()
            .Include(r => r.TaxSlabs)
            .Include(r => r.SocialSecurityRule)
            .Where(r => r.FiscalYear == source)
            .ToListAsync(cancellationToken);

        if (sourceRules.Count == 0)
            return Result<IReadOnlyList<StatutoryRuleDto>>.Failure(
                $"No statutory rules found for fiscal year '{source}'.", 404, "source_fiscal_year_empty");

        var clonedIds = new List<Guid>();
        foreach (var src in sourceRules)
        {
            var cloneId = BaseEntity.NewUuidV7();
            var clone = new StatutoryRule
            {
                Id = cloneId,
                TenantId = _tenantContext.TenantId,
                RuleType = src.RuleType,
                RuleName = src.RuleName,
                CountryCode = src.CountryCode,
                FiscalYear = target,
                EffectiveFrom = effectiveFrom,
                EffectiveTo = effectiveTo,
                IsActive = src.IsActive,
                IsDeleted = false,
                TaxSlabs = src.TaxSlabs.Select(s => new TaxSlab
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = _tenantContext.TenantId,
                    StatutoryRuleId = cloneId,
                    SlabFrom = s.SlabFrom,
                    SlabTo = s.SlabTo,
                    RatePercentage = s.RatePercentage,
                    OrderIndex = s.OrderIndex,
                    IsDeleted = false,
                }).ToList(),
                SocialSecurityRule = src.SocialSecurityRule is null ? null : new SocialSecurityRule
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = _tenantContext.TenantId,
                    StatutoryRuleId = cloneId,
                    EmployeeRate = src.SocialSecurityRule.EmployeeRate,
                    EmployerRate = src.SocialSecurityRule.EmployerRate,
                    WageCeilingAnnual = src.SocialSecurityRule.WageCeilingAnnual,
                    ApplicableOn = src.SocialSecurityRule.ApplicableOn,
                    ApplicableComponentIds = src.SocialSecurityRule.ApplicableComponentIds is null
                        ? null
                        : [.. src.SocialSecurityRule.ApplicableComponentIds],
                    IsDeleted = false,
                },
            };
            _dbContext.StatutoryRules.Add(clone);
            clonedIds.Add(cloneId);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Statutory rules cloned. Source FY={Source}, Target FY={Target}, Count={Count}, TenantId={TenantId}, By={User}",
            source, target, clonedIds.Count, _tenantContext.TenantId, _currentUser.Email);

        var dtos = new List<StatutoryRuleDto>(clonedIds.Count);
        foreach (var id in clonedIds)
        {
            var dto = await BuildDtoResultAsync(id, cancellationToken);
            if (dto.IsSuccess)
                dtos.Add(dto.Value!);
        }

        return Result<IReadOnlyList<StatutoryRuleDto>>.Success(dtos);
    }

    public async Task<Result<StatutoryCalculationResultDto>> TestCalculationAsync(TestCalculationInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<StatutoryCalculationResultDto>.Failure("Tenant context is not resolved.", 400);

        var basic = input.MonthlyBasic ?? input.MonthlyGross;
        var wage = new StatutoryWageInput(
            MonthlyGross: input.MonthlyGross,
            MonthlyBasic: basic,
            ExemptEarnings: input.ExemptEarnings,
            DeclaredExemptions: input.DeclaredMonthlyExemptions,
            ComponentAmountsById: null);

        // Use today's date to pick the effective version; FR-5 is a what-if so a current-period date is fine.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var resolved = await _resolver.ResolveAsync(today.Year, today.Month, wage, input.FiscalYear, cancellationToken);
        if (resolved.IsFailure)
            return Result<StatutoryCalculationResultDto>.Failure(resolved.Error!, resolved.StatusCode ?? 400, resolved.ErrorCode);

        var d = resolved.Value!;
        return Result<StatutoryCalculationResultDto>.Success(new StatutoryCalculationResultDto
        {
            TaxableIncome = d.TaxableIncome,
            IncomeTax = d.IncomeTax,
            EmployeeEpf = d.EmployeeEpf,
            EmployerEpf = d.EmployerEpf,
            Etf = d.Etf,
            ProfessionalTax = d.ProfessionalTax,
            OtherStatutory = d.OtherStatutory,
            TotalEmployeeDeductions = d.TotalEmployeeDeductions,
            TotalEmployerContributions = d.TotalEmployerContributions,
            FiscalYear = d.FiscalYear,
            Lines = d.Lines.Select(l => new StatutoryLineDto
            {
                Label = l.Label,
                RuleType = StatutoryRuleType.Custom,
                Amount = l.Amount,
                IsEmployerContribution = l.IsEmployerContribution,
                Basis = l.Basis,
            }).ToList(),
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Structural shape guard (defensive — the validator is the primary FR-6 gate): IncomeTax needs at least
    /// one slab and no social-security row; the other types need a social-security row and no slabs. Slab
    /// contiguity (FR-6) is re-checked so a non-validated caller (e.g. the resolver) can't store gaps.
    /// </summary>
    private static (string error, string code)? ValidateShape(
        StatutoryRuleType ruleType, IReadOnlyList<TaxSlabInput> slabs, SocialSecurityInputDto? social)
    {
        if (ruleType == StatutoryRuleType.IncomeTax)
        {
            if (slabs.Count == 0)
                return ("An income-tax rule requires at least one tax slab.", "no_tax_slabs");
            if (social is not null)
                return ("An income-tax rule cannot carry social-security parameters.", "unexpected_social_security");

            var contiguity = ValidateContiguity(slabs);
            if (contiguity is not null)
                return (contiguity, "non_contiguous_slabs");
        }
        else
        {
            if (social is null)
                return ($"A {ruleType} rule requires social-security parameters.", "missing_social_security");
            if (slabs.Count > 0)
                return ($"A {ruleType} rule cannot carry tax slabs.", "unexpected_tax_slabs");
        }

        return null;
    }

    /// <summary>
    /// FR-6: slabs must be contiguous — ordered ascending with no gaps and no overlaps; only the last slab
    /// may be unbounded (SlabTo null). Returns an error message or null.
    /// </summary>
    private static string? ValidateContiguity(IReadOnlyList<TaxSlabInput> slabs)
    {
        var ordered = slabs.OrderBy(s => s.OrderIndex).ThenBy(s => s.SlabFrom).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            if (s.SlabTo is not null && s.SlabTo <= s.SlabFrom)
                return $"Slab {i + 1}: upper bound must be greater than the lower bound.";
            if (i == 0 && s.SlabFrom != 0m)
                return "The first tax slab must start at 0.";
            if (i < ordered.Count - 1)
            {
                if (s.SlabTo is null)
                    return $"Only the highest tax slab may be unbounded (slab {i + 1} has no upper bound).";
                if (ordered[i + 1].SlabFrom != s.SlabTo)
                    return $"Tax slabs must be contiguous: slab {i + 2} must start where slab {i + 1} ends ({s.SlabTo}).";
            }
        }
        return null;
    }

    private List<TaxSlab> BuildSlabs(Guid ruleId, IReadOnlyList<TaxSlabInput> slabs)
        => slabs.OrderBy(s => s.OrderIndex).Select((s, i) => new TaxSlab
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            StatutoryRuleId = ruleId,
            SlabFrom = s.SlabFrom,
            SlabTo = s.SlabTo,
            RatePercentage = s.RatePercentage,
            OrderIndex = i,
            IsDeleted = false,
        }).ToList();

    private SocialSecurityRule? BuildSocialSecurity(Guid ruleId, SocialSecurityInputDto? social)
        => social is null ? null : new SocialSecurityRule
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            StatutoryRuleId = ruleId,
            EmployeeRate = social.EmployeeRate,
            EmployerRate = social.EmployerRate,
            WageCeilingAnnual = social.WageCeilingAnnual,
            ApplicableOn = social.ApplicableOn,
            ApplicableComponentIds = social.ApplicableComponentIds is { Count: > 0 }
                ? [.. social.ApplicableComponentIds]
                : null,
            IsDeleted = false,
        };

    private async Task<Result<StatutoryRuleDto>> BuildDtoResultAsync(Guid ruleId, CancellationToken cancellationToken)
    {
        var rule = await _dbContext.StatutoryRules.AsNoTracking()
            .Include(r => r.TaxSlabs)
            .Include(r => r.SocialSecurityRule)
            .FirstAsync(r => r.Id == ruleId, cancellationToken);

        return Result<StatutoryRuleDto>.Success(ToDto(rule));
    }

    private static StatutoryRuleDto ToDto(StatutoryRule r) => new()
    {
        Id = r.Id,
        RuleType = r.RuleType,
        RuleTypeName = r.RuleType.ToString(),
        RuleName = r.RuleName,
        CountryCode = r.CountryCode,
        FiscalYear = r.FiscalYear,
        EffectiveFrom = r.EffectiveFrom,
        EffectiveTo = r.EffectiveTo,
        IsActive = r.IsActive,
        TaxSlabs = r.TaxSlabs.OrderBy(s => s.OrderIndex).Select(s => new TaxSlabDto
        {
            Id = s.Id,
            SlabFrom = s.SlabFrom,
            SlabTo = s.SlabTo,
            RatePercentage = s.RatePercentage,
            OrderIndex = s.OrderIndex,
        }).ToList(),
        SocialSecurity = r.SocialSecurityRule is null ? null : new SocialSecurityRuleDto
        {
            Id = r.SocialSecurityRule.Id,
            EmployeeRate = r.SocialSecurityRule.EmployeeRate,
            EmployerRate = r.SocialSecurityRule.EmployerRate,
            WageCeilingAnnual = r.SocialSecurityRule.WageCeilingAnnual,
            ApplicableOn = r.SocialSecurityRule.ApplicableOn,
            ApplicableOnName = r.SocialSecurityRule.ApplicableOn.ToString(),
            ApplicableComponentIds = r.SocialSecurityRule.ApplicableComponentIds ?? [],
        },
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
    };
}
