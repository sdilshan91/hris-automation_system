using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Domain.Workflows;

/// <summary>
/// A resolved, applicable workflow step the evaluator has determined applies to a given request (US-ADM-007
/// FR-2). Carries everything the (deferred) runtime router would need without holding the EF entity.
/// </summary>
public sealed record ApplicableStep(
    int StepOrder,
    WorkflowApproverType ApproverType,
    Guid? ApproverIdentifier,
    bool IsParallel,
    int SlaHours,
    WorkflowApproverType? EscalationApproverType,
    Guid? EscalationApproverIdentifier,
    bool DelegationEnabled,
    Guid? DelegationBackupUserId)
{
    /// <summary>
    /// BR-3: a parallel step requires ALL of its approvers to approve before the request proceeds. For a single
    /// step the flag is informational; the (deferred) runtime engine fans the step out to every approver and
    /// only advances once all have approved.
    /// </summary>
    public bool RequiresAllApprovers => IsParallel;
}

/// <summary>
/// The PURE, side-effect-free core of the approval engine (US-ADM-007 FR-2/BR-3/BR-5). Given a workflow
/// definition's steps and a request's data dictionary, it returns the ORDERED list of APPLICABLE steps —
/// skipping any step whose condition is not met (BR-5) and surfacing parallel steps as all-approvers-required
/// groups (BR-3).
///
/// <para>This is the engine's brain and is heavily unit-tested. WIRING it into live Leave/Attendance/Expense/
/// Offer request processing (per-request WorkflowInstance/StepInstance creation, SLA Hangfire timers,
/// live delegation when an approver is on leave) is DEFERRED engine work — this class only computes which
/// steps apply; it never touches the database, clock, or any service.</para>
/// </summary>
public static class WorkflowEvaluator
{
    /// <summary>
    /// Returns the ordered applicable steps for <paramref name="steps"/> given <paramref name="requestData"/>.
    /// Steps are ordered by <see cref="WorkflowStep.StepOrder"/>; a step with a condition that the request data
    /// does not satisfy is skipped (BR-5). A step with no condition is always applicable. A malformed condition
    /// is treated as not-applicable (defensive — validation rejects malformed conditions at save time, FR-5).
    /// </summary>
    public static IReadOnlyList<ApplicableStep> Evaluate(
        IEnumerable<WorkflowStep> steps,
        IReadOnlyDictionary<string, object?> requestData)
    {
        ArgumentNullException.ThrowIfNull(steps);
        requestData ??= new Dictionary<string, object?>();

        var result = new List<ApplicableStep>();

        foreach (var step in steps.OrderBy(s => s.StepOrder))
        {
            if (!IsApplicable(step, requestData))
                continue;

            result.Add(new ApplicableStep(
                step.StepOrder,
                step.ApproverType,
                step.ApproverIdentifier,
                step.IsParallel,
                step.SlaHours,
                step.EscalationApproverType,
                step.EscalationApproverIdentifier,
                step.DelegationEnabled,
                step.DelegationBackupUserId));
        }

        return result;
    }

    /// <summary>
    /// True when the step applies to the request (BR-5): no condition ⇒ always applies; otherwise the condition
    /// must be satisfied by the request data. A condition that fails to parse is treated as not-applicable.
    /// </summary>
    public static bool IsApplicable(WorkflowStep step, IReadOnlyDictionary<string, object?> requestData)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (string.IsNullOrWhiteSpace(step.ConditionJson))
            return true;

        WorkflowCondition? condition;
        try
        {
            condition = WorkflowCondition.Parse(step.ConditionJson);
        }
        catch (FormatException)
        {
            return false;
        }

        return condition is null || condition.IsSatisfiedBy(requestData);
    }
}
