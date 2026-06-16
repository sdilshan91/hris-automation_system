using HRM.Application.Common.Interfaces;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Resolves the Pending payroll adjustments in effect for a payroll period (US-PAY-007 FR-3) and marks the
/// included ones Applied after the run persists slips (FR-4). The single seam the US-PAY-003
/// <c>PayrollRunProcessor</c> calls — wired ADDITIVELY (mirrors <see cref="StatutoryDeductionResolver"/>):
/// when no adjustments exist for the period the period map is empty and the processor behaves exactly as
/// before, so every existing US-PAY-003/006 test stays green. All reads/writes are tenant-scoped via the EF
/// global query filter (AC-5).
/// </summary>
public sealed class PayrollAdjustmentResolver : IPayrollAdjustmentResolver
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public PayrollAdjustmentResolver(AppDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyDictionary<Guid, EmployeeAdjustments>> ResolveForPeriodAsync(
        int payYear, int payMonth, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return new Dictionary<Guid, EmployeeAdjustments>();

        // FR-3: all Pending adjustments for the period, in one query (no N+1). Empty when none configured →
        // the processor's per-employee lookup misses and it behaves exactly as the pre-US-PAY-007 engine.
        var pending = await _dbContext.PayrollAdjustments.AsNoTracking()
            .Where(a => a.ApplicablePayYear == payYear
                && a.ApplicablePayMonth == payMonth
                && a.Status == AdjustmentStatus.Pending)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
            return new Dictionary<Guid, EmployeeAdjustments>();

        return pending
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => new EmployeeAdjustments(g
                    .Select(a => new ResolvedAdjustment(
                        a.Id, a.AdjustmentType, a.Amount, a.Description, a.IsTaxable, a.ReferencePayrollSlipId))
                    .ToList()));
    }

    public async Task MarkAppliedAsync(
        IReadOnlyCollection<Guid> adjustmentIds, Guid payrollRunId, CancellationToken cancellationToken = default)
    {
        if (adjustmentIds.Count == 0 || !_tenantContext.IsResolved)
            return;

        // FR-4: flip Pending → Applied with the run id, preventing double application on a re-run. Idempotent —
        // only Pending rows are touched. Tracked entities are saved by the processor in its own SaveChanges.
        var ids = adjustmentIds.ToHashSet();
        var rows = await _dbContext.PayrollAdjustments
            .Where(a => ids.Contains(a.Id) && a.Status == AdjustmentStatus.Pending)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.Status = AdjustmentStatus.Applied;
            row.AppliedInPayrollRunId = payrollRunId;
            row.UpdatedAt = now;
        }
    }
}
