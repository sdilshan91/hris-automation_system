namespace HRM.Domain.Entities;

/// <summary>
/// The kinds of tenant lifecycle transition a System Admin can initiate (US-ADM-004).
/// </summary>
public enum TenantLifecycleTransition
{
    Suspend,
    Terminate,
    Reactivate,
    Restore,
}

/// <summary>
/// Pure, side-effect-free authority on which lifecycle transitions are allowed from a given
/// <see cref="TenantStatus"/> (US-ADM-004 BR-1). Centralizing the matrix here (a) keeps every command honest
/// about the same rules and (b) makes the allowed/blocked combinations unit-testable without a database.
///
/// <para>BR-1 transition matrix:</para>
/// <list type="bullet">
///   <item><b>Suspend</b>: <c>Active|PastDue</c> → <c>Suspended</c>.</item>
///   <item><b>Terminate</b>: <c>Active|PastDue|Suspended</c> → <c>Terminating</c>.</item>
///   <item><b>Reactivate</b>: <c>Suspended</c> → <c>Active|PastDue</c>.</item>
///   <item><b>Restore</b>: <c>Terminating</c> → <c>Suspended|Active</c> (within grace).</item>
/// </list>
/// A <c>Trial</c> tenant is treated like <c>Active</c> for suspend/terminate (it is a live, usable workspace).
/// <c>Terminated</c> is terminal: no transition is allowed out of it (BR-3 — a terminated tenant cannot be
/// restored; recovery is a manual operator process).
/// </summary>
public static class TenantLifecycleTransitions
{
    /// <summary>True iff <paramref name="transition"/> is permitted from <paramref name="from"/> (BR-1/BR-3).</summary>
    public static bool IsAllowed(TenantStatus from, TenantLifecycleTransition transition) => transition switch
    {
        TenantLifecycleTransition.Suspend =>
            from is TenantStatus.Active or TenantStatus.PastDue or TenantStatus.Trial,

        TenantLifecycleTransition.Terminate =>
            from is TenantStatus.Active or TenantStatus.PastDue or TenantStatus.Trial or TenantStatus.Suspended,

        TenantLifecycleTransition.Reactivate =>
            from is TenantStatus.Suspended,

        TenantLifecycleTransition.Restore =>
            from is TenantStatus.Terminating,

        _ => false,
    };
}
