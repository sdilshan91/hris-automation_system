using HRM.Application.Common.Interfaces;
using HRM.Domain.Leave;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRM.Infrastructure.Services;

/// <summary>
/// ISSUE-305: the single reader of <c>Tenant.FiscalYearStartMonth</c>. See
/// <see cref="ITenantLeaveYearResolver"/> for why this is one service rather than a helper copied into each
/// caller.
/// </summary>
public sealed class TenantLeaveYearResolver : ITenantLeaveYearResolver
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Memo, keyed by tenant id. Keyed rather than a bare field because this sits on per-employee×leave-type
    /// sweep paths (accrual would otherwise cost ~10k extra queries for 1,000 employees × 5 types, daily), and
    /// because <see cref="ITenantContext"/> is mutable (<c>SetTenant</c>): the job runners create a scope per
    /// tenant, but this service must not silently depend on that. Keying means a reused scope costs a
    /// redundant query rather than applying one tenant's leave year to another's ledger (Critical Rule #1).
    /// </summary>
    private readonly Dictionary<Guid, int> _startMonthByTenant = new();

    public TenantLeaveYearResolver(AppDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task<int> GetStartMonthAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        if (_startMonthByTenant.TryGetValue(tenantId, out var cached))
            return cached;

        var month = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()   // predicated on this tenant's own id — no cross-tenant read
            .Where(t => t.Id == tenantId)
            .Select(t => (int?)t.FiscalYearStartMonth)
            .FirstOrDefaultAsync(cancellationToken);

        var resolved = month ?? LeaveYear.CalendarStartMonth;
        _startMonthByTenant[tenantId] = resolved;
        return resolved;
    }

    public async Task<int> LabelForAsync(DateOnly date, CancellationToken cancellationToken = default)
        => LeaveYear.LabelFor(date, await GetStartMonthAsync(cancellationToken));
}
