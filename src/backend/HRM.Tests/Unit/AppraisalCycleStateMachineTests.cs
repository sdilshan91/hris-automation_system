// ============================================================================
// US-PRF-004 FR-7: AppraisalCycle status state machine — pure domain tests.
// Asserts the legal/illegal transition table directly on AppraisalCycle.IsValidTransition
// (no DB, no service) so the canonical edges are pinned independently of the service flow:
//   - Draft → Active / Cancelled
//   - Active → Paused / Completed / Cancelled
//   - Paused → Active / Cancelled
//   - everything else (incl. any edge out of a terminal state) is illegal.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Enums;
using HRM.Domain.Performance;

namespace HRM.Tests.Unit;

public sealed class AppraisalCycleStateMachineTests
{
    [Theory]
    [InlineData(AppraisalCycleStatus.Draft, AppraisalCycleStatus.Active)]
    [InlineData(AppraisalCycleStatus.Draft, AppraisalCycleStatus.Cancelled)]
    [InlineData(AppraisalCycleStatus.Active, AppraisalCycleStatus.Paused)]
    [InlineData(AppraisalCycleStatus.Active, AppraisalCycleStatus.Completed)]
    [InlineData(AppraisalCycleStatus.Active, AppraisalCycleStatus.Cancelled)]
    [InlineData(AppraisalCycleStatus.Paused, AppraisalCycleStatus.Active)]
    [InlineData(AppraisalCycleStatus.Paused, AppraisalCycleStatus.Cancelled)]
    public void IsValidTransition_LegalEdges_AreAllowed(AppraisalCycleStatus from, AppraisalCycleStatus to)
    {
        AppraisalCycle.IsValidTransition(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData(AppraisalCycleStatus.Draft, AppraisalCycleStatus.Paused)]
    [InlineData(AppraisalCycleStatus.Draft, AppraisalCycleStatus.Completed)]
    [InlineData(AppraisalCycleStatus.Active, AppraisalCycleStatus.Draft)]
    [InlineData(AppraisalCycleStatus.Paused, AppraisalCycleStatus.Completed)] // must resume to Active first
    [InlineData(AppraisalCycleStatus.Completed, AppraisalCycleStatus.Active)] // terminal
    [InlineData(AppraisalCycleStatus.Cancelled, AppraisalCycleStatus.Active)] // terminal
    [InlineData(AppraisalCycleStatus.Cancelled, AppraisalCycleStatus.Draft)]  // terminal
    public void IsValidTransition_IllegalEdges_AreRejected(AppraisalCycleStatus from, AppraisalCycleStatus to)
    {
        AppraisalCycle.IsValidTransition(from, to).Should().BeFalse();
    }

    [Theory]
    [InlineData(AppraisalCycleStatus.Completed)]
    [InlineData(AppraisalCycleStatus.Cancelled)]
    [InlineData(AppraisalCycleStatus.Closed)]
    public void IsTerminal_TrueForTerminalStatuses(AppraisalCycleStatus status)
    {
        new AppraisalCycle { Status = status }.IsTerminal.Should().BeTrue();
    }

    [Theory]
    [InlineData(AppraisalCycleStatus.Draft)]
    [InlineData(AppraisalCycleStatus.Active)]
    [InlineData(AppraisalCycleStatus.Paused)]
    public void IsTerminal_FalseForLiveStatuses(AppraisalCycleStatus status)
    {
        new AppraisalCycle { Status = status }.IsTerminal.Should().BeFalse();
    }
}
