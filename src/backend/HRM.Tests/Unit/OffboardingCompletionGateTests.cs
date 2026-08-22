// ============================================================================
// US-ONB-005 AC-5 / BR-2 — the completion rule, tested at its own boundary.
//
// OffboardingCompletionGate is a public static over a plain list of task entities. Every other arm reaches
// it through OffboardingService, which loads tasks via EF — and EF's global query filter
// (AppDbContext.cs:285) already drops soft-deleted rows before the gate ever sees them. That makes the
// gate's own `!t.IsDeleted` clause unobservable from the service, which mutation testing proved: deleting
// it left the entire service suite green.
//
// The gate is callable by anyone with any list (including one fetched with IgnoreQueryFilters), so its
// correctness must not depend on how that list was obtained. These arms hand it the states directly.
// ============================================================================

using FluentAssertions;
using HRM.Application.Features.Onboarding;
using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Tests.Unit;

public sealed class OffboardingCompletionGateTests
{
    private static OffboardingTaskInstance Task(
        bool mandatory = true,
        OnboardingTaskStatus status = OnboardingTaskStatus.Pending,
        ClearanceStatus? clearance = null,
        bool deleted = false,
        int sortOrder = 0,
        string title = "Task") =>
        new()
        {
            Id = Guid.NewGuid(),
            Title = title,
            IsMandatory = mandatory,
            Status = status,
            ClearanceStatus = clearance,
            IsDeleted = deleted,
            SortOrder = sortOrder,
            ClearanceCategory = ClearanceCategory.IT,
        };

    /// <summary>
    /// THE CLAUSE THE SERVICE SUITE CANNOT SEE. A soft-deleted task handed to the gate directly must be
    /// ignored; without this, a caller that bypasses the query filter blocks on a task nobody can complete.
    /// </summary>
    [Fact]
    public void A_soft_deleted_mandatory_task_is_ignored_even_when_it_reaches_the_gate()
    {
        var deleted = Task(deleted: true, title: "Removed");

        // Guardian: the very same task, undeleted, DOES block — so this is not green because the gate
        // ignores everything.
        OffboardingCompletionGate.PendingMandatoryItems([Task(title: "Removed")])
            .Should().ContainSingle("an undeleted mandatory Pending task blocks");

        OffboardingCompletionGate.PendingMandatoryItems([deleted]).Should().BeEmpty();
    }

    [Fact]
    public void An_optional_task_never_blocks_however_bad_its_state()
    {
        var optional = Task(mandatory: false, clearance: ClearanceStatus.PendingIssues);

        OffboardingCompletionGate.PendingMandatoryItems([optional]).Should().BeEmpty();
    }

    [Fact]
    public void A_completed_mandatory_task_with_a_refused_clearance_blocks_as_clearance_not_approved()
    {
        var refused = Task(
            status: OnboardingTaskStatus.Completed,
            clearance: ClearanceStatus.PendingIssues);

        var pending = OffboardingCompletionGate.PendingMandatoryItems([refused]);

        pending.Should().ContainSingle()
            .Which.Reason.Should().Be(OffboardingCompletionGate.ReasonClearanceNotApproved);
    }

    /// <summary>
    /// An UNDECIDED clearance on an otherwise-finished mandatory task does not block. Not every mandatory
    /// task is a clearance; demanding a verdict on ones that never receive a decision would deadlock every
    /// offboarding.
    /// </summary>
    [Fact]
    public void A_completed_mandatory_task_with_no_clearance_decision_does_not_block()
    {
        var done = Task(status: OnboardingTaskStatus.Completed, clearance: null);

        OffboardingCompletionGate.PendingMandatoryItems([done]).Should().BeEmpty();
    }

    [Fact]
    public void An_unfinished_mandatory_task_blocks_as_not_completed()
    {
        var pending = OffboardingCompletionGate.PendingMandatoryItems(
            [Task(status: OnboardingTaskStatus.InProgress)]);

        pending.Should().ContainSingle()
            .Which.Reason.Should().Be(OffboardingCompletionGate.ReasonNotCompleted);
    }

    /// <summary>
    /// <see cref="OnboardingTaskStatus.Skipped"/> is not <see cref="OnboardingTaskStatus.Completed"/>, so a
    /// skipped MANDATORY task still blocks. Recorded explicitly because "skipped" reads like a resolution
    /// and a future reader might assume it clears the item.
    /// </summary>
    [Fact]
    public void A_skipped_mandatory_task_still_blocks()
    {
        OffboardingCompletionGate.PendingMandatoryItems([Task(status: OnboardingTaskStatus.Skipped)])
            .Should().ContainSingle();
    }

    [Fact]
    public void Blocking_items_are_returned_in_sort_order()
    {
        var third = Task(sortOrder: 30, title: "Third");
        var first = Task(sortOrder: 10, title: "First");
        var second = Task(sortOrder: 20, title: "Second");

        var pending = OffboardingCompletionGate.PendingMandatoryItems([third, first, second]);

        pending.Select(i => i.Title).Should().Equal("First", "Second", "Third");
    }
}
