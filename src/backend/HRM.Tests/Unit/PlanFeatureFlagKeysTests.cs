// ============================================================================
// D3 (ISSUE-358): PlanFeatureFlagKeys — the shared derivation + fail-open
// predicate every entitlement seam (SCIM route gate, custom-domain resolution
// gate, sandbox provisioning gate) routes through, so their semantics cannot
// drift. These arms pin: (a) Derive maps a plan's flags to the enabled-key set
// and returns the null fail-open sentinel when there is no flags object, and
// (b) IsFeatureEnabled fails OPEN on a null set and is AUTHORITATIVE on a
// non-null one. The Sandbox provisioning seam is inert end-to-end (no sandbox
// concept in ProvisionTenantInput yet), so its gate LOGIC is proven here.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;

namespace HRM.Tests.Unit;

public sealed class PlanFeatureFlagKeysTests
{
    // ── Derive ──────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("TC", "TC-ADM-358")]
    public void Derive_null_flags_returns_null_failopen_sentinel()
    {
        // No flags object (no plan row / unreadable) ⇒ null, which every gate reads as fail-open.
        PlanFeatureFlagKeys.Derive(null).Should().BeNull();
    }

    [Fact]
    [Trait("TC", "TC-ADM-358")]
    public void Derive_all_false_flags_returns_nonnull_empty_authoritative_set()
    {
        // A resolved plan that grants nothing is AUTHORITATIVE-empty (non-null) — NOT the fail-open sentinel.
        // This is the discriminator between "plan says no" (deny) and "couldn't read plan" (allow).
        var derived = PlanFeatureFlagKeys.Derive(new PlanFeatureFlags());

        derived.Should().NotBeNull();
        derived.Should().BeEmpty();
    }

    [Fact]
    [Trait("TC", "TC-ADM-358")]
    public void Derive_maps_each_true_flag_to_its_canonical_key()
    {
        var derived = PlanFeatureFlagKeys.Derive(new PlanFeatureFlags
        {
            Sso = true,
            CustomDomain = true,
            WhiteLabel = false,
            Scim = true,
            Sandbox = true,
        });

        derived.Should().BeEquivalentTo(
            PlanFeatureFlagKeys.Sso,
            PlanFeatureFlagKeys.CustomDomain,
            PlanFeatureFlagKeys.Scim,
            PlanFeatureFlagKeys.Sandbox);
        derived.Should().NotContain(PlanFeatureFlagKeys.WhiteLabel);
    }

    // ── IsFeatureEnabled: fail-open on null ──────────────────────────────────────

    [Theory]
    [Trait("TC", "TC-ADM-358")]
    [InlineData(PlanFeatureFlagKeys.Scim)]
    [InlineData(PlanFeatureFlagKeys.CustomDomain)]
    [InlineData(PlanFeatureFlagKeys.Sandbox)]
    public void IsFeatureEnabled_null_set_fails_open_true(string flag)
    {
        // FAIL-OPEN: a null (unreadable) flag set must never deny — mutating this to `false` (fail-closed) is the
        // arm the mutation guide says MUST die.
        PlanFeatureFlagKeys.IsFeatureEnabled(null, flag).Should().BeTrue();
    }

    // ── IsFeatureEnabled: authoritative on a non-null set ────────────────────────

    [Fact]
    [Trait("TC", "TC-ADM-358")]
    public void IsFeatureEnabled_authoritative_set_grants_only_present_flag()
    {
        var flags = PlanFeatureFlagKeys.Derive(new PlanFeatureFlags { Scim = true });

        PlanFeatureFlagKeys.IsFeatureEnabled(flags, PlanFeatureFlagKeys.Scim).Should().BeTrue();
        PlanFeatureFlagKeys.IsFeatureEnabled(flags, PlanFeatureFlagKeys.CustomDomain).Should().BeFalse();
    }

    [Fact]
    [Trait("TC", "TC-ADM-358")]
    public void IsFeatureEnabled_authoritative_empty_set_denies()
    {
        // Resolved plan, all flags false ⇒ authoritative-empty ⇒ every flag denied. This is the Starter-plan case
        // that MUST deny (distinct from the null fail-open case above).
        var flags = PlanFeatureFlagKeys.Derive(new PlanFeatureFlags());

        PlanFeatureFlagKeys.IsFeatureEnabled(flags, PlanFeatureFlagKeys.Scim).Should().BeFalse();
        PlanFeatureFlagKeys.IsFeatureEnabled(flags, PlanFeatureFlagKeys.CustomDomain).Should().BeFalse();
        PlanFeatureFlagKeys.IsFeatureEnabled(flags, PlanFeatureFlagKeys.Sandbox).Should().BeFalse();
    }

    // ── Sandbox provisioning-seam LOGIC (the seam itself is inert; this proves its predicate) ──

    [Fact]
    [Trait("TC", "TC-ADM-358")]
    public void Sandbox_gate_denies_when_plan_lacks_flag_but_grants_when_present()
    {
        var withoutSandbox = PlanFeatureFlagKeys.Derive(new PlanFeatureFlags { Scim = true }); // Sandbox false
        var withSandbox = PlanFeatureFlagKeys.Derive(new PlanFeatureFlags { Sandbox = true });

        PlanFeatureFlagKeys.IsFeatureEnabled(withoutSandbox, PlanFeatureFlagKeys.Sandbox).Should().BeFalse();
        PlanFeatureFlagKeys.IsFeatureEnabled(withSandbox, PlanFeatureFlagKeys.Sandbox).Should().BeTrue();
    }
}
