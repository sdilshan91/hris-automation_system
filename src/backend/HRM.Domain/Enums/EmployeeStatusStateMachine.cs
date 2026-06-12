namespace HRM.Domain.Enums;

/// <summary>
/// Enforces valid status transitions for the employee lifecycle (US-CHR-009 FR-2, BR-1).
/// The state machine is hardcoded; tenants cannot customize transitions in Phase 1.
///
/// Valid transitions:
///   probation  -> active, terminated
///   active     -> suspended, terminated, inactive
///   suspended  -> active, terminated
///   inactive   -> active, terminated
///   terminated -> (terminal, no outbound)
/// </summary>
public static class EmployeeStatusStateMachine
{
    private static readonly Dictionary<EmployeeStatus, EmployeeStatus[]> Transitions = new()
    {
        [EmployeeStatus.Probation] = [EmployeeStatus.Active, EmployeeStatus.Terminated],
        [EmployeeStatus.Active] = [EmployeeStatus.Suspended, EmployeeStatus.Terminated, EmployeeStatus.Inactive],
        [EmployeeStatus.Suspended] = [EmployeeStatus.Active, EmployeeStatus.Terminated],
        [EmployeeStatus.Inactive] = [EmployeeStatus.Active, EmployeeStatus.Terminated],
        [EmployeeStatus.Terminated] = [],
    };

    /// <summary>
    /// Returns the list of valid target statuses from the given current status.
    /// </summary>
    public static IReadOnlyList<EmployeeStatus> GetValidTransitions(EmployeeStatus currentStatus)
    {
        return Transitions.TryGetValue(currentStatus, out var targets)
            ? targets
            : [];
    }

    /// <summary>
    /// Returns true if transitioning from <paramref name="from"/> to <paramref name="to"/> is allowed.
    /// </summary>
    public static bool IsValidTransition(EmployeeStatus from, EmployeeStatus to)
    {
        return Transitions.TryGetValue(from, out var targets) && targets.Contains(to);
    }

    /// <summary>
    /// Returns a user-friendly error message for an invalid transition.
    /// </summary>
    public static string GetInvalidTransitionMessage(EmployeeStatus from, EmployeeStatus to)
    {
        return $"Invalid status transition. {from} employees cannot be moved to {to}.";
    }
}
