// ============================================================================
// US-ADM-004: pure BR-1 transition-matrix unit tests. No database — exercises
// TenantLifecycleTransitions.IsAllowed for every (status, transition) cell.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Tests.Unit;

public sealed class TenantLifecycleTransitionsTests
{
    // ── Suspend: Active|PastDue|Trial → Suspended ───────────────────────────
    [Theory]
    [InlineData(TenantStatus.Active)]
    [InlineData(TenantStatus.PastDue)]
    [InlineData(TenantStatus.Trial)]
    public void Suspend_AllowedFromLiveStates(TenantStatus from)
        => TenantLifecycleTransitions.IsAllowed(from, TenantLifecycleTransition.Suspend).Should().BeTrue();

    [Theory]
    [InlineData(TenantStatus.Suspended)]
    [InlineData(TenantStatus.Terminating)]
    [InlineData(TenantStatus.Terminated)]
    public void Suspend_BlockedFromNonLiveStates(TenantStatus from)
        => TenantLifecycleTransitions.IsAllowed(from, TenantLifecycleTransition.Suspend).Should().BeFalse();

    // ── Terminate: Active|PastDue|Trial|Suspended → Terminating ─────────────
    [Theory]
    [InlineData(TenantStatus.Active)]
    [InlineData(TenantStatus.PastDue)]
    [InlineData(TenantStatus.Trial)]
    [InlineData(TenantStatus.Suspended)]
    public void Terminate_AllowedFromActiveOrSuspended(TenantStatus from)
        => TenantLifecycleTransitions.IsAllowed(from, TenantLifecycleTransition.Terminate).Should().BeTrue();

    [Theory]
    [InlineData(TenantStatus.Terminating)]
    [InlineData(TenantStatus.Terminated)]
    public void Terminate_BlockedFromTerminatingOrTerminated(TenantStatus from)
        => TenantLifecycleTransitions.IsAllowed(from, TenantLifecycleTransition.Terminate).Should().BeFalse();

    // ── Reactivate: only Suspended ──────────────────────────────────────────
    [Fact]
    public void Reactivate_AllowedOnlyFromSuspended()
    {
        TenantLifecycleTransitions.IsAllowed(TenantStatus.Suspended, TenantLifecycleTransition.Reactivate).Should().BeTrue();
        foreach (var s in new[] { TenantStatus.Active, TenantStatus.PastDue, TenantStatus.Trial, TenantStatus.Terminating, TenantStatus.Terminated })
            TenantLifecycleTransitions.IsAllowed(s, TenantLifecycleTransition.Reactivate).Should().BeFalse();
    }

    // ── Restore: only Terminating (BR-3: never from Terminated) ─────────────
    [Fact]
    public void Restore_AllowedOnlyFromTerminating()
    {
        TenantLifecycleTransitions.IsAllowed(TenantStatus.Terminating, TenantLifecycleTransition.Restore).Should().BeTrue();
        foreach (var s in new[] { TenantStatus.Active, TenantStatus.PastDue, TenantStatus.Trial, TenantStatus.Suspended, TenantStatus.Terminated })
            TenantLifecycleTransitions.IsAllowed(s, TenantLifecycleTransition.Restore).Should().BeFalse();
    }

    [Fact]
    public void Terminated_IsTerminal_NoTransitionAllowed()
    {
        foreach (var t in Enum.GetValues<TenantLifecycleTransition>())
            TenantLifecycleTransitions.IsAllowed(TenantStatus.Terminated, t).Should().BeFalse();
    }
}
