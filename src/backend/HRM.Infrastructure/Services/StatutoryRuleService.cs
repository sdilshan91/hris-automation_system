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
        r.IsActive, r.IsCumulative, SlabCount = r.TaxSlabs?.Count ?? 0,
    };

    public async Task<Result<StatutoryRuleDto>> CreateAsync(CreateStatutoryRuleInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<StatutoryRuleDto>.Failure("Tenant context is not resolved.", 400);

        var exemptions = input.Exemptions ?? [];
        var shapeError = ValidateShape(input.RuleType, input.TaxSlabs, input.SocialSecurity, exemptions, input.IsCumulative);
        if (shapeError is not null)
            return Result<StatutoryRuleDto>.Failure(shapeError.Value.error, 400, shapeError.Value.code);

        var countryCode = input.CountryCode.Trim().ToUpperInvariant();
        var fiscalYear = input.FiscalYear.Trim();

        // Multi-country tax foundation (integrity, mirrors the ISSUE-170 guard style): reject a duplicate
        // (tenant, type, country, fiscal-year) rule whose effective window OVERLAPS an existing one — otherwise
        // two same-type rules for the same country/FY would collide at resolve time (arbitrary winner). A
        // DIFFERENT country's rule for the same type/FY is allowed (that is the whole point of multi-country).
        // The tenant-scoped UNIQUE index (tenant, type, country, fiscal_year) is the hard DB backstop.
        var newFrom = input.EffectiveFrom;
        var newTo = input.EffectiveTo ?? DateOnly.MaxValue;
        var sameKey = await _dbContext.StatutoryRules.AsNoTracking()
            .Where(r => r.RuleType == input.RuleType && r.CountryCode == countryCode && r.FiscalYear == fiscalYear)
            .Select(r => new { r.EffectiveFrom, r.EffectiveTo })
            .ToListAsync(cancellationToken);
        if (sameKey.Any(r => r.EffectiveFrom <= newTo && (r.EffectiveTo ?? DateOnly.MaxValue) >= newFrom))
            return Result<StatutoryRuleDto>.Failure(
                $"A {input.RuleType} statutory rule already exists for country '{countryCode}' in fiscal year "
                + $"'{fiscalYear}' with an overlapping effective period. Edit the existing rule or use a "
                + "non-overlapping period.",
                409, "duplicate_statutory_rule");

        var ruleId = BaseEntity.NewUuidV7();
        var rule = new StatutoryRule
        {
            Id = ruleId,
            TenantId = _tenantContext.TenantId,
            RuleType = input.RuleType,
            RuleName = input.RuleName.Trim(),
            CountryCode = countryCode,
            FiscalYear = fiscalYear,
            EffectiveFrom = input.EffectiveFrom,
            EffectiveTo = input.EffectiveTo,
            IsActive = input.IsActive,
            IsCumulative = input.IsCumulative,
            IsDeleted = false,
            TaxSlabs = BuildSlabs(ruleId, input.TaxSlabs),
            Exemptions = BuildExemptions(ruleId, exemptions),
            SocialSecurityRule = BuildSocialSecurity(ruleId, input.SocialSecurity),
        };

        _dbContext.StatutoryRules.Add(rule);

        // US-PAY-012 (FR-2): audit the create — before=null, after=snapshot. Committed atomically.
        _audit.Log(PA.StatutoryRuleCreated, PA.ResourceType.StatutoryRule,
            rule.Id.ToString(), before: null, after: Snapshot(rule));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.GetBaseException() is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Concurrency backstop: two creates passed the app-level overlap pre-check at the same time and
            // collided on the (tenant, type, country, fiscal_year, effective_from) unique index. Surface the
            // SAME 409 the pre-check returns instead of a raw 500.
            _dbContext.ChangeTracker.Clear();
            return Result<StatutoryRuleDto>.Failure(
                $"A {input.RuleType} statutory rule already exists for country '{countryCode}' in fiscal year "
                + $"'{fiscalYear}' with an overlapping effective period. Edit the existing rule or use a "
                + "non-overlapping period.",
                409, "duplicate_statutory_rule");
        }

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
            .Include(r => r.Exemptions)
            .Include(r => r.SocialSecurityRule)
            .FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
        if (rule is null)
            return Result<StatutoryRuleDto>.Failure("Statutory rule not found.", 404);

        var exemptions = input.Exemptions ?? [];
        var shapeError = ValidateShape(rule.RuleType, input.TaxSlabs, input.SocialSecurity, exemptions, input.IsCumulative);
        if (shapeError is not null)
            return Result<StatutoryRuleDto>.Failure(shapeError.Value.error, 400, shapeError.Value.code);

        // BR-7 (ISSUE-170): HARD-block editing a statutory rule whose effective period overlaps ANY finalized
        // payroll run. Those periods' payslips already applied this rule and are never recomputed, so a
        // retroactive edit would silently desync the rule from the numbers already paid. Corrections to a
        // finalized period must go through a US-PAY-007 payroll adjustment instead. The check uses the rule's
        // CURRENT effective window (the periods it has actually governed) OR the proposed one (so an edit can't
        // extend the rule onto a finalized period either).
        var windowStart = rule.EffectiveFrom < input.EffectiveFrom ? rule.EffectiveFrom : input.EffectiveFrom;
        var windowEnd = (rule.EffectiveTo ?? DateOnly.MaxValue) > (input.EffectiveTo ?? DateOnly.MaxValue)
            ? (rule.EffectiveTo ?? DateOnly.MaxValue)
            : (input.EffectiveTo ?? DateOnly.MaxValue);
        var finalizedPeriods = await _dbContext.PayrollRuns
            .Where(r => r.Status == PayrollRunStatus.Finalized)
            .Select(r => new { r.PayYear, r.PayMonth })
            .ToListAsync(cancellationToken);
        var overlapsFinalized = finalizedPeriods.Any(p =>
        {
            var monthStart = new DateOnly(p.PayYear, p.PayMonth, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            return monthStart <= windowEnd && monthEnd >= windowStart; // period intersects the rule window.
        });
        if (overlapsFinalized)
            return Result<StatutoryRuleDto>.Failure(
                "This statutory rule applies to a period with a finalized payroll run and cannot be edited "
                + "retroactively (BR-7). Correct finalized payslips via a payroll adjustment (US-PAY-007) instead.",
                409, "statutory_rule_finalized_period");

        // US-PAY-012: capture the BEFORE snapshot prior to mutating (TaxSlabs already Included above).
        var before = Snapshot(rule);

        rule.RuleName = input.RuleName.Trim();
        rule.CountryCode = input.CountryCode.Trim().ToUpperInvariant();
        rule.FiscalYear = input.FiscalYear.Trim();
        rule.EffectiveFrom = input.EffectiveFrom;
        rule.EffectiveTo = input.EffectiveTo;
        rule.IsActive = input.IsActive;
        rule.IsCumulative = input.IsCumulative;

        // Replace slabs + social-security wholesale (these are owned children; historical payslips are never
        // recomputed so prior versions can be replaced for the SAME rule version).
        // BUG-073: explicitly ADD the rebuilt children. Assigning new children (with client-generated
        // UUIDv7 keys) to the TRACKED rule's navigation made EF treat them as MODIFIED, not Added — it
        // emitted `UPDATE tax_slab ... WHERE id = <brand-new-id>` which matched 0 rows and threw
        // DbUpdateConcurrencyException on every update. AddRange/Add forces the Added state (INSERT).
        _dbContext.TaxSlabs.RemoveRange(rule.TaxSlabs);
        var newSlabs = BuildSlabs(ruleId, input.TaxSlabs);
        _dbContext.TaxSlabs.AddRange(newSlabs);
        rule.TaxSlabs = newSlabs;

        // TAX-2: replace the configurable exemptions wholesale (same BUG-073 explicit-Add pattern as slabs).
        _dbContext.StatutoryExemptions.RemoveRange(rule.Exemptions);
        var newExemptions = BuildExemptions(ruleId, exemptions);
        _dbContext.StatutoryExemptions.AddRange(newExemptions);
        rule.Exemptions = newExemptions;

        if (rule.SocialSecurityRule is not null)
            _dbContext.SocialSecurityRules.Remove(rule.SocialSecurityRule);
        var newSocial = BuildSocialSecurity(ruleId, input.SocialSecurity);
        if (newSocial is not null)
            _dbContext.SocialSecurityRules.Add(newSocial);
        rule.SocialSecurityRule = newSocial;

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

        var sourceRules = await _dbContext.StatutoryRules.AsNoTracking()
            .Include(r => r.TaxSlabs)
            .Include(r => r.Exemptions)
            .Include(r => r.SocialSecurityRule)
            .Where(r => r.FiscalYear == source)
            .ToListAsync(cancellationToken);

        if (sourceRules.Count == 0)
            return Result<IReadOnlyList<StatutoryRuleDto>>.Failure(
                $"No statutory rules found for fiscal year '{source}'.", 404, "source_fiscal_year_empty");

        // Multi-country tax foundation: the target-FY guard is PER COUNTRY. Cloning country A's rules into a new
        // fiscal year must succeed even when country B already has rules in that target FY — only a collision on
        // one of the SOURCE's own countries (which would breach the unique (tenant,type,country,FY) index) blocks.
        var sourceCountries = sourceRules.Select(r => r.CountryCode).Distinct().ToList();
        if (await _dbContext.StatutoryRules
                .AnyAsync(r => r.FiscalYear == target && sourceCountries.Contains(r.CountryCode), cancellationToken))
            return Result<IReadOnlyList<StatutoryRuleDto>>.Failure(
                $"Statutory rules already exist for fiscal year '{target}' in one of the source countries "
                + $"({string.Join(", ", sourceCountries)}). Delete them before cloning.", 409, "target_fiscal_year_exists");

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
                IsCumulative = src.IsCumulative,
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
                // TAX-2: carry the configurable exemptions across the clone (children, mirrors TaxSlabs).
                Exemptions = src.Exemptions.Select(e => new StatutoryExemption
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = _tenantContext.TenantId,
                    StatutoryRuleId = cloneId,
                    Name = e.Name,
                    CalculationType = e.CalculationType,
                    Value = e.Value,
                    ComponentId = e.ComponentId,
                    MaxAmount = e.MaxAmount,
                    IsAnnual = e.IsAnnual,
                    OrderIndex = e.OrderIndex,
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
        // TAX-2 component-aware preview: the caller may now supply sample per-component amounts so a
        // PercentOfComponent EXEMPTION previews the same figure the run applies (the run derives these from the
        // slip's earning/reimbursement lines; the what-if has no salary structure, so they are supplied). When
        // none are given a percent-of-component exemption still resolves to 0 here (as before); FlatAmount +
        // PercentOfGross preview exactly regardless. An empty map is treated as none.
        var componentAmounts = input.ComponentAmounts is { Count: > 0 } ? input.ComponentAmounts : null;
        // TAX-3 cumulative preview: the caller supplies the FY-to-date prior taxable + withheld so a cumulative
        // rule previews the YTD true-up delta (tax(priorTaxable+thisMonth) − priorWithheld) instead of the
        // first-month figure. Both default to 0 → monthly/first-month behaviour (unchanged for non-cumulative rules).
        var wage = new StatutoryWageInput(
            MonthlyGross: input.MonthlyGross,
            MonthlyBasic: basic,
            ExemptEarnings: input.ExemptEarnings,
            DeclaredExemptions: input.DeclaredMonthlyExemptions,
            ComponentAmountsById: componentAmounts,
            PriorTaxableIncomeYtd: input.PriorTaxableIncomeYtd,
            PriorTaxWithheldYtd: input.PriorTaxWithheldYtd);

        // Use today's date to pick the effective version; FR-5 is a what-if so a current-period date is fine.
        // Multi-country tax foundation: the preview computes under the selected country's regime (null → nothing).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var resolved = await _resolver.ResolveAsync(
            today.Year, today.Month, wage, input.FiscalYear, input.CountryCode, cancellationToken);
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
        StatutoryRuleType ruleType, IReadOnlyList<TaxSlabInput> slabs, SocialSecurityInputDto? social,
        IReadOnlyList<ExemptionInput> exemptions, bool isCumulative)
    {
        // TAX-3: the cumulative (YTD) basis is meaningful only for income tax — reject it on any other type
        // (mirrors the exemptions guard) so a caller can't silently mark an EPF/ETF rule cumulative.
        if (ruleType != StatutoryRuleType.IncomeTax && isCumulative)
            return ("Only an income-tax rule may be cumulative (YTD).", "unexpected_cumulative");

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

            // TAX-2: exemptions reduce the INCOME-TAX taxable base only — never a social-security rule.
            if (exemptions.Count > 0)
                return ("Only an income-tax rule may carry tax exemptions.", "unexpected_exemptions");
        }

        // TAX-2: per-exemption structural checks (mirrors the FluentValidation gate, re-checked defensively).
        var exemptionError = ValidateExemptions(exemptions);
        if (exemptionError is not null)
            return exemptionError.Value;

        return null;
    }

    /// <summary>
    /// TAX-2: validates each configurable exemption. A PercentOfComponent exemption requires a ComponentId;
    /// Value must be non-negative (and 0..100 for percentage types); MaxAmount must be non-negative when present.
    /// </summary>
    private static (string error, string code)? ValidateExemptions(IReadOnlyList<ExemptionInput> exemptions)
    {
        foreach (var e in exemptions)
        {
            if (string.IsNullOrWhiteSpace(e.Name))
                return ("Each tax exemption requires a name.", "exemption_name_required");
            if (e.Value < 0m)
                return ($"Tax exemption '{e.Name}': value cannot be negative.", "exemption_value_negative");
            if (e.CalculationType is ExemptionCalculationType.PercentOfGross or ExemptionCalculationType.PercentOfComponent
                && e.Value > 100m)
                return ($"Tax exemption '{e.Name}': a percentage value cannot exceed 100.", "exemption_percent_out_of_range");
            if (e.CalculationType == ExemptionCalculationType.PercentOfComponent && e.ComponentId is null)
                return ($"Tax exemption '{e.Name}': a component id is required for a percent-of-component exemption.",
                    "exemption_component_required");
            if (e.MaxAmount is < 0m)
                return ($"Tax exemption '{e.Name}': maximum amount cannot be negative.", "exemption_max_negative");
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

    /// <summary>TAX-2: builds the exemption children, stamped with tenant + rule id, re-indexed by OrderIndex.</summary>
    private List<StatutoryExemption> BuildExemptions(Guid ruleId, IReadOnlyList<ExemptionInput> exemptions)
        => exemptions.OrderBy(e => e.OrderIndex).Select((e, i) => new StatutoryExemption
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            StatutoryRuleId = ruleId,
            Name = e.Name.Trim(),
            CalculationType = e.CalculationType,
            Value = e.Value,
            ComponentId = e.ComponentId,
            MaxAmount = e.MaxAmount,
            IsAnnual = e.IsAnnual,
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
            .Include(r => r.Exemptions)
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
        IsCumulative = r.IsCumulative,
        TaxSlabs = r.TaxSlabs.OrderBy(s => s.OrderIndex).Select(s => new TaxSlabDto
        {
            Id = s.Id,
            SlabFrom = s.SlabFrom,
            SlabTo = s.SlabTo,
            RatePercentage = s.RatePercentage,
            OrderIndex = s.OrderIndex,
        }).ToList(),
        Exemptions = r.Exemptions.OrderBy(e => e.OrderIndex).Select(e => new ExemptionDto
        {
            Id = e.Id,
            Name = e.Name,
            CalculationType = e.CalculationType,
            CalculationTypeName = e.CalculationType.ToString(),
            Value = e.Value,
            ComponentId = e.ComponentId,
            MaxAmount = e.MaxAmount,
            IsAnnual = e.IsAnnual,
            OrderIndex = e.OrderIndex,
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
