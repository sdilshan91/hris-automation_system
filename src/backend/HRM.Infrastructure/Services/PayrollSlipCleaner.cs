using HRM.Application.Common.Interfaces;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRM.Infrastructure.Services;

/// <summary>
/// ISSUE-154: the single, shared slip-cleanup for a payroll run — extracted verbatim from the former private
/// <c>PayrollRunProcessor.RemoveExistingSlipsAsync</c> so BOTH the re-run path
/// (<see cref="PayrollRunProcessor.ProcessAsync"/>) and the cancel path
/// (<see cref="PayrollRunService.CancelAsync"/>) call ONE implementation of the deletion + adjustment-revert
/// logic (no duplication). Scoped, sharing the request/job's <see cref="AppDbContext"/>, so the removals are
/// committed on the same connection under the same tenant GUC (RLS-safe) as the caller. Tenant-scoped by the
/// EF global query filter.
/// </summary>
public sealed class PayrollSlipCleaner : IPayrollSlipCleaner
{
    private readonly AppDbContext _dbContext;

    public PayrollSlipCleaner(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task RemoveRunSlipsAndRevertAdjustmentsAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        // US-PAY-007 FR-7 consistency: revert adjustments this run had marked Applied back to Pending so a
        // re-run re-picks them (otherwise a re-run would silently drop the adjustment lines) — and a cancel
        // releases them for a future run. Double application across DIFFERENT runs is still prevented: those
        // stay Applied under their own run id.
        var appliedHere = await _dbContext.PayrollAdjustments
            .Where(a => a.AppliedInPayrollRunId == runId && a.Status == AdjustmentStatus.Applied)
            .ToListAsync(cancellationToken);
        foreach (var a in appliedHere)
        {
            a.Status = AdjustmentStatus.Pending;
            a.AppliedInPayrollRunId = null;
            a.UpdatedAt = DateTime.UtcNow;
        }

        var existingSlips = await _dbContext.PayrollSlips.Where(s => s.PayrollRunId == runId).ToListAsync(cancellationToken);
        if (existingSlips.Count == 0)
        {
            if (appliedHere.Count > 0) await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var slipIds = existingSlips.Select(s => s.Id).ToList();
        var existingDetails = await _dbContext.PayrollSlipDetails
            .Where(d => slipIds.Contains(d.PayrollSlipId)).ToListAsync(cancellationToken);

        _dbContext.PayrollSlipDetails.RemoveRange(existingDetails);
        _dbContext.PayrollSlips.RemoveRange(existingSlips);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
