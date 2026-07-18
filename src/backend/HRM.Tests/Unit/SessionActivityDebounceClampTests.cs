// ============================================================================
// US-AUTH-009 / ISSUE-061: the activity-tracking debounce must be clamped BELOW the tenant's
// configured idle timeout, otherwise a very short idle window (the policy validator permits
// idle = 1 minute) is defeated — the fixed 1-minute debounce skips the intermediate requests
// that should advance last_active_at, so a continuously-active session is idle-expired.
// SessionActivityMiddleware.ClampDebounce returns min(configured, idleTimeout / 2).
// ============================================================================

using FluentAssertions;
using HRM.Api.Middleware;

namespace HRM.Tests.Unit;

public sealed class SessionActivityDebounceClampTests
{
    private static readonly TimeSpan Configured = TimeSpan.FromMinutes(1);

    [Fact]
    public void ShortIdleTimeout_ClampsDebounceBelowIt_SoIdleResetIsNotDefeated()
    {
        // idle = 1 min (the minimum the policy validator allows) == the fixed 1-min debounce → the bug case.
        var idleTimeout = TimeSpan.FromMinutes(1);

        var effective = SessionActivityMiddleware.ClampDebounce(Configured, idleTimeoutMinutes: 1);

        // The whole point: the effective debounce is strictly BELOW the idle window, so an active session
        // always advances last_active_at before it can idle-expire.
        effective.Should().BeLessThan(idleTimeout);
        effective.Should().Be(TimeSpan.FromSeconds(30)); // idleTimeout / 2
    }

    [Fact]
    public void IdleTimeoutEqualToTwiceDebounce_KeepsConfiguredDebounce()
    {
        // idle = 2 min → idle/2 = 1 min == configured → configured is retained (boundary).
        var effective = SessionActivityMiddleware.ClampDebounce(Configured, idleTimeoutMinutes: 2);

        effective.Should().Be(Configured);
    }

    [Fact]
    public void LongIdleTimeout_KeepsConfiguredDebounce()
    {
        // idle = 30 min (realistic prod config) → idle/2 = 15 min > 1 min → configured 1 min is kept.
        var effective = SessionActivityMiddleware.ClampDebounce(Configured, idleTimeoutMinutes: 30);

        effective.Should().Be(Configured);
    }

    [Fact]
    public void NonPositiveIdleTimeout_FallsBackToConfigured()
    {
        SessionActivityMiddleware.ClampDebounce(Configured, idleTimeoutMinutes: 0).Should().Be(Configured);
    }
}
