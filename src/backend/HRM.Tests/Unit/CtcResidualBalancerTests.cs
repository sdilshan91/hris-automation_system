// ============================================================================
// BUG-070: CtcResidualBalancer.Balance — pure residual-to-one-component CTC math.
// TC-PAY-070-01..09. These are exact-decimal arithmetic tests on real
// CtcComponentResult inputs. Every happy-path assertion pins the concrete
// residual amount AND the tie-out invariant  |sum(earnings) − annualCtc| ≤ tolerance,
// not merely Success == true. Each guard is exercised on inputs that pass every
// *earlier* guard, so the assertion isolates the one rule under test.
//
// Residual selection (from the SUT): an Earning whose code contains "SPECIAL" or
// equals "SPL"/"SA" (case-insensitive) wins; else the "BASIC" Earning; else none.
// Amounts round to 2dp AwayFromZero; MonthlyAmount = Round(AnnualAmount / 12).
// ============================================================================

using FluentAssertions;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;

namespace HRM.Tests.Unit;

public sealed class CtcResidualBalancerTests
{
    private const decimal Tolerance = 1m;

    // Stable ids so the "overridden" set can name specific components.
    private static readonly Guid BasicId = Guid.NewGuid();
    private static readonly Guid HraId = Guid.NewGuid();
    private static readonly Guid SpecialId = Guid.NewGuid();
    private static readonly Guid PfId = Guid.NewGuid();

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static CtcComponentResult Earning(Guid id, string code, decimal annual) =>
        new(id, code, SalaryComponentType.Earning, annual, Round(annual / 12m), false);

    private static CtcComponentResult Deduction(Guid id, string code, decimal annual) =>
        new(id, code, SalaryComponentType.Deduction, annual, Round(annual / 12m), false);

    private static decimal SumEarnings(IReadOnlyList<CtcComponentResult> comps) =>
        comps.Where(c => c.Type == SalaryComponentType.Earning).Sum(c => c.AnnualAmount);

    private static IReadOnlySet<Guid> Overridden(params Guid[] ids) => ids.ToHashSet();

    private static CtcComponentResult Line(IReadOnlyList<CtcComponentResult> comps, Guid id) =>
        comps.Single(c => c.ComponentId == id);

    // ── Happy path: override UP, SPECIAL residual absorbs the delta ──────────
    // BASIC 240,000 + HRA(overridden) 60,000 + SPECIAL 312,000 = 612,000 vs CTC 600,000.
    // delta = −12,000 → SPECIAL 312,000 − 12,000 = 300,000 (monthly 25,000). Ties to 600,000.
    // A PF deduction of 20,000 is present to prove non-earnings are neither summed nor chosen as residual.
    [Fact]
    public void Balance_OverrideUp_SpecialResidualAbsorbs_BUG070()
    {
        var resolved = new[]
        {
            Earning(BasicId, "BASIC", 240_000m),
            Earning(HraId, "HRA", 60_000m),        // overridden verbatim (was 48,000)
            Earning(SpecialId, "SPECIAL", 312_000m),
            Deduction(PfId, "PF", 20_000m),         // must be ignored by the balancer
        };

        var result = CtcResidualBalancer.Balance(resolved, 600_000m, Overridden(HraId), Tolerance);

        result.Success.Should().BeTrue();

        var special = Line(result.Components, SpecialId);
        special.AnnualAmount.Should().Be(300_000m);         // 312,000 − 12,000 delta
        special.MonthlyAmount.Should().Be(25_000m);         // 300,000 / 12

        // Only the residual moved: BASIC + the overridden HRA are byte-for-byte unchanged; PF untouched.
        Line(result.Components, BasicId).AnnualAmount.Should().Be(240_000m);
        Line(result.Components, HraId).AnnualAmount.Should().Be(60_000m);
        Line(result.Components, PfId).AnnualAmount.Should().Be(20_000m);

        // Tie-out invariant (earnings only): |sum − CTC| ≤ tolerance, and here it is EXACT.
        SumEarnings(result.Components).Should().Be(600_000m);
        Math.Abs(SumEarnings(result.Components) - 600_000m).Should().BeLessThanOrEqualTo(Tolerance);
    }

    // ── Override DOWN: residual grows by the delta ──────────────────────────
    // BASIC 240,000 + HRA(overridden) 30,000 + SPECIAL 312,000 = 582,000 vs 600,000.
    // delta = +18,000 → SPECIAL 330,000 (monthly 27,500). Ties to 600,000.
    [Fact]
    public void Balance_OverrideDown_ResidualGrows_BUG070()
    {
        var resolved = new[]
        {
            Earning(BasicId, "BASIC", 240_000m),
            Earning(HraId, "HRA", 30_000m),
            Earning(SpecialId, "SPECIAL", 312_000m),
        };

        var result = CtcResidualBalancer.Balance(resolved, 600_000m, Overridden(HraId), Tolerance);

        result.Success.Should().BeTrue();
        Line(result.Components, SpecialId).AnnualAmount.Should().Be(330_000m);
        Line(result.Components, SpecialId).MonthlyAmount.Should().Be(27_500m);
        SumEarnings(result.Components).Should().Be(600_000m);
    }

    // ── Multiple overrides: residual absorbs the COMBINED delta ─────────────
    // BASIC(overridden) 250,000 + HRA(overridden) 60,000 + SPECIAL 312,000 = 622,000 vs 600,000.
    // delta = −22,000 → SPECIAL 290,000 (monthly 24,166.67 — 290,000/12 rounds AwayFromZero). Ties to 600,000.
    [Fact]
    public void Balance_MultipleOverrides_ResidualAbsorbsCombinedDelta_BUG070()
    {
        var resolved = new[]
        {
            Earning(BasicId, "BASIC", 250_000m),
            Earning(HraId, "HRA", 60_000m),
            Earning(SpecialId, "SPECIAL", 312_000m),
        };

        var result = CtcResidualBalancer.Balance(resolved, 600_000m, Overridden(BasicId, HraId), Tolerance);

        result.Success.Should().BeTrue();
        Line(result.Components, SpecialId).AnnualAmount.Should().Be(290_000m);
        Line(result.Components, SpecialId).MonthlyAmount.Should().Be(24_166.67m);
        // Overridden lines are preserved verbatim.
        Line(result.Components, BasicId).AnnualAmount.Should().Be(250_000m);
        Line(result.Components, HraId).AnnualAmount.Should().Be(60_000m);
        SumEarnings(result.Components).Should().Be(600_000m);
    }

    // ── BASIC-as-residual fallback: no SPECIAL present ──────────────────────
    // BASIC 300,000 + HRA(overridden) 250,000 = 550,000 vs 500,000. delta = −50,000.
    // No SPECIAL → BASIC is the residual → 250,000 (monthly 20,833.33). Ties to 500,000.
    [Fact]
    public void Balance_NoSpecial_BasicIsResidual_BUG070()
    {
        var resolved = new[]
        {
            Earning(BasicId, "BASIC", 300_000m),
            Earning(HraId, "HRA", 250_000m),
        };

        var result = CtcResidualBalancer.Balance(resolved, 500_000m, Overridden(HraId), Tolerance);

        result.Success.Should().BeTrue();
        Line(result.Components, BasicId).AnnualAmount.Should().Be(250_000m);
        Line(result.Components, BasicId).MonthlyAmount.Should().Be(20_833.33m);
        SumEarnings(result.Components).Should().Be(500_000m);
    }

    // ── Guard: residual_would_be_negative (no partial mutation) ─────────────
    // BASIC(overridden) 250,000 + SPECIAL 50,000 = 300,000 vs 150,000. delta = −150,000.
    // SPECIAL would go to −100,000 (< floor 0). SPECIAL is NOT overridden, so this isolates the
    // negative-floor guard specifically. Fail must carry no components (no partial mutation).
    [Fact]
    public void Balance_ResidualNegative_Rejected_BUG070()
    {
        var resolved = new[]
        {
            Earning(BasicId, "BASIC", 250_000m),
            Earning(SpecialId, "SPECIAL", 50_000m),
        };

        var result = CtcResidualBalancer.Balance(resolved, 150_000m, Overridden(BasicId), Tolerance);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("residual_would_be_negative");
        result.Components.Should().BeEmpty();   // fail-closed: no half-balanced list leaks out
    }

    // ── Guard: residual_component_overridden ────────────────────────────────
    // SPECIAL itself is overridden. delta = −8,000 (> tolerance) so selection is reached; the residual
    // (SPECIAL) is in the overridden set → reject. You cannot both fix and balance the same line.
    [Fact]
    public void Balance_ResidualOverridden_Rejected_BUG070()
    {
        var resolved = new[]
        {
            Earning(BasicId, "BASIC", 240_000m),
            Earning(HraId, "HRA", 48_000m),
            Earning(SpecialId, "SPECIAL", 320_000m),
        };

        var result = CtcResidualBalancer.Balance(resolved, 600_000m, Overridden(SpecialId), Tolerance);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("residual_component_overridden");
        result.Components.Should().BeEmpty();
    }

    // ── Guard: no_residual_component ────────────────────────────────────────
    // Neither a SPECIAL* earning nor a BASIC earning exists. delta = +50,000 (> tolerance) so selection
    // runs and finds nothing to absorb into → reject.
    [Fact]
    public void Balance_NoResidualComponent_Rejected_BUG070()
    {
        var resolved = new[]
        {
            Earning(HraId, "HRA", 250_000m),
            Earning(Guid.NewGuid(), "CONVEYANCE", 100_000m),
        };

        var result = CtcResidualBalancer.Balance(resolved, 400_000m, Overridden(HraId), Tolerance);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("no_residual_component");
        result.Components.Should().BeEmpty();
    }

    // ── No-op within tolerance: already balanced → input returned unchanged ──
    // sum(earnings) 600,000; CTC 600,000.50 → delta 0.50 ≤ 1 → returns the SAME list, Success.
    // This is why a formula-driven SPECIAL that self-absorbs needs no balancing.
    [Fact]
    public void Balance_WithinTolerance_NoOp_ReturnsInputUnchanged_BUG070()
    {
        var resolved = new[]
        {
            Earning(BasicId, "BASIC", 240_000m),
            Earning(HraId, "HRA", 48_000m),
            Earning(SpecialId, "SPECIAL", 312_000m),
        };

        var result = CtcResidualBalancer.Balance(resolved, 600_000.50m, Overridden(HraId), Tolerance);

        result.Success.Should().BeTrue();
        result.Components.Should().BeSameAs(resolved);   // no allocation, no mutation on the no-op path
        Line(result.Components, SpecialId).AnnualAmount.Should().Be(312_000m);
    }

    // ── Rounding: residual monthly is Round(annual/12) AwayFromZero, no cent drift ──
    // BASIC 240,000 + HRA(overridden) 48,100 + SPECIAL 312,000 = 600,100 vs 600,000. delta = −100.
    // SPECIAL → 311,900; monthly 311,900/12 = 25,991.666… → 25,991.67 (AwayFromZero). Annual ties EXACTLY.
    [Fact]
    public void Balance_ResidualMonthly_RoundsAwayFromZero_BUG070()
    {
        var resolved = new[]
        {
            Earning(BasicId, "BASIC", 240_000m),
            Earning(HraId, "HRA", 48_100m),
            Earning(SpecialId, "SPECIAL", 312_000m),
        };

        var result = CtcResidualBalancer.Balance(resolved, 600_000m, Overridden(HraId), Tolerance);

        result.Success.Should().BeTrue();
        var special = Line(result.Components, SpecialId);
        special.AnnualAmount.Should().Be(311_900m);
        special.MonthlyAmount.Should().Be(25_991.67m);            // rounded up (away from zero)
        SumEarnings(result.Components).Should().Be(600_000m);      // annual tie-out is exact — no cent drift
        Math.Abs(SumEarnings(result.Components) - 600_000m).Should().BeLessThanOrEqualTo(Tolerance);
    }

    // ── Residual selection is case-insensitive on the SPECIAL alias (SA/SPL/special) ──
    // Confirms "sa" (lower) is picked as the residual over BASIC. delta = −10,000 → SA 90,000.
    [Fact]
    public void Balance_SpecialAliasCaseInsensitive_PickedOverBasic_BUG070()
    {
        var saId = Guid.NewGuid();
        var resolved = new[]
        {
            Earning(BasicId, "basic", 240_000m),
            Earning(HraId, "HRA", 60_000m),      // overridden up by 10,000
            Earning(saId, "sa", 100_000m),        // lowercase alias — must be the residual
        };

        var result = CtcResidualBalancer.Balance(resolved, 390_000m, Overridden(HraId), Tolerance);

        result.Success.Should().BeTrue();
        Line(result.Components, saId).AnnualAmount.Should().Be(90_000m);   // 100,000 − 10,000
        Line(result.Components, BasicId).AnnualAmount.Should().Be(240_000m); // BASIC untouched — SA won selection
        SumEarnings(result.Components).Should().Be(390_000m);
    }
}
