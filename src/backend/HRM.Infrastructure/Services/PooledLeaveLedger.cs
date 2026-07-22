using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRM.Infrastructure.Services;

/// <summary>
/// DF-19 / ISSUE-045: the ONE authoritative FIFO pool split for EVERY leave-balance CONSUMPTION path
/// — leave approval, HR encashment draw-down, compulsory-leave, and LOP-conversion. Consumes the
/// active carry-forward bucket for the leave year BEFORE accrual, writing up to two ledger rows (each
/// tagged with its <see cref="LeavePool"/>; the carry row also with its bucket id) and bumping the
/// bucket's <c>ConsumedDays</c> counter. The year-end expiry job reads that PERSISTED counter (not a
/// derived ledger sum), so unless every consumption path bumps it here the job over-forfeits — this
/// helper is what keeps the counter and the ledger in agreement across all four paths.
///
/// Rows net to −<paramref name="totalDays"/> and chain <c>BalanceAfter</c> from
/// <paramref name="currentBalance"/>; the FINAL row's <c>BalanceAfter</c> equals
/// <c>currentBalance − totalDays</c> — byte-identical in aggregate to a single pre-DF-19 row. Rows are
/// ADDED to the change set but NOT saved (the caller owns the unit of work). A caller that may roll the
/// unit of work back (e.g. encashment when the earning adjustment is rejected) uses the returned
/// <see cref="PooledDeductionResult"/> to detach the rows and revert the in-memory counter bump.
/// </summary>
public static class PooledLeaveLedger
{
    // 1µs (= 10 ticks): the smallest increment that survives PostgreSQL timestamptz (microsecond)
    // truncation, so the second (accrual) row is unambiguously "latest" for the running-balance read
    // (GetLedgerBalanceAsync — latest OccurredAt wins) on both InMemory and real Postgres.
    public const long PoolRowTickOffset = 10;

    public static async Task<PooledDeductionResult> AppendDeductionAsync(
        AppDbContext db,
        Guid tenantId,
        Guid employeeId,
        Guid leaveTypeId,
        int leaveYear,
        decimal totalDays,
        Guid? leaveRequestId,
        decimal currentBalance,
        string description,
        DateTime occurredAt,
        LedgerEntryType entryType,
        CancellationToken cancellationToken)
    {
        // The active carry-forward bucket that landed INTO this leave year is the FIFO source.
        // Tracked load (not AsNoTracking) so the ConsumedDays increment persists in the caller's save.
        var tracking = await db.LeaveCarryForwardTrackings
            .Where(t => t.EmployeeId == employeeId
                        && t.LeaveTypeId == leaveTypeId
                        && t.ToYear == leaveYear
                        && t.Status == CarryForwardTrackingStatus.Active)
            .FirstOrDefaultAsync(cancellationToken);

        decimal remaining = tracking is null
            ? 0m
            : Math.Max(0m, tracking.CarriedDays - tracking.ConsumedDays - tracking.ExpiredDays);

        decimal carriedPortion = Math.Min(totalDays, remaining);
        decimal accrualPortion = totalDays - carriedPortion;

        decimal running = currentBalance;
        LeaveLedger? finalRow = null;
        var rows = new List<LeaveLedger>(2);

        if (carriedPortion > 0m && tracking is not null)
        {
            running -= carriedPortion;
            var carryRow = new LeaveLedger
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                EntryType = entryType,
                EmployeeId = employeeId,
                LeaveTypeId = leaveTypeId,
                LeaveYear = leaveYear,
                LeaveRequestId = leaveRequestId,
                Pool = LeavePool.CarryForward,
                CarryForwardTrackingId = tracking.Id,
                Amount = -carriedPortion,
                BalanceAfter = running,
                Description = $"{description} [carry-forward pool: {carriedPortion} day(s)]",
                OccurredAt = occurredAt,
            };
            db.LeaveLedgerEntries.Add(carryRow);
            rows.Add(carryRow);
            // These carried days are now spoken for. The expiry job reads this counter, so a later
            // cancellation/rollback that decrements it keeps expiry honest. Status stays Active — the
            // expiry sweep flips it to Consumed/Expired.
            tracking.ConsumedDays += carriedPortion;
            finalRow = carryRow;
        }

        // Accrual row: written whenever there are accrual-pool days OR nothing was carried (so a lone
        // row still nets −totalDays and carries the Accrual tag). A lone accrual row keeps the base
        // description + un-offset timestamp — byte-identical to the historical single row.
        if (accrualPortion > 0m || carriedPortion == 0m)
        {
            running -= accrualPortion;
            var accrualRow = new LeaveLedger
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                EntryType = entryType,
                EmployeeId = employeeId,
                LeaveTypeId = leaveTypeId,
                LeaveYear = leaveYear,
                LeaveRequestId = leaveRequestId,
                Pool = LeavePool.Accrual,
                Amount = -accrualPortion,
                BalanceAfter = running,
                Description = carriedPortion > 0m
                    ? $"{description} [accrual pool: {accrualPortion} day(s)]"
                    : description,
                OccurredAt = carriedPortion > 0m ? occurredAt.AddTicks(PoolRowTickOffset) : occurredAt,
            };
            db.LeaveLedgerEntries.Add(accrualRow);
            rows.Add(accrualRow);
            finalRow = accrualRow;
        }

        // finalRow is never null: the accrual branch fires whenever carriedPortion == 0, and the carry
        // branch fires whenever carriedPortion > 0.
        return new PooledDeductionResult(finalRow!, rows, tracking, carriedPortion);
    }
}

/// <summary>
/// The outcome of <see cref="PooledLeaveLedger.AppendDeductionAsync"/>. Most callers use only
/// <see cref="FinalRow"/> (its Id + BalanceAfter). A caller that may roll its unit of work back uses
/// <see cref="Undo"/> to detach the added rows and revert the in-memory <c>ConsumedDays</c> bump before
/// any save has flushed them.
/// </summary>
/// <param name="FinalRow">The last ledger row written — its BalanceAfter equals currentBalance − totalDays.</param>
/// <param name="Rows">Every ledger row added to the change set (one or two).</param>
/// <param name="Tracking">The carry-forward bucket whose ConsumedDays was bumped, or null if none was carried.</param>
/// <param name="CarriedPortion">The days drawn from the carry-forward bucket (0 if none).</param>
public sealed record PooledDeductionResult(
    LeaveLedger FinalRow,
    IReadOnlyList<LeaveLedger> Rows,
    LeaveCarryForwardTracking? Tracking,
    decimal CarriedPortion)
{
    /// <summary>Reverts this deduction on a rolled-back unit of work: detaches the rows and un-bumps the counter.</summary>
    public void Undo(AppDbContext db)
    {
        foreach (var row in Rows)
            db.Entry(row).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        if (Tracking is not null)
            Tracking.ConsumedDays = Math.Max(0m, Tracking.ConsumedDays - CarriedPortion);
    }
}
