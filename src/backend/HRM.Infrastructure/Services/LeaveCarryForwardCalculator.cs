using HRM.Domain.Entities;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Pure carry-forward / forfeiture / expiry math for US-LV-008 (BR-1, BR-2, BR-3, BR-4, BR-6).
///
/// This class is static and pure — no I/O, no DI — so the year-end job, the monthly expiry job,
/// and the read-only preview API all share one implementation and cannot diverge (the story's
/// "preview must match the job" Test Hint). It mirrors the style of LeaveEntitlementEngine.
/// </summary>
internal static class LeaveCarryForwardCalculator
{
    /// <summary>Outcome of computing carry-forward at year-end for one employee × leave type.</summary>
    internal sealed record CarryForwardOutcome(
        decimal UnusedBalance,
        decimal CarriedDays,
        decimal ForfeitedDays);

    /// <summary>
    /// BR-6: whether carry-forward processing applies to this leave type at all.
    /// Skips leave types where carry-forward does not make sense:
    ///   - CarryForwardLimit is null (no carry-forward configured / "unlimited"-style types),
    ///   - NegativeBalanceAllowed types (e.g. Unpaid Leave — balance is a debit facility, not a
    ///     bucket of earned days to carry),
    ///   - zero-entitlement types (nothing is ever granted, so nothing to carry).
    /// A configured limit of 0 IS applicable (AC-4: all unused is forfeited) — that is handled by
    /// <see cref="Compute"/>, not skipped here.
    /// </summary>
    internal static bool AppliesTo(LeaveType leaveType)
    {
        if (leaveType.CarryForwardLimit is null)
            return false;
        if (leaveType.NegativeBalanceAllowed)
            return false;
        if (leaveType.AnnualEntitlement <= 0m)
            return false;
        return true;
    }

    /// <summary>
    /// Computes the carry-forward and forfeiture for a single employee × leave type at year-end.
    /// BR-1: carried = MIN(unused, limit). BR-2: forfeited = unused - carried (if &gt; 0).
    /// A limit of 0 forfeits everything (AC-4). Negative/zero unused balances carry/forfeit nothing.
    /// </summary>
    /// <param name="unusedBalance">The closing-year balance (entitlement + carry-in − used − expired + adj).</param>
    /// <param name="carryForwardLimit">The leave type's carry_forward_limit (BR-1). Assumed non-null by callers (see <see cref="AppliesTo"/>).</param>
    internal static CarryForwardOutcome Compute(decimal unusedBalance, decimal carryForwardLimit)
    {
        decimal unused = unusedBalance < 0m ? 0m : unusedBalance;
        decimal limit = carryForwardLimit < 0m ? 0m : carryForwardLimit;

        decimal carried = Math.Min(unused, limit);
        decimal forfeited = unused - carried;
        if (forfeited < 0m)
            forfeited = 0m;

        return new CarryForwardOutcome(unused, carried, forfeited);
    }

    /// <summary>
    /// BR-3: the date carried days expire = first day of the new leave year + expiry months.
    /// Null when the leave type configures no expiry (carried days never expire).
    /// </summary>
    internal static DateOnly? ComputeExpiryDate(int toYear, int? carryForwardExpiryMonths)
    {
        if (carryForwardExpiryMonths is null)
            return null;
        var newYearStart = new DateOnly(toYear, 1, 1);
        return newYearStart.AddMonths(carryForwardExpiryMonths.Value);
    }

    /// <summary>
    /// BR-4 (FIFO): how many of the carried-forward days are still unexpired, given how much of the
    /// new year's leave has been consumed against the carried bucket first.
    ///
    /// Carried days are consumed before fresh entitlement, so the first <paramref name="carriedDays"/>
    /// of <paramref name="usedDaysInNewYear"/> come out of the carried bucket. Remaining carried =
    /// carried − used (floored at 0), then less anything already expired.
    /// </summary>
    /// <param name="carriedDays">Days originally carried into the new year.</param>
    /// <param name="usedDaysInNewYear">Total days the employee has used in the new year (FIFO against carried first).</param>
    /// <param name="alreadyExpiredDays">Carried days already expired by a prior expiry-job run (idempotency).</param>
    /// <returns>The carried days that remain unexpired and still available, never negative.</returns>
    internal static decimal RemainingCarriedDays(
        decimal carriedDays, decimal usedDaysInNewYear, decimal alreadyExpiredDays)
    {
        decimal consumedFromCarried = Math.Min(carriedDays, Math.Max(usedDaysInNewYear, 0m));
        decimal remaining = carriedDays - consumedFromCarried - Math.Max(alreadyExpiredDays, 0m);
        return remaining < 0m ? 0m : remaining;
    }
}
