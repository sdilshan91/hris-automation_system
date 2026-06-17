// ============================================================================
// US-ADM-009 (FR-4): pure PlanLimitResolver unit tests.
//
// Exercises the side-effect-free resolution order: a non-expired per-tenant override WINS over the plan field;
// an expired override is ignored (falls back to the plan); an absent override yields the plan value; and a null
// value means UNLIMITED (BR-3) at both the override and plan level.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;

namespace HRM.Tests.Unit;

public sealed class PlanLimitResolverTests
{
    private static readonly DateTime Now = new(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc);

    private static PlanLimitOverride Override(
        string key, long? value, DateTime? expiresAt = null, DateTime? createdAt = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        LimitKey = key,
        Value = value,
        ExpiresAt = expiresAt,
        CreatedAt = createdAt ?? Now.AddDays(-1),
    };

    [Fact]
    public void NoOverrides_UsesPlanValue()
    {
        var result = PlanLimitResolver.Resolve(PlanLimitKeys.MaxEmployees, planValue: 50, overrides: null, nowUtc: Now);

        result.Value.Should().Be(50);
        result.Source.Should().Be(PlanLimitResolver.LimitSource.Plan);
        result.IsUnlimited.Should().BeFalse();
    }

    [Fact]
    public void EmptyOverrides_UsesPlanValue()
    {
        var result = PlanLimitResolver.Resolve(
            PlanLimitKeys.MaxEmployees, planValue: 50, overrides: Array.Empty<PlanLimitOverride>(), nowUtc: Now);

        result.Value.Should().Be(50);
        result.Source.Should().Be(PlanLimitResolver.LimitSource.Plan);
    }

    [Fact]
    public void NonExpiredOverride_WinsOverPlanValue()
    {
        var overrides = new[] { Override(PlanLimitKeys.MaxEmployees, value: 200, expiresAt: Now.AddDays(30)) };

        var result = PlanLimitResolver.Resolve(PlanLimitKeys.MaxEmployees, planValue: 50, overrides, Now);

        result.Value.Should().Be(200);
        result.Source.Should().Be(PlanLimitResolver.LimitSource.Override);
    }

    [Fact]
    public void OverrideWithNoExpiry_WinsOverPlanValue()
    {
        var overrides = new[] { Override(PlanLimitKeys.MaxEmployees, value: 200, expiresAt: null) };

        var result = PlanLimitResolver.Resolve(PlanLimitKeys.MaxEmployees, planValue: 50, overrides, Now);

        result.Value.Should().Be(200);
        result.Source.Should().Be(PlanLimitResolver.LimitSource.Override);
    }

    [Fact]
    public void ExpiredOverride_IsIgnored_FallsBackToPlan()
    {
        var overrides = new[] { Override(PlanLimitKeys.MaxEmployees, value: 200, expiresAt: Now.AddDays(-1)) };

        var result = PlanLimitResolver.Resolve(PlanLimitKeys.MaxEmployees, planValue: 50, overrides, Now);

        result.Value.Should().Be(50);
        result.Source.Should().Be(PlanLimitResolver.LimitSource.Plan);
    }

    [Fact]
    public void OverrideExpiringExactlyNow_IsTreatedAsExpired()
    {
        var overrides = new[] { Override(PlanLimitKeys.MaxEmployees, value: 200, expiresAt: Now) };

        var result = PlanLimitResolver.Resolve(PlanLimitKeys.MaxEmployees, planValue: 50, overrides, Now);

        result.Value.Should().Be(50);
        result.Source.Should().Be(PlanLimitResolver.LimitSource.Plan);
    }

    [Fact]
    public void NullPlanValue_MeansUnlimited()
    {
        var result = PlanLimitResolver.Resolve(PlanLimitKeys.MaxEmployees, planValue: null, overrides: null, nowUtc: Now);

        result.IsUnlimited.Should().BeTrue();
        result.Value.Should().BeNull();
        result.Source.Should().Be(PlanLimitResolver.LimitSource.Plan);
    }

    [Fact]
    public void NullOverrideValue_MeansUnlimited_AndStillWinsOverPlanCap()
    {
        // A present override with a null Value removes the cap for this tenant (BR-3 unlimited override).
        var overrides = new[] { Override(PlanLimitKeys.MaxEmployees, value: null, expiresAt: Now.AddDays(30)) };

        var result = PlanLimitResolver.Resolve(PlanLimitKeys.MaxEmployees, planValue: 50, overrides, Now);

        result.IsUnlimited.Should().BeTrue();
        result.Value.Should().BeNull();
        result.Source.Should().Be(PlanLimitResolver.LimitSource.Override);
    }

    [Fact]
    public void OverrideForDifferentKey_IsIgnored()
    {
        var overrides = new[] { Override(PlanLimitKeys.MaxStorageGb, value: 999, expiresAt: Now.AddDays(30)) };

        var result = PlanLimitResolver.Resolve(PlanLimitKeys.MaxEmployees, planValue: 50, overrides, Now);

        result.Value.Should().Be(50);
        result.Source.Should().Be(PlanLimitResolver.LimitSource.Plan);
    }

    [Fact]
    public void MultipleNonExpiredOverridesForKey_MostRecentlyCreatedWins()
    {
        var overrides = new[]
        {
            Override(PlanLimitKeys.MaxEmployees, value: 100, createdAt: Now.AddDays(-5)),
            Override(PlanLimitKeys.MaxEmployees, value: 300, createdAt: Now.AddDays(-1)),
        };

        var result = PlanLimitResolver.Resolve(PlanLimitKeys.MaxEmployees, planValue: 50, overrides, Now);

        result.Value.Should().Be(300);
        result.Source.Should().Be(PlanLimitResolver.LimitSource.Override);
    }
}
