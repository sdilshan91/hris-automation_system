// ============================================================================
// US-PAY-006 FR-6: tax-slab contiguity rule — unit tests.
// Slabs must start at 0 and be contiguous: REJECT gaps AND REJECT overlaps;
// only the highest slab may be unbounded.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Payroll.Validators;

namespace HRM.Tests.Unit;

public sealed class StatutorySlabRulesTests
{
    private static TaxSlabInput Slab(decimal from, decimal? to, decimal rate, int order)
        => new(from, to, rate, order);

    [Fact]
    public void AreContiguous_WellFormedSlabs_IsTrue()
    {
        var slabs = new[]
        {
            Slab(0m, 250_000m, 0m, 0),
            Slab(250_000m, 500_000m, 5m, 1),
            Slab(500_000m, null, 20m, 2),
        };
        StatutorySlabRules.AreContiguous(slabs).Should().BeTrue();
    }

    [Fact]
    public void AreContiguous_WithGap_IsRejected()
    {
        // 250K..300K is uncovered (slab 2 starts at 300K, not 250K).
        var slabs = new[]
        {
            Slab(0m, 250_000m, 0m, 0),
            Slab(300_000m, null, 10m, 1),
        };
        StatutorySlabRules.AreContiguous(slabs).Should().BeFalse();
    }

    [Fact]
    public void AreContiguous_WithOverlap_IsRejected()
    {
        // slab 1 ends at 300K but slab 2 starts at 250K (overlap of 50K).
        var slabs = new[]
        {
            Slab(0m, 300_000m, 0m, 0),
            Slab(250_000m, null, 10m, 1),
        };
        StatutorySlabRules.AreContiguous(slabs).Should().BeFalse();
    }

    [Fact]
    public void AreContiguous_NotStartingAtZero_IsRejected()
        => StatutorySlabRules.AreContiguous(new[] { Slab(100m, null, 5m, 0) }).Should().BeFalse();

    [Fact]
    public void AreContiguous_NonFinalSlabUnbounded_IsRejected()
    {
        var slabs = new[]
        {
            Slab(0m, null, 0m, 0),       // unbounded but not last
            Slab(250_000m, null, 5m, 1),
        };
        StatutorySlabRules.AreContiguous(slabs).Should().BeFalse();
    }

    [Fact]
    public void AreContiguous_InvertedBounds_IsRejected()
        => StatutorySlabRules.AreContiguous(new[] { Slab(0m, 0m, 0m, 0) }).Should().BeFalse();

    [Fact]
    public void AreContiguous_OutOfOrderInput_IsSortedThenAccepted()
    {
        var slabs = new[]
        {
            Slab(500_000m, null, 20m, 2),
            Slab(0m, 250_000m, 0m, 0),
            Slab(250_000m, 500_000m, 5m, 1),
        };
        StatutorySlabRules.AreContiguous(slabs).Should().BeTrue();
    }

    [Fact]
    public void AreContiguous_Empty_IsTrue()
        => StatutorySlabRules.AreContiguous(System.Array.Empty<TaxSlabInput>()).Should().BeTrue();
}
