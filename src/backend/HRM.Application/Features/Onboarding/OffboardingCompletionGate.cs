using HRM.Application.Features.Onboarding.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Application.Features.Onboarding;

/// <summary>
/// US-ONB-005 AC-5 / BR-2 — <b>the</b> definition of what blocks an offboarding from completing.
///
/// <para>
/// This exists because the rule used to be written twice: once in
/// <c>OffboardingService.CompleteOffboardingAsync</c> (which enforces it) and once in the Angular
/// <c>pendingMandatoryTitles()</c> helper (which predicts it so the dashboard can disable the button and
/// explain why). Nothing checked that the two agreed — and they did not. The frontend copy tested a task's
/// clearance against <c>'cleared'</c>, a token that only ever appears at <i>department</i> level; a task
/// carries <c>"approved"</c>, <c>"pending_issues"</c> or null. The comparison could therefore never be true,
/// so every mandatory task counted as blocking forever and the Complete button was permanently disabled.
/// </para>
///
/// <para>
/// The fix is not to re-write the prediction more carefully in the client. Two hand-written descriptions of
/// one rule drift again the moment the rule changes, and the client's copy is the one nobody runs against a
/// database. The rule now lives here, the enforcement path and the projected
/// <see cref="OffboardingInstanceDto.PendingMandatoryItems"/> both call it, and the client renders the
/// answer instead of deriving it.
/// </para>
/// </summary>
public static class OffboardingCompletionGate
{
    /// <summary>A mandatory task that is not finished, or whose clearance was refused.</summary>
    public const string ReasonNotCompleted = "not_completed";

    /// <summary>A mandatory clearance a department explicitly flagged as having outstanding issues.</summary>
    public const string ReasonClearanceNotApproved = "clearance_not_approved";

    /// <summary>
    /// Every mandatory item standing between this offboarding and completion, in display order.
    /// Empty means nothing blocks.
    /// </summary>
    /// <remarks>
    /// An <b>undecided</b> clearance (<c>ClearanceStatus is null</c>) on a task that is otherwise
    /// <see cref="OnboardingTaskStatus.Completed"/> does <i>not</i> block: not every mandatory task is a
    /// clearance, and requiring a decision on tasks that never carry one would deadlock completion. Only an
    /// explicit <see cref="ClearanceStatus.PendingIssues"/> refuses.
    /// </remarks>
    public static List<PendingMandatoryItemDto> PendingMandatoryItems(
        IEnumerable<OffboardingTaskInstance> tasks) =>
        tasks
            .Where(t => !t.IsDeleted && t.IsMandatory)
            .Where(Blocks)
            .OrderBy(t => t.SortOrder)
            .Select(t => new PendingMandatoryItemDto
            {
                TaskId = t.Id,
                Title = t.Title,
                ClearanceCategory = t.ClearanceCategory,
                ClearanceCategoryName = t.ClearanceCategory.ToString(),
                Reason = t.ClearanceStatus == ClearanceStatus.PendingIssues
                    ? ReasonClearanceNotApproved
                    : ReasonNotCompleted,
            })
            .ToList();

    /// <summary>Whether this single mandatory task blocks completion.</summary>
    private static bool Blocks(OffboardingTaskInstance task) =>
        task.Status != OnboardingTaskStatus.Completed
        || task.ClearanceStatus == ClearanceStatus.PendingIssues;
}
