using System.Text.Json;
using HRM.Domain.Leave;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Leave entitlement rules, overrides, resolution, and accrual processing (US-LV-002).
/// All queries are tenant-scoped via ITenantContext and EF global query filters.
/// </summary>
public sealed class LeaveEntitlementService : ILeaveEntitlementService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantLeaveYearResolver _leaveYearResolver;
    private readonly ILogger<LeaveEntitlementService> _logger;
    // BUG-118: optional so unit tests / non-Hangfire hosts construct the service without it (mirrors the
    // IPayrollRunJobScheduler seam). Null → the recalc is simply not enqueued.
    private readonly ILeaveEntitlementRecalcJobScheduler? _recalcScheduler;
    // BUG-291: the accrual job now credits only the periods that have ELAPSED as of today (Monthly/Quarterly),
    // so accrual output depends on "today". Trailing-optional (mirrors LeaveAccrualJob / ProcessLeaveYearEndJob)
    // so production and existing InMemory fixtures keep the real clock; money-path tests inject a fixed one.
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Batch size for accrual processing (NFR-1: handle 5,000 employees).
    /// </summary>
    private const int AccrualBatchSize = 500;

    /// <summary>
    /// BUG-118: sentinel Description prefix that tags an <c>Adjusted</c> ledger row as a rule-recalculation
    /// delta (not a manual admin adjustment). The recalc counts these as "rule-derived granted" so it is
    /// idempotent, and it never disturbs manual adjustments that lack this prefix.
    /// </summary>
    internal const string RecalcAdjustmentPrefix = "Entitlement recalculation";

    public LeaveEntitlementService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<LeaveEntitlementService> logger,
        ITenantLeaveYearResolver leaveYearResolver,
        ILeaveEntitlementRecalcJobScheduler? recalcScheduler = null,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _leaveYearResolver = leaveYearResolver;
        _logger = logger;
        _recalcScheduler = recalcScheduler;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    // ══════════════════════════════════════════════════════════════
    //  Rules CRUD
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<LeaveEntitlementRuleDto>> CreateRuleAsync(
        UpsertLeaveEntitlementRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LeaveEntitlementRuleDto>.Failure("Tenant context is not resolved.", 400);

        var validationResult = await ValidateRuleReferencesAsync(request, cancellationToken);
        if (validationResult != null)
            return Result<LeaveEntitlementRuleDto>.Failure(validationResult, 400);

        var rule = new LeaveEntitlementRule
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            LeaveTypeId = request.LeaveTypeId,
            DepartmentId = request.DepartmentId,
            JobTitleId = request.JobTitleId,
            JobLevelId = request.JobLevelId,
            EmploymentType = ParseEmploymentType(request.EmploymentType),
            TenureMinMonths = request.TenureMinMonths,
            TenureMaxMonths = request.TenureMaxMonths,
            EntitlementDays = request.EntitlementDays,
            Priority = request.Priority,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            IsActive = true,
        };

        _dbContext.LeaveEntitlementRules.Add(rule);
        // BUG-028 (US-LV-002): write a queryable tenant audit row (after-snapshot), not just an ILogger line.
        AddRuleAudit("LeaveEntitlementRule.Created", rule.Id, before: null, after: SnapshotRule(rule),
            $"Leave entitlement rule created for leave type {rule.LeaveTypeId} ({rule.EntitlementDays} days).");
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created leave entitlement rule {RuleId} for leave type {LeaveTypeId} in tenant {TenantId}",
            rule.Id, rule.LeaveTypeId, _tenantContext.TenantId);

        return Result<LeaveEntitlementRuleDto>.Success(await ToRuleDtoAsync(rule, cancellationToken));
    }

    public async Task<Result<LeaveEntitlementRuleDto>> UpdateRuleAsync(
        Guid ruleId,
        UpsertLeaveEntitlementRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LeaveEntitlementRuleDto>.Failure("Tenant context is not resolved.", 400);

        var rule = await _dbContext.LeaveEntitlementRules.FindAsync([ruleId], cancellationToken);
        if (rule == null)
            return Result<LeaveEntitlementRuleDto>.Failure("Leave entitlement rule not found.", 404);

        var validationResult = await ValidateRuleReferencesAsync(request, cancellationToken, excludeRuleId: ruleId);
        if (validationResult != null)
            return Result<LeaveEntitlementRuleDto>.Failure(validationResult, 400);

        // BUG-028 (US-LV-002): snapshot the pre-mutation state for the audit before/after.
        var beforeSnapshot = SnapshotRule(rule);

        rule.LeaveTypeId = request.LeaveTypeId;
        rule.DepartmentId = request.DepartmentId;
        rule.JobTitleId = request.JobTitleId;
        rule.JobLevelId = request.JobLevelId;
        rule.EmploymentType = ParseEmploymentType(request.EmploymentType);
        rule.TenureMinMonths = request.TenureMinMonths;
        rule.TenureMaxMonths = request.TenureMaxMonths;
        rule.EntitlementDays = request.EntitlementDays;
        rule.Priority = request.Priority;
        rule.EffectiveFrom = request.EffectiveFrom;
        rule.EffectiveTo = request.EffectiveTo;

        AddRuleAudit("LeaveEntitlementRule.Updated", rule.Id, before: beforeSnapshot, after: SnapshotRule(rule),
            $"Leave entitlement rule {rule.Id} updated.");
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated leave entitlement rule {RuleId} in tenant {TenantId}",
            rule.Id, _tenantContext.TenantId);

        // BUG-118 (AC-5): a rule edit must recalculate affected employees' balances. Enqueue a tenant-scoped
        // recalc for the current leave year + this rule's leave type, so already-accrued employees get an
        // Adjusted ledger delta to the new entitlement (the recalc is idempotent, so a no-op edit writes none).
        var leaveYear = await _leaveYearResolver.LabelForAsync(
            DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
        var jobId = _recalcScheduler?.Enqueue(
            _tenantContext.TenantId, _tenantContext.Subdomain, leaveYear, rule.LeaveTypeId);
        if (jobId is not null)
            _logger.LogInformation(
                "Enqueued entitlement recalc job {JobId} for rule {RuleId}, leaveType {LeaveTypeId}, year {LeaveYear}.",
                jobId, rule.Id, rule.LeaveTypeId, leaveYear);

        return Result<LeaveEntitlementRuleDto>.Success(await ToRuleDtoAsync(rule, cancellationToken));
    }

    public async Task<Result> DeleteRuleAsync(
        Guid ruleId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var rule = await _dbContext.LeaveEntitlementRules.FindAsync([ruleId], cancellationToken);
        if (rule == null)
            return Result.Failure("Leave entitlement rule not found.", 404);

        // BUG-028 (US-LV-002): capture the pre-delete state (delete audit is before-only).
        var beforeSnapshot = SnapshotRule(rule);

        rule.IsDeleted = true;
        rule.IsActive = false;

        AddRuleAudit("LeaveEntitlementRule.Deleted", rule.Id, before: beforeSnapshot, after: null,
            $"Leave entitlement rule {rule.Id} soft-deleted.");
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Soft-deleted leave entitlement rule {RuleId} in tenant {TenantId}",
            ruleId, _tenantContext.TenantId);

        return Result.Success();
    }

    public async Task<Result<LeaveEntitlementRuleDto>> GetRuleByIdAsync(
        Guid ruleId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LeaveEntitlementRuleDto>.Failure("Tenant context is not resolved.", 400);

        var rule = await _dbContext.LeaveEntitlementRules
            .Include(r => r.LeaveType)
            .Include(r => r.Department)
            .Include(r => r.JobTitle)
            .FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);

        if (rule == null)
            return Result<LeaveEntitlementRuleDto>.Failure("Leave entitlement rule not found.", 404);

        return Result<LeaveEntitlementRuleDto>.Success(MapRuleToDto(rule));
    }

    public async Task<Result<IReadOnlyList<LeaveEntitlementRuleDto>>> GetRulesAsync(
        Guid? leaveTypeId = null,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<LeaveEntitlementRuleDto>>.Failure("Tenant context is not resolved.", 400);

        var query = _dbContext.LeaveEntitlementRules
            .Include(r => r.LeaveType)
            .Include(r => r.Department)
            .Include(r => r.JobTitle)
            .AsQueryable();

        if (leaveTypeId.HasValue)
            query = query.Where(r => r.LeaveTypeId == leaveTypeId.Value);

        var rules = await query
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        var dtos = rules.Select(MapRuleToDto).ToList();
        return Result<IReadOnlyList<LeaveEntitlementRuleDto>>.Success(dtos);
    }

    public async Task<Result<IReadOnlyList<LeaveEntitlementRuleDto>>> BulkCreateRulesAsync(
        IReadOnlyList<UpsertLeaveEntitlementRuleRequest> requests,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<LeaveEntitlementRuleDto>>.Failure("Tenant context is not resolved.", 400);

        var results = new List<LeaveEntitlementRuleDto>();
        foreach (var request in requests)
        {
            var result = await CreateRuleAsync(request, cancellationToken);
            if (result.IsFailure)
                return Result<IReadOnlyList<LeaveEntitlementRuleDto>>.Failure(
                    $"Failed to create rule: {result.Error}", result.StatusCode ?? 400);
            results.Add(result.Value!);
        }

        return Result<IReadOnlyList<LeaveEntitlementRuleDto>>.Success(results);
    }

    // ══════════════════════════════════════════════════════════════
    //  Overrides CRUD
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<LeaveEntitlementOverrideDto>> UpsertOverrideAsync(
        UpsertLeaveEntitlementOverrideRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LeaveEntitlementOverrideDto>.Failure("Tenant context is not resolved.", 400);

        // Validate references.
        var employee = await _dbContext.Employees.FindAsync([request.EmployeeId], cancellationToken);
        if (employee == null)
            return Result<LeaveEntitlementOverrideDto>.Failure("Employee not found.", 404);

        var leaveType = await _dbContext.LeaveTypes.FindAsync([request.LeaveTypeId], cancellationToken);
        if (leaveType == null)
            return Result<LeaveEntitlementOverrideDto>.Failure("Leave type not found.", 404);

        if (request.EntitlementDays < 0)
            return Result<LeaveEntitlementOverrideDto>.Failure("Entitlement days cannot be negative.", 400);

        // Find existing override for upsert.
        var existing = await _dbContext.LeaveEntitlementOverrides
            .FirstOrDefaultAsync(o =>
                o.EmployeeId == request.EmployeeId &&
                o.LeaveTypeId == request.LeaveTypeId &&
                o.LeaveYear == request.LeaveYear,
                cancellationToken);

        // BUG-028 (US-LV-002): distinguish insert vs update and snapshot before/after for the audit.
        var wasInsert = existing == null;
        string? beforeSnapshot = null;
        if (existing != null)
        {
            beforeSnapshot = SnapshotOverride(existing);
            existing.EntitlementDays = request.EntitlementDays;
            existing.Reason = request.Reason;
        }
        else
        {
            existing = new LeaveEntitlementOverride
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                EmployeeId = request.EmployeeId,
                LeaveTypeId = request.LeaveTypeId,
                LeaveYear = request.LeaveYear,
                EntitlementDays = request.EntitlementDays,
                Reason = request.Reason,
            };
            _dbContext.LeaveEntitlementOverrides.Add(existing);
        }

        AddOverrideAudit("LeaveEntitlementOverride.Upserted", existing.Id, before: beforeSnapshot,
            after: SnapshotOverride(existing),
            $"Leave entitlement override {(wasInsert ? "created" : "updated")} for employee {existing.EmployeeId}, leave type {existing.LeaveTypeId}, year {existing.LeaveYear}.");
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Upserted leave entitlement override for employee {EmployeeId}, leave type {LeaveTypeId}, year {Year} in tenant {TenantId}",
            request.EmployeeId, request.LeaveTypeId, request.LeaveYear, _tenantContext.TenantId);

        return Result<LeaveEntitlementOverrideDto>.Success(new LeaveEntitlementOverrideDto
        {
            Id = existing.Id,
            EmployeeId = existing.EmployeeId,
            EmployeeName = $"{employee.FirstName} {employee.LastName}",
            LeaveTypeId = existing.LeaveTypeId,
            LeaveTypeName = leaveType.Name,
            LeaveYear = existing.LeaveYear,
            EntitlementDays = existing.EntitlementDays,
            Reason = existing.Reason,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = existing.UpdatedAt,
        });
    }

    public async Task<Result> DeleteOverrideAsync(
        Guid overrideId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var entity = await _dbContext.LeaveEntitlementOverrides.FindAsync([overrideId], cancellationToken);
        if (entity == null)
            return Result.Failure("Leave entitlement override not found.", 404);

        // BUG-028 (US-LV-002): capture the pre-delete state (delete audit is before-only).
        var beforeSnapshot = SnapshotOverride(entity);

        entity.IsDeleted = true;

        AddOverrideAudit("LeaveEntitlementOverride.Deleted", entity.Id, before: beforeSnapshot, after: null,
            $"Leave entitlement override {entity.Id} soft-deleted.");
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Soft-deleted leave entitlement override {OverrideId} in tenant {TenantId}",
            overrideId, _tenantContext.TenantId);

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<LeaveEntitlementOverrideDto>>> GetOverridesAsync(
        Guid? employeeId = null,
        Guid? leaveTypeId = null,
        int? leaveYear = null,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<LeaveEntitlementOverrideDto>>.Failure("Tenant context is not resolved.", 400);

        var query = _dbContext.LeaveEntitlementOverrides
            .Include(o => o.Employee)
            .Include(o => o.LeaveType)
            .AsQueryable();

        if (employeeId.HasValue)
            query = query.Where(o => o.EmployeeId == employeeId.Value);
        if (leaveTypeId.HasValue)
            query = query.Where(o => o.LeaveTypeId == leaveTypeId.Value);
        if (leaveYear.HasValue)
            query = query.Where(o => o.LeaveYear == leaveYear.Value);

        var overrides = await query
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        var dtos = overrides.Select(o => new LeaveEntitlementOverrideDto
        {
            Id = o.Id,
            EmployeeId = o.EmployeeId,
            EmployeeName = o.Employee != null ? $"{o.Employee.FirstName} {o.Employee.LastName}" : string.Empty,
            LeaveTypeId = o.LeaveTypeId,
            LeaveTypeName = o.LeaveType?.Name ?? string.Empty,
            LeaveYear = o.LeaveYear,
            EntitlementDays = o.EntitlementDays,
            Reason = o.Reason,
            CreatedAt = o.CreatedAt,
            UpdatedAt = o.UpdatedAt,
        }).ToList();

        return Result<IReadOnlyList<LeaveEntitlementOverrideDto>>.Success(dtos);
    }

    // ══════════════════════════════════════════════════════════════
    //  Entitlement Resolution
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<EffectiveEntitlementDto>> ComputeEffectiveEntitlementAsync(
        Guid employeeId,
        Guid leaveTypeId,
        int leaveYear,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<EffectiveEntitlementDto>.Failure("Tenant context is not resolved.", 400);

        var employee = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (employee == null)
            return Result<EffectiveEntitlementDto>.Failure("Employee not found.", 404);

        var leaveType = await _dbContext.LeaveTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(lt => lt.Id == leaveTypeId, cancellationToken);
        if (leaveType == null)
            return Result<EffectiveEntitlementDto>.Failure("Leave type not found.", 404);

        // BR-3: Probation employees only get probation-eligible leave types.
        if (employee.Status == EmployeeStatus.Probation && !leaveType.ProbationEligible)
        {
            return Result<EffectiveEntitlementDto>.Success(new EffectiveEntitlementDto
            {
                EmployeeId = employeeId,
                LeaveTypeId = leaveTypeId,
                LeaveTypeName = leaveType.Name,
                LeaveYear = leaveYear,
                BaseEntitlementDays = 0,
                ProratedEntitlementDays = 0,
                Source = "probation_ineligible",
                CurrentBalance = 0,
            });
        }

        var resolution = await ResolveEntitlementAsync(employee, leaveType, leaveYear, cancellationToken);

        // Pro-rata for mid-year joiners (FR-3, AC-4) and part-timers (US-LV-002 AC-K1 — closed by
        // US-CHR-013's Employee.Fte; full-timers are 1.00 so this is unchanged for them).
        decimal prorated = LeaveEntitlementEngine.CalculateProRata(
            resolution.BaseEntitlementDays, employee.DateOfJoining, leaveYear, fte: employee.Fte,
            fiscalYearStartMonth: await ResolveFiscalYearStartMonthAsync(cancellationToken));

        // Get current balance from ledger.
        decimal currentBalance = await GetLedgerBalanceAsync(
            employeeId, leaveTypeId, leaveYear, cancellationToken);

        return Result<EffectiveEntitlementDto>.Success(new EffectiveEntitlementDto
        {
            EmployeeId = employeeId,
            LeaveTypeId = leaveTypeId,
            LeaveTypeName = leaveType.Name,
            LeaveYear = leaveYear,
            BaseEntitlementDays = resolution.BaseEntitlementDays,
            ProratedEntitlementDays = prorated,
            Source = resolution.Source,
            CurrentBalance = currentBalance,
        });
    }

    /// <summary>
    /// BUG-124: set-based counterpart to <see cref="ComputeEffectiveEntitlementAsync"/> for the leave
    /// reports. Replicates its EXACT proration logic (probation gate → override → most-specific active
    /// rule → leave-type default, then <see cref="LeaveEntitlementEngine.CalculateProRata"/>) but resolves
    /// every (employee × leave type) pair from just two queries — one for overrides, one for active rules —
    /// instead of ~5 round-trips per pair. The ledger balance is intentionally NOT computed (reports use
    /// only the prorated entitlement). Values are byte-identical to the per-pair path.
    /// </summary>
    public async Task<Dictionary<(Guid EmployeeId, Guid LeaveTypeId), decimal>> ComputeProratedEntitlementsBatchAsync(
        IReadOnlyList<Employee> employees,
        IReadOnlyList<LeaveType> leaveTypes,
        int year,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<(Guid EmployeeId, Guid LeaveTypeId), decimal>();
        if (employees.Count == 0 || leaveTypes.Count == 0)
            return result;

        // ISSUE-305: resolved ONCE for the whole batch — this method exists to avoid per-employee work, so a
        // per-employee tenant lookup would defeat its purpose.
        var fiscalYearStartMonth = await ResolveFiscalYearStartMonthAsync(cancellationToken);

        var employeeIds = employees.Select(e => e.Id).ToList();
        var leaveTypeIds = leaveTypes.Select(lt => lt.Id).ToList();

        // ONE query for per-employee overrides (AC-3) covering all scoped pairs for the year.
        var overrideRows = await _dbContext.LeaveEntitlementOverrides
            .AsNoTracking()
            .Where(o => employeeIds.Contains(o.EmployeeId)
                        && leaveTypeIds.Contains(o.LeaveTypeId)
                        && o.LeaveYear == year)
            .Select(o => new { o.EmployeeId, o.LeaveTypeId, o.EntitlementDays })
            .ToListAsync(cancellationToken);

        var overrideByKey = new Dictionary<(Guid EmployeeId, Guid LeaveTypeId), decimal>();
        foreach (var o in overrideRows)
            overrideByKey[(o.EmployeeId, o.LeaveTypeId)] = o.EntitlementDays;

        // ONE query for the active rules effective at the year's reference date (mirrors the per-pair
        // ResolveEntitlementAsync: EffectiveFrom/To are timestamptz, so the reference date is UTC-kinded).
        // ISSUE-305: the reference date is the LEAVE year's first day — not 1 January for a fiscal tenant.
        // EffectiveFrom/To are timestamptz, so it must stay UTC-kinded or Npgsql rejects it.
        var referenceDate = LeaveYear.StartOf(year, fiscalYearStartMonth)
            .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var ruleRows = await _dbContext.LeaveEntitlementRules
            .AsNoTracking()
            .Where(r => leaveTypeIds.Contains(r.LeaveTypeId)
                        && r.IsActive
                        && r.EffectiveFrom <= referenceDate
                        && (!r.EffectiveTo.HasValue || r.EffectiveTo.Value >= referenceDate))
            .ToListAsync(cancellationToken);

        var rulesByLeaveType = ruleRows
            .GroupBy(r => r.LeaveTypeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<LeaveEntitlementRule>)g.ToList());
        var emptyRules = (IReadOnlyList<LeaveEntitlementRule>)Array.Empty<LeaveEntitlementRule>();

        foreach (var employee in employees)
        {
            // Tenure depends only on the employee + reference date (not the leave type) — compute once.
            var tenureMonths = LeaveEntitlementEngine.ComputeTenureMonths(
                employee.DateOfJoining, referenceDate);

            foreach (var leaveType in leaveTypes)
            {
                var key = (employee.Id, leaveType.Id);

                // BR-3: probation employees only get probation-eligible types (gate applied FIRST, exactly
                // as ComputeEffectiveEntitlementAsync does — before override/rule resolution).
                if (employee.Status == EmployeeStatus.Probation && !leaveType.ProbationEligible)
                {
                    result[key] = 0m;
                    continue;
                }

                decimal baseEntitlement;
                if (overrideByKey.TryGetValue(key, out var overrideDays))
                {
                    // Override beats rules and default (AC-3).
                    baseEntitlement = overrideDays;
                }
                else
                {
                    var rules = rulesByLeaveType.TryGetValue(leaveType.Id, out var r) ? r : emptyRules;
                    baseEntitlement = LeaveEntitlementEngine.Resolve(
                        rules,
                        employee.DepartmentId,
                        employee.JobTitleId,
                        employee.EmploymentType,
                        tenureMonths,
                        leaveType.AnnualEntitlement).BaseEntitlementDays;
                }

                // Pro-rata for mid-year joiners (FR-3, AC-4) + part-timers (US-LV-002 AC-K1 / US-CHR-013),
                // identical inputs to the per-pair path.
                result[key] = LeaveEntitlementEngine.CalculateProRata(
                    baseEntitlement, employee.DateOfJoining, year, fte: employee.Fte,
                    fiscalYearStartMonth: fiscalYearStartMonth);
            }
        }

        return result;
    }

    // ══════════════════════════════════════════════════════════════
    //  Accrual Processing (Hangfire job)
    // ══════════════════════════════════════════════════════════════

    public async Task ProcessAccrualsAsync(
        int leaveYear,
        Guid? leaveTypeId = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting accrual processing for year {LeaveYear}, leaveTypeId={LeaveTypeId}, tenant={TenantId}",
            leaveYear, leaveTypeId, _tenantContext.TenantId);

        // Get all active leave types (or a specific one).
        var leaveTypes = await _dbContext.LeaveTypes
            .Where(lt => lt.IsActive && (!leaveTypeId.HasValue || lt.Id == leaveTypeId.Value))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Get active employees in batches.
        var totalEmployees = await _dbContext.Employees
            .CountAsync(e => e.IsActive && !e.IsDeleted, cancellationToken);

        int processed = 0;
        int skip = 0;

        while (skip < totalEmployees)
        {
            var employees = await _dbContext.Employees
                .Where(e => e.IsActive && !e.IsDeleted)
                .OrderBy(e => e.Id)
                .Skip(skip)
                .Take(AccrualBatchSize)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (employees.Count == 0)
                break;

            foreach (var employee in employees)
            {
                foreach (var lt in leaveTypes)
                {
                    // BR-3: Probation employees only get probation-eligible types.
                    if (employee.Status == EmployeeStatus.Probation && !lt.ProbationEligible)
                        continue;

                    await ProcessSingleAccrualAsync(employee, lt, leaveYear, cancellationToken);
                }
            }

            skip += AccrualBatchSize;
            processed += employees.Count;
            _logger.LogInformation("Processed accruals for {Processed}/{Total} employees", processed, totalEmployees);
        }

        _logger.LogInformation(
            "Completed accrual processing for year {LeaveYear}. Total employees processed: {Total}",
            leaveYear, processed);
    }

    public async Task RecalculateEntitlementsAsync(
        int leaveYear,
        Guid? leaveTypeId = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting entitlement recalculation for year {LeaveYear}, leaveTypeId={LeaveTypeId}, tenant={TenantId}",
            leaveYear, leaveTypeId, _tenantContext.TenantId);

        var leaveTypes = await _dbContext.LeaveTypes
            .Where(lt => lt.IsActive && (!leaveTypeId.HasValue || lt.Id == leaveTypeId.Value))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        if (leaveTypes.Count == 0)
            return;

        var fiscalYearStartMonth = await ResolveFiscalYearStartMonthAsync(cancellationToken);
        var totalEmployees = await _dbContext.Employees
            .CountAsync(e => e.IsActive && !e.IsDeleted, cancellationToken);

        int skip = 0, adjusted = 0;
        while (skip < totalEmployees)
        {
            var employees = await _dbContext.Employees
                .Where(e => e.IsActive && !e.IsDeleted)
                .OrderBy(e => e.Id)
                .Skip(skip)
                .Take(AccrualBatchSize)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (employees.Count == 0)
                break;

            foreach (var employee in employees)
            {
                foreach (var lt in leaveTypes)
                {
                    if (employee.Status == EmployeeStatus.Probation && !lt.ProbationEligible)
                        continue;

                    if (await RecalcSingleEntitlementAsync(employee, lt, leaveYear, fiscalYearStartMonth, cancellationToken))
                        adjusted++;
                }
            }

            skip += AccrualBatchSize;
        }

        _logger.LogInformation(
            "Completed entitlement recalculation for year {LeaveYear}. Balance adjustments written: {Adjusted}",
            leaveYear, adjusted);
    }

    /// <summary>
    /// BUG-118: bring ONE already-accrued employee×type into line with the current rule by writing an
    /// <c>Adjusted</c> ledger delta (target − rule-derived-granted). Returns true when an adjustment was written.
    /// Not-yet-accrued employees are skipped (the accrual job credits them at the new amount). "Rule-derived
    /// granted" = the Accrual row + prior recalc adjustments (tagged by <see cref="RecalcAdjustmentPrefix"/>),
    /// so this is idempotent and never disturbs manual (untagged) adjustments, usage, carry-forward, or expiry.
    /// </summary>
    private async Task<bool> RecalcSingleEntitlementAsync(
        Employee employee,
        LeaveType leaveType,
        int leaveYear,
        int fiscalYearStartMonth,
        CancellationToken cancellationToken)
    {
        var entries = await _dbContext.LeaveLedgerEntries
            .Where(l => l.EmployeeId == employee.Id && l.LeaveTypeId == leaveType.Id && l.LeaveYear == leaveYear)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Only adjust employees who were already accrued for this period — a not-yet-accrued employee is
        // credited (at the new amount) by the normal accrual run, so an Adjusted delta would double-count.
        if (!entries.Any(l => l.EntryType == LedgerEntryType.Accrual))
            return false;

        // Rule-derived entitlement granted so far = the accrual + this recalc's own prior adjustments.
        decimal ruleGranted = entries
            .Where(l => l.EntryType == LedgerEntryType.Accrual
                        || (l.EntryType == LedgerEntryType.Adjusted
                            && l.Description != null
                            && l.Description.StartsWith(RecalcAdjustmentPrefix, StringComparison.Ordinal)))
            .Sum(l => l.Amount);

        var resolution = await ResolveEntitlementAsync(employee, leaveType, leaveYear, cancellationToken);
        decimal target = LeaveEntitlementEngine.CalculateProRata(
            resolution.BaseEntitlementDays, employee.DateOfJoining, leaveYear,
            fte: employee.Fte, fiscalYearStartMonth: fiscalYearStartMonth);

        decimal delta = target - ruleGranted;
        if (delta == 0m)
            return false;

        decimal currentBalance = await GetLedgerBalanceAsync(
            employee.Id, leaveType.Id, leaveYear, cancellationToken);

        _dbContext.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = employee.TenantId,
            EntryType = LedgerEntryType.Adjusted,
            EmployeeId = employee.Id,
            LeaveTypeId = leaveType.Id,
            LeaveYear = leaveYear,
            Amount = delta,
            BalanceAfter = currentBalance + delta,
            Description = $"{RecalcAdjustmentPrefix}: {delta:+0.##;-0.##} days (rule entitlement now {target:0.##})",
            OccurredAt = DateTime.UtcNow,
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ══════════════════════════════════════════════════════════════
    //  Private helpers
    // ══════════════════════════════════════════════════════════════

    private async Task ProcessSingleAccrualAsync(
        Employee employee,
        LeaveType leaveType,
        int leaveYear,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolveEntitlementAsync(employee, leaveType, leaveYear, cancellationToken);
        var fiscalYearStartMonth = await ResolveFiscalYearStartMonthAsync(cancellationToken);

        // Pro-rata for mid-year joiners (FR-3, AC-4) + part-timers (US-LV-002 AC-K1 / US-CHR-013). The
        // accrual job CREDITS the ledger, so an FTE miss here is a real over-credit, not just a display bug.
        // This is the FULL-YEAR prorated entitlement; BUG-291 spreads it across the accrual periods BELOW —
        // the pro-ration is applied ONCE here (single discount), never again per period.
        decimal proratedAnnual = LeaveEntitlementEngine.CalculateProRata(
            resolution.BaseEntitlementDays, employee.DateOfJoining, leaveYear, fte: employee.Fte,
            fiscalYearStartMonth: fiscalYearStartMonth);

        // BUG-291: credit only the periods that have ELAPSED as of today, keyed on AccrualPeriod so each
        // period credits exactly once. Before this the guard was year-scoped (any Accrual row for the year),
        // so the first run credited 12/12 and every later run was skipped — a Monthly/Quarterly type behaved
        // exactly like Yearly, and the inflated balance was encashed / paid in F&F. Yearly + Upfront resolve
        // to a single period, so their single full-entitlement row is byte-identical to the pre-BUG-291 path.
        var leaveYearStart = LeaveYear.StartOf(leaveYear, fiscalYearStartMonth);
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var (elapsedPeriods, periodsPerYear) =
            AccrualPeriodProgress(leaveType.AccrualFrequency, leaveYearStart, today);
        if (elapsedPeriods == 0)
            return; // The leave year has not started yet for this tenant — nothing accrues.

        // Which periods are already credited. A legacy accrual row (AccrualPeriod == null) predates BUG-291
        // and already credited the FULL year the old way; we must NOT touch existing balances (correcting an
        // over-credited Monthly balance downward is an employee-detriment change gated on a user decision, per
        // DF-65). So if any un-tagged accrual exists for this year, leave it exactly as-is and do nothing.
        var existingPeriods = await _dbContext.LeaveLedgerEntries
            .Where(l =>
                l.EmployeeId == employee.Id &&
                l.LeaveTypeId == leaveType.Id &&
                l.LeaveYear == leaveYear &&
                l.EntryType == LedgerEntryType.Accrual)
            .Select(l => l.AccrualPeriod)
            .ToListAsync(cancellationToken);

        if (existingPeriods.Any(p => p == null))
            return; // Legacy full-year accrual already present — no back-fill, no re-shaping (BUG-291 / DF-65).

        var creditedPeriods = existingPeriods.Where(p => p != null).Select(p => p!.Value).ToHashSet();

        decimal runningBalance = await GetLedgerBalanceAsync(
            employee.Id, leaveType.Id, leaveYear, cancellationToken);

        // Stagger OccurredAt by 1µs per row so that when several elapsed periods are back-filled in one run
        // (first run of a mid-year-started year), the last-credited row deterministically sorts latest for the
        // OccurredAt-DESC running-balance read — mirrors PooledLeaveLedger.PoolRowTickOffset (PG truncates
        // < 1µs). For the single-period case (offset 0) OccurredAt == UtcNow, byte-identical to before.
        var baseOccurredAt = _timeProvider.GetUtcNow().UtcDateTime;
        int written = 0;
        for (int period = 1; period <= elapsedPeriods; period++)
        {
            if (creditedPeriods.Contains(period))
                continue; // Already credited this period — idempotent at period granularity.

            // Per-period amount via cumulative-difference so the rows SUM to proratedAnnual with no rounding
            // drift: cum(p) − cum(p−1), each cum rounded to the ledger's numeric(5,2) scale. cum(periodsPerYear)
            // == proratedAnnual, so a full year of periods credits the exact annual entitlement — never over or
            // under. For Yearly/Upfront (periodsPerYear == 1) this is simply proratedAnnual in one row.
            decimal amount = PeriodAccrualAmount(proratedAnnual, period, periodsPerYear);
            runningBalance += amount;

            _dbContext.LeaveLedgerEntries.Add(new LeaveLedger
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = employee.TenantId,
                EntryType = LedgerEntryType.Accrual,
                EmployeeId = employee.Id,
                LeaveTypeId = leaveType.Id,
                LeaveYear = leaveYear,
                AccrualPeriod = period,
                Amount = amount,
                BalanceAfter = runningBalance,
                Description = periodsPerYear == 1
                    // Byte-identical to the pre-BUG-291 label for Yearly/Upfront.
                    ? $"Annual accrual ({resolution.Source}): {amount} days"
                    : $"{leaveType.AccrualFrequency} accrual ({resolution.Source}) period {period}/{periodsPerYear}: {amount} days",
                OccurredAt = baseOccurredAt.AddTicks(written * PeriodRowTickOffset),
            });
            written++;
        }

        if (written > 0)
            await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>1µs, in ticks — the smallest OccurredAt separation that survives PostgreSQL timestamptz
    /// microsecond truncation (mirrors <c>PooledLeaveLedger.PoolRowTickOffset</c>). Keeps back-filled
    /// per-period accrual rows deterministically ordered for the running-balance read.</summary>
    private const long PeriodRowTickOffset = 10;

    /// <summary>
    /// BUG-291 (crux): how many accrual periods have ELAPSED as of <paramref name="today"/> within the leave
    /// year that starts on <paramref name="leaveYearStart"/>, and how many periods that frequency has per year.
    ///
    /// <para>Monthly = 12 periods (1 month each), Quarterly = 4 (3 months each), Yearly + Upfront = 1 (the
    /// whole year, credited in a single row — byte-identical to the pre-BUG-291 behaviour). A date before the
    /// year start yields 0 elapsed (nothing accrues yet); a date after the year end is capped at the last
    /// period (back-filling a fully-elapsed year credits the full entitlement). Pure + framework-free so this
    /// boundary arithmetic — the part easy to get wrong at the period edges — is unit-testable in isolation,
    /// mirroring <see cref="LeaveYear"/> / <c>LeaveEntitlementEngine</c>.</para>
    /// </summary>
    internal static (int ElapsedPeriods, int PeriodsPerYear) AccrualPeriodProgress(
        AccrualFrequency frequency, DateOnly leaveYearStart, DateOnly today)
    {
        int periodsPerYear = frequency switch
        {
            AccrualFrequency.Monthly => 12,
            AccrualFrequency.Quarterly => 4,
            // Yearly + Upfront both credit the whole entitlement once, at/after the year start.
            _ => 1,
        };

        int monthsSinceStart = (today.Year - leaveYearStart.Year) * 12 + (today.Month - leaveYearStart.Month);
        if (monthsSinceStart < 0)
            return (0, periodsPerYear);          // Leave year not started yet.
        if (monthsSinceStart > 11)
            monthsSinceStart = 11;               // Fully-elapsed (past) year → cap at the final period.

        int monthsPerPeriod = 12 / periodsPerYear;               // 1 / 3 / 12
        int elapsed = (monthsSinceStart / monthsPerPeriod) + 1;  // 1-based
        return (elapsed, periodsPerYear);
    }

    /// <summary>
    /// BUG-291: the amount to credit for accrual <paramref name="period"/> (1-based), computed as the
    /// cumulative-through-<paramref name="period"/> minus cumulative-through-previous, each cumulative rounded
    /// to the ledger's numeric(5,2) scale. This guarantees the per-period rows SUM to exactly
    /// <paramref name="proratedAnnual"/> after a full year (cum(periodsPerYear) == proratedAnnual) with no
    /// rounding over/under-credit, while every individual amount stores exactly in numeric(5,2).
    /// </summary>
    internal static decimal PeriodAccrualAmount(decimal proratedAnnual, int period, int periodsPerYear)
    {
        static decimal Cumulative(decimal annual, int p, int perYear) =>
            Math.Round(annual * p / perYear, 2, MidpointRounding.AwayFromZero);

        return Cumulative(proratedAnnual, period, periodsPerYear)
             - Cumulative(proratedAnnual, period - 1, periodsPerYear);
    }

    private async Task<LeaveEntitlementEngine.EntitlementResolution> ResolveEntitlementAsync(
        Employee employee,
        LeaveType leaveType,
        int leaveYear,
        CancellationToken cancellationToken)
    {
        // 1. Check for per-employee override first (AC-3).
        var employeeOverride = await _dbContext.LeaveEntitlementOverrides
            .AsNoTracking()
            .FirstOrDefaultAsync(o =>
                o.EmployeeId == employee.Id &&
                o.LeaveTypeId == leaveType.Id &&
                o.LeaveYear == leaveYear,
                cancellationToken);

        if (employeeOverride != null)
        {
            return new LeaveEntitlementEngine.EntitlementResolution(
                employeeOverride.EntitlementDays, "override");
        }

        // 2. Fetch matching active rules for this leave type.
        // EffectiveFrom/To are timestamptz, so the reference date MUST be UTC-kinded or Npgsql rejects it.
        // ISSUE-305: the LEAVE year's first day (see above). Still UTC-kinded for the timestamptz compare.
        var referenceDate = LeaveYear.StartOf(leaveYear, await ResolveFiscalYearStartMonthAsync(cancellationToken))
            .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rules = await _dbContext.LeaveEntitlementRules
            .AsNoTracking()
            .Where(r =>
                r.LeaveTypeId == leaveType.Id &&
                r.IsActive &&
                r.EffectiveFrom <= referenceDate &&
                (!r.EffectiveTo.HasValue || r.EffectiveTo.Value >= referenceDate))
            .ToListAsync(cancellationToken);

        var tenureMonths = LeaveEntitlementEngine.ComputeTenureMonths(
            employee.DateOfJoining, referenceDate);

        return LeaveEntitlementEngine.Resolve(
            rules,
            employee.DepartmentId,
            employee.JobTitleId,
            employee.EmploymentType,
            tenureMonths,
            leaveType.AnnualEntitlement);
    }

    private async Task<decimal> GetLedgerBalanceAsync(
        Guid employeeId,
        Guid leaveTypeId,
        int leaveYear,
        CancellationToken cancellationToken)
    {
        // Get the latest ledger entry for the running balance.
        var lastEntry = await _dbContext.LeaveLedgerEntries
            .AsNoTracking()
            .Where(l =>
                l.EmployeeId == employeeId &&
                l.LeaveTypeId == leaveTypeId &&
                l.LeaveYear == leaveYear)
            .OrderByDescending(l => l.OccurredAt)
            .ThenByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return lastEntry?.BalanceAfter ?? 0m;
    }

    private async Task<string?> ValidateRuleReferencesAsync(
        UpsertLeaveEntitlementRuleRequest request,
        CancellationToken cancellationToken,
        Guid? excludeRuleId = null)
    {
        var leaveTypeExists = await _dbContext.LeaveTypes
            .AnyAsync(lt => lt.Id == request.LeaveTypeId, cancellationToken);
        if (!leaveTypeExists)
            return "Leave type not found.";

        if (request.DepartmentId.HasValue)
        {
            var deptExists = await _dbContext.Departments
                .AnyAsync(d => d.Id == request.DepartmentId.Value, cancellationToken);
            if (!deptExists)
                return "Department not found.";
        }

        if (request.JobTitleId.HasValue)
        {
            var jtExists = await _dbContext.JobTitles
                .AnyAsync(j => j.Id == request.JobTitleId.Value, cancellationToken);
            if (!jtExists)
                return "Job title not found.";
        }

        // ISSUE-033 (TC-LV-038 step 4): the job-level dimension is not yet implemented (no JobLevel entity),
        // so a job_level_id can never resolve. Reject any supplied value rather than persisting it unvalidated
        // and inert. When the job-level dimension lands, replace this with a JobLevels FK existence check.
        if (request.JobLevelId.HasValue)
            return "Job level not found.";

        if (request.EmploymentType != null &&
            !Enum.TryParse<EmploymentType>(request.EmploymentType, true, out _))
        {
            return "Invalid employment type. Valid values: FullTime, PartTime, Contract, Intern.";
        }

        if (request.EntitlementDays < 0)
            return "Entitlement days cannot be negative.";

        // ISSUE-033 (TC-LV-038): a single leave type cannot grant more than a full year of days.
        if (request.EntitlementDays > 365)
            return "Entitlement days cannot exceed 365.";

        if (request.TenureMinMonths.HasValue && request.TenureMaxMonths.HasValue &&
            request.TenureMinMonths.Value >= request.TenureMaxMonths.Value)
        {
            return "Tenure min months must be less than tenure max months.";
        }

        if (request.EffectiveTo.HasValue && request.EffectiveTo.Value < request.EffectiveFrom)
            return "Effective to date must be after effective from date.";

        // ISSUE-033 (TC-LV-038 step 11): reject a duplicate rule for the same targeting dimensions, so
        // identical overlapping rules cannot silently pile up. A rule is a duplicate when an ACTIVE,
        // non-deleted rule already targets the same (leaveType + department + jobTitle + jobLevel +
        // employmentType + tenure) combination (excluding the rule being updated).
        var empType = ParseEmploymentType(request.EmploymentType);
        var duplicateExists = await _dbContext.LeaveEntitlementRules
            .AnyAsync(r =>
                r.IsActive && !r.IsDeleted &&
                (excludeRuleId == null || r.Id != excludeRuleId.Value) &&
                r.LeaveTypeId == request.LeaveTypeId &&
                r.DepartmentId == request.DepartmentId &&
                r.JobTitleId == request.JobTitleId &&
                r.JobLevelId == request.JobLevelId &&
                r.EmploymentType == empType &&
                r.TenureMinMonths == request.TenureMinMonths &&
                r.TenureMaxMonths == request.TenureMaxMonths,
                cancellationToken);
        if (duplicateExists)
            return "A leave entitlement rule for this combination already exists.";

        return null; // Valid.
    }

    private async Task<LeaveEntitlementRuleDto> ToRuleDtoAsync(
        LeaveEntitlementRule rule,
        CancellationToken cancellationToken)
    {
        // Load related entities if not already loaded.
        var leaveType = rule.LeaveType
            ?? await _dbContext.LeaveTypes.FindAsync([rule.LeaveTypeId], cancellationToken);
        var department = rule.DepartmentId.HasValue && rule.Department == null
            ? await _dbContext.Departments.FindAsync([rule.DepartmentId.Value], cancellationToken)
            : rule.Department;
        var jobTitle = rule.JobTitleId.HasValue && rule.JobTitle == null
            ? await _dbContext.JobTitles.FindAsync([rule.JobTitleId.Value], cancellationToken)
            : rule.JobTitle;

        return new LeaveEntitlementRuleDto
        {
            Id = rule.Id,
            LeaveTypeId = rule.LeaveTypeId,
            LeaveTypeName = leaveType?.Name ?? string.Empty,
            DepartmentId = rule.DepartmentId,
            DepartmentName = department?.Name,
            JobTitleId = rule.JobTitleId,
            JobTitleName = jobTitle?.TitleName,
            JobLevelId = rule.JobLevelId,
            EmploymentType = rule.EmploymentType?.ToString(),
            TenureMinMonths = rule.TenureMinMonths,
            TenureMaxMonths = rule.TenureMaxMonths,
            EntitlementDays = rule.EntitlementDays,
            Priority = rule.Priority,
            EffectiveFrom = rule.EffectiveFrom,
            EffectiveTo = rule.EffectiveTo,
            IsActive = rule.IsActive,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt,
        };
    }

    private static LeaveEntitlementRuleDto MapRuleToDto(LeaveEntitlementRule rule)
    {
        return new LeaveEntitlementRuleDto
        {
            Id = rule.Id,
            LeaveTypeId = rule.LeaveTypeId,
            LeaveTypeName = rule.LeaveType?.Name ?? string.Empty,
            DepartmentId = rule.DepartmentId,
            DepartmentName = rule.Department?.Name,
            JobTitleId = rule.JobTitleId,
            JobTitleName = rule.JobTitle?.TitleName,
            JobLevelId = rule.JobLevelId,
            EmploymentType = rule.EmploymentType?.ToString(),
            TenureMinMonths = rule.TenureMinMonths,
            TenureMaxMonths = rule.TenureMaxMonths,
            EntitlementDays = rule.EntitlementDays,
            Priority = rule.Priority,
            EffectiveFrom = rule.EffectiveFrom,
            EffectiveTo = rule.EffectiveTo,
            IsActive = rule.IsActive,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt,
        };
    }

    private static EmploymentType? ParseEmploymentType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return Enum.TryParse<EmploymentType>(value, true, out var result) ? result : null;
    }

    // ── BUG-028 (US-LV-002): queryable tenant audit rows (mirror LeaveTypeService.AddLeaveTypeAudit) ──

    private static readonly JsonSerializerOptions AuditJsonOptions = new() { WriteIndented = false };

    /// <summary>
    /// Appends a queryable tenant audit row (with before/after JSON snapshots) for a leave entitlement
    /// rule. Tenant/actor stamped from context. Added to the change tracker and persisted by the caller's
    /// SaveChanges (same transaction as the write). Mirrors <c>LeaveTypeService.AddLeaveTypeAudit</c>.
    /// </summary>
    private void AddRuleAudit(string action, Guid? resourceId, string? before, string? after, string detail)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = action,
            Action = action,
            ResourceType = "LeaveEntitlementRule",
            ResourceId = resourceId?.ToString(),
            Before = before,
            After = after,
            Detail = detail,
            CreatedAt = DateTime.UtcNow,
        });
    }

    /// <summary>Appends a queryable tenant audit row for a leave entitlement override.</summary>
    private void AddOverrideAudit(string action, Guid? resourceId, string? before, string? after, string detail)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = action,
            Action = action,
            ResourceType = "LeaveEntitlementOverride",
            ResourceId = resourceId?.ToString(),
            Before = before,
            After = after,
            Detail = detail,
            CreatedAt = DateTime.UtcNow,
        });
    }

    /// <summary>Serializes the audit-relevant fields of an entitlement rule to a JSON snapshot.</summary>
    private static string SnapshotRule(LeaveEntitlementRule r) => JsonSerializer.Serialize(new
    {
        r.LeaveTypeId,
        r.DepartmentId,
        r.JobTitleId,
        r.JobLevelId,
        EmploymentType = r.EmploymentType?.ToString(),
        r.TenureMinMonths,
        r.TenureMaxMonths,
        r.EntitlementDays,
        r.Priority,
        r.EffectiveFrom,
        r.EffectiveTo,
        r.IsActive,
        r.IsDeleted,
    }, AuditJsonOptions);

    /// <summary>Serializes the audit-relevant fields of an entitlement override to a JSON snapshot.</summary>
    private static string SnapshotOverride(LeaveEntitlementOverride o) => JsonSerializer.Serialize(new
    {
        o.EmployeeId,
        o.LeaveTypeId,
        o.LeaveYear,
        o.EntitlementDays,
        o.Reason,
        o.IsDeleted,
    }, AuditJsonOptions);

    /// <summary>
    /// ISSUE-305: the tenant's leave-year start month, via the shared <see cref="ITenantLeaveYearResolver"/>
    /// (which owns the memo — this sits on the per-employee x leave-type accrual path, so an unmemoized
    /// lookup would cost ~10k extra queries for 1,000 employees x 5 types, daily).
    /// </summary>
    private Task<int> ResolveFiscalYearStartMonthAsync(CancellationToken cancellationToken)
        => _leaveYearResolver.GetStartMonthAsync(cancellationToken);

}
