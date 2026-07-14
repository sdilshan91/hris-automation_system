using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Read/write for the per-tenant, effective-dated Full-and-Final settlement policy (ISSUE-294 Phase 1).
/// Creating a configuration adds a NEW effective-dated version (never mutates history); the settlement engine
/// reads the latest version whose EffectiveFrom ≤ the employee's last working day. Tenant-scoped via
/// ITenantContext + the EF global query filter (the TenantInterceptor stamps TenantId on write).
/// </summary>
public sealed class FnFPolicyService : IFnFPolicyService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<FnFPolicyService> _logger;

    public FnFPolicyService(AppDbContext dbContext, ITenantContext tenantContext, ILogger<FnFPolicyService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<FnFPolicyDto>> CreateAsync(CreateFnFPolicyInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<FnFPolicyDto>.Failure("Tenant context is not resolved.", 400);

        // One version per effective-from date: replace (soft-delete) any existing active version on the same
        // date so editing "today's" policy doesn't accumulate colliding rows the resolver would tie-break on.
        var sameDate = await _dbContext.TenantFnFPolicies
            .Where(p => p.EffectiveFrom == input.EffectiveFrom)
            .ToListAsync(cancellationToken);
        foreach (var p in sameDate)
            p.IsDeleted = true;

        var policy = new TenantFnFPolicy
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            EffectiveFrom = input.EffectiveFrom,
            IncludeProRatedFinalPay = input.IncludeProRatedFinalPay,
            IncludeStatutory = input.IncludeStatutory,
            IncludeLeaveEncashment = input.IncludeLeaveEncashment,
            FinalPeriodOwnedBySettlement = input.FinalPeriodOwnedBySettlement,
            IsActive = input.IsActive,
            IsDeleted = false,
        };
        _dbContext.TenantFnFPolicies.Add(policy);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "F&F policy configured. Id={PolicyId}, EffectiveFrom={EffectiveFrom}, Tenant={TenantId}",
            policy.Id, policy.EffectiveFrom, _tenantContext.TenantId);

        return Result<FnFPolicyDto>.Success(Map(policy));
    }

    public async Task<Result<IReadOnlyList<FnFPolicyDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<FnFPolicyDto>>.Failure("Tenant context is not resolved.", 400);

        var rows = await _dbContext.TenantFnFPolicies.AsNoTracking()
            .OrderByDescending(p => p.EffectiveFrom)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<FnFPolicyDto>>.Success(rows.Select(Map).ToList());
    }

    public async Task<Result<FnFPolicyDto>> GetEffectiveAsync(DateOnly asOf, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<FnFPolicyDto>.Failure("Tenant context is not resolved.", 400);

        var policy = await _dbContext.TenantFnFPolicies.AsNoTracking()
            .Where(p => p.IsActive && p.EffectiveFrom <= asOf)
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

        // No configured version → surface the safe code-default (all includes on) so the effective behaviour is visible.
        return Result<FnFPolicyDto>.Success(policy is null ? CodeDefault(asOf) : Map(policy));
    }

    private static FnFPolicyDto Map(TenantFnFPolicy p) => new()
    {
        Id = p.Id,
        EffectiveFrom = p.EffectiveFrom,
        IncludeProRatedFinalPay = p.IncludeProRatedFinalPay,
        IncludeStatutory = p.IncludeStatutory,
        IncludeLeaveEncashment = p.IncludeLeaveEncashment,
        FinalPeriodOwnedBySettlement = p.FinalPeriodOwnedBySettlement,
        IsActive = p.IsActive,
        IsDefault = false,
        CreatedAt = p.CreatedAt,
    };

    private static FnFPolicyDto CodeDefault(DateOnly asOf) => new()
    {
        Id = Guid.Empty,
        EffectiveFrom = asOf,
        IncludeProRatedFinalPay = true,
        IncludeStatutory = true,
        IncludeLeaveEncashment = true,
        FinalPeriodOwnedBySettlement = true,
        IsActive = true,
        IsDefault = true,
    };
}
