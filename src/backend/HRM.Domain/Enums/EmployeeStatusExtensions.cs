namespace HRM.Domain.Enums;

/// <summary>
/// The single definition of "may this employee take part in new activity?".
///
/// <para>
/// This existed three times as a bare literal before C4 — <c>AttendanceService</c> (twice, clock-in and one
/// other path) and <c>OvertimeService</c> — each spelled
/// <c>employee.Status is EmployeeStatus.Terminated or EmployeeStatus.Inactive</c>. They agreed, which is
/// exactly what made a fourth copy look harmless. GAP-026 is what happens when the fourth site simply
/// forgets: <c>BenefitEnrollmentService</c> never consulted status at all, so a terminated employee could be
/// enrolled into any rules-free benefit plan — silently, with no error and no log, on a benefits-cost path.
/// </para>
///
/// <para>
/// <b>Probation and Suspended are deliberately NOT blocked.</b> A probationary employee is employed and
/// routinely enrols in benefits; a suspended one is still employed and their coverage normally continues.
/// The gap register prescribed <c>Status == Active</c>, which would have blocked both — a regression dressed
/// as a fix. Where a specific plan should exclude probationers, that is what the eligibility RULES are for
/// (<c>BenefitEligibilityEvaluator</c>), and they already receive the status as an attribute.
/// </para>
/// </summary>
public static class EmployeeStatusExtensions
{
    /// <summary>
    /// True when the employee has left or been deactivated, and so must not be enrolled, scheduled, or
    /// otherwise signed up for anything new. Existing records are untouched — see
    /// <see cref="CanStartNewActivity"/>'s remarks.
    /// </summary>
    public static bool HasLeftTheOrganisation(this EmployeeStatus status)
        => status is EmployeeStatus.Terminated or EmployeeStatus.Inactive;

    /// <summary>
    /// True when the employee may be signed up for something NEW.
    /// </summary>
    /// <remarks>
    /// Deliberately about *new* activity only. C4's decision was to block new enrollments and leave existing
    /// ones alone: US-TRN-003 AC-7 makes termination of an enrollment an explicit, manual act, and a
    /// validation guard must not silently mutate live benefit or training records as a side effect of a
    /// deploy. Someone terminated mid-year keeps their coverage until a human ends it.
    /// </remarks>
    public static bool CanStartNewActivity(this EmployeeStatus status)
        => !status.HasLeftTheOrganisation();
}
