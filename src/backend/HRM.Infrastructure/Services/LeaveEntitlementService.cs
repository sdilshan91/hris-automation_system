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
    private readonly ILogger<LeaveEntitlementService> _logger;

    /// <summary>
    /// Batch size for accrual processing (NFR-1: handle 5,000 employees).
    /// </summary>
    private const int AccrualBatchSize = 500;

    public LeaveEntitlementService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<LeaveEntitlementService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
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

        var validationResult = await ValidateRuleReferencesAsync(request, cancellationToken);
        if (validationResult != null)
            return Result<LeaveEntitlementRuleDto>.Failure(validationResult, 400);

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

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated leave entitlement rule {RuleId} in tenant {TenantId}",
            rule.Id, _tenantContext.TenantId);

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

        rule.IsDeleted = true;
        rule.IsActive = false;
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

        if (existing != null)
        {
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

        entity.IsDeleted = true;
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

        // Pro-rata for mid-year joiners (FR-3, AC-4).
        // TODO(part-time): When FTE field exists, use employee.Fte instead of 1.0m.
        decimal prorated = LeaveEntitlementEngine.CalculateProRata(
            resolution.BaseEntitlementDays, employee.DateOfJoining, leaveYear, fte: 1.0m);

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

        decimal prorated = LeaveEntitlementEngine.CalculateProRata(
            resolution.BaseEntitlementDays, employee.DateOfJoining, leaveYear, fte: 1.0m);

        // Check if an accrual entry already exists for this employee/leave type/year.
        var existingAccrual = await _dbContext.LeaveLedgerEntries
            .AnyAsync(l =>
                l.EmployeeId == employee.Id &&
                l.LeaveTypeId == leaveType.Id &&
                l.LeaveYear == leaveYear &&
                l.EntryType == LedgerEntryType.Accrual,
                cancellationToken);

        if (existingAccrual)
        {
            // Already accrued for this period — skip to avoid double-crediting.
            return;
        }

        // Get current balance.
        decimal currentBalance = await GetLedgerBalanceAsync(
            employee.Id, leaveType.Id, leaveYear, cancellationToken);

        decimal newBalance = currentBalance + prorated;

        var ledgerEntry = new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = employee.TenantId,
            EntryType = LedgerEntryType.Accrual,
            EmployeeId = employee.Id,
            LeaveTypeId = leaveType.Id,
            LeaveYear = leaveYear,
            Amount = prorated,
            BalanceAfter = newBalance,
            Description = $"Annual accrual ({resolution.Source}): {prorated} days",
            OccurredAt = DateTime.UtcNow,
        };

        _dbContext.LeaveLedgerEntries.Add(ledgerEntry);
        await _dbContext.SaveChangesAsync(cancellationToken);
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
        var referenceDate = new DateTime(leaveYear, 1, 1);
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
        CancellationToken cancellationToken)
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

        if (request.EmploymentType != null &&
            !Enum.TryParse<EmploymentType>(request.EmploymentType, true, out _))
        {
            return "Invalid employment type. Valid values: FullTime, PartTime, Contract, Intern.";
        }

        if (request.EntitlementDays < 0)
            return "Entitlement days cannot be negative.";

        if (request.TenureMinMonths.HasValue && request.TenureMaxMonths.HasValue &&
            request.TenureMinMonths.Value >= request.TenureMaxMonths.Value)
        {
            return "Tenure min months must be less than tenure max months.";
        }

        if (request.EffectiveTo.HasValue && request.EffectiveTo.Value < request.EffectiveFrom)
            return "Effective to date must be after effective from date.";

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
}
