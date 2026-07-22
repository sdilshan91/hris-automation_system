namespace HRM.Domain.Enums;

/// <summary>
/// The balance "pool" a leave-ledger Used/Adjusted row draws from or restores to (DF-19 / ISSUE-045).
///
/// Deduction follows BR-4 FIFO: a request consumes <see cref="CarryForward"/> days before
/// <see cref="Accrual"/> days. Historically that allocation was DERIVED (never stored), so a
/// cancellation could not restore each pool exactly and expiry re-derived consumption from the
/// Used ledger — which double-counted a later-cancelled request. Tagging each deduction row with
/// its pool makes the allocation PERSISTED, so cancel restores each pool's exact consumption and
/// the expiry job reads the persisted counter (LeaveCarryForwardTracking.ConsumedDays).
/// </summary>
public enum LeavePool
{
    /// <summary>Days drawn from a carry-forward bucket (LeaveCarryForwardTracking). FIFO-first.</summary>
    CarryForward = 0,

    /// <summary>Days drawn from the current-year accrued entitlement.</summary>
    Accrual = 1,
}
