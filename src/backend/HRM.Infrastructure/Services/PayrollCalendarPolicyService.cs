using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Read/write for the per-tenant, effective-dated payroll calendar policy (US-ATT-011 AC-4 / CAL-5).
/// Creating a configuration adds a NEW effective-dated version (never mutates history); the payroll engines read
/// the latest version whose EffectiveFrom ≤ the pay-period date, via the SAME
/// <see cref="PayrollCalendarResolver.ResolvePolicyAsync"/> this service's GetEffective uses — so what the API
/// reports as effective is exactly what the run applies. Tenant-scoped via ITenantContext + the EF global query
/// filter (the TenantInterceptor stamps TenantId on write).
/// </summary>
public sealed class PayrollCalendarPolicyService : IPayrollCalendarPolicyService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<PayrollCalendarPolicyService> _logger;

    public PayrollCalendarPolicyService(
        AppDbContext dbContext, ITenantContext tenantContext, ILogger<PayrollCalendarPolicyService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<PayrollCalendarPolicyDto>> CreateAsync(
        CreatePayrollCalendarPolicyInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollCalendarPolicyDto>.Failure("Tenant context is not resolved.", 400);

        // One version per effective-from date: replace (soft-delete) any existing version on the same date so
        // re-configuring "today's" policy doesn't accumulate colliding rows the resolver would tie-break on
        // non-deterministically (the F&F rule). Money-critical: an ambiguous tie-break here is an ambiguous
        // OT base.
        var sameDate = await _dbContext.TenantPayrollCalendarPolicies
            .Where(p => p.EffectiveFrom == input.EffectiveFrom)
            .ToListAsync(cancellationToken);
        foreach (var p in sameDate)
            p.IsDeleted = true;

        var policy = new TenantPayrollCalendarPolicy
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            EffectiveFrom = input.EffectiveFrom,
            ExcludeHolidaysFromWorkingDays = input.ExcludeHolidaysFromWorkingDays,
            IsActive = input.IsActive,
            IsDeleted = false,
        };
        _dbContext.TenantPayrollCalendarPolicies.Add(policy);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Payroll calendar policy configured. Id={PolicyId}, EffectiveFrom={EffectiveFrom}, ExcludeHolidays={ExcludeHolidays}, Tenant={TenantId}",
            policy.Id, policy.EffectiveFrom, policy.ExcludeHolidaysFromWorkingDays, _tenantContext.TenantId);

        return Result<PayrollCalendarPolicyDto>.Success(Map(policy));
    }

    public async Task<Result<IReadOnlyList<PayrollCalendarPolicyDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<PayrollCalendarPolicyDto>>.Failure("Tenant context is not resolved.", 400);

        var rows = await _dbContext.TenantPayrollCalendarPolicies.AsNoTracking()
            .OrderByDescending(p => p.EffectiveFrom)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<PayrollCalendarPolicyDto>>.Success(rows.Select(Map).ToList());
    }

    public async Task<Result<PayrollCalendarPolicyDto>> GetEffectiveAsync(
        DateOnly asOf, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollCalendarPolicyDto>.Failure("Tenant context is not resolved.", 400);

        // The SAME resolution the engines use — this endpoint cannot report an "effective" policy the run
        // wouldn't actually apply.
        var policy = await PayrollCalendarResolver.ResolvePolicyAsync(_dbContext, asOf, cancellationToken);

        // No configured version → surface the code-default (holidays NOT excluded) so the effective behaviour
        // is visible rather than implied.
        return Result<PayrollCalendarPolicyDto>.Success(policy is null ? CodeDefault(asOf) : Map(policy));
    }

    private static PayrollCalendarPolicyDto Map(TenantPayrollCalendarPolicy p) => new()
    {
        Id = p.Id,
        EffectiveFrom = p.EffectiveFrom,
        ExcludeHolidaysFromWorkingDays = p.ExcludeHolidaysFromWorkingDays,
        IsActive = p.IsActive,
        IsDefault = false,
        CreatedAt = p.CreatedAt,
    };

    private static PayrollCalendarPolicyDto CodeDefault(DateOnly asOf) => new()
    {
        Id = Guid.Empty,
        EffectiveFrom = asOf,
        ExcludeHolidaysFromWorkingDays = false,   // money-critical code-default — see the entity remarks.
        IsActive = true,
        IsDefault = true,
    };
}
