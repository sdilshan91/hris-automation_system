namespace HRM.Domain.Enums;

/// <summary>
/// The notification categories a user can tune per channel (US-NTF-003 FR-2). One row per
/// (tenant, user, category) in <c>notification_preference</c> when a user customizes a category;
/// otherwise the <see cref="HRM.Domain.Notifications.NotificationPreferenceDefaults"/> catalog supplies
/// the effective value (FR-5 cascade).
///
/// <para>Stored as a string column for readability/forward-compat (the codebase's varchar-enum pattern).
/// The API uses a global JsonStringEnumConverter, so the FE consumes these exact PascalCase member
/// names (US-PLT-003).</para>
/// </summary>
public enum NotificationCategory
{
    /// <summary>Leave request approvals/rejections, balance + accrual updates.</summary>
    LeaveUpdates = 0,

    /// <summary>Attendance regularization decisions, missing-punch and late/early alerts.</summary>
    AttendanceAlerts = 1,

    /// <summary>Payslip publication, payroll-run completion and pay-related notices.</summary>
    PayrollNotifications = 2,

    /// <summary>Onboarding/offboarding task assignments, reminders and clearance updates.</summary>
    OnboardingOffboarding = 3,

    /// <summary>Appraisal cycle phases, goal/review reminders and sign-off requests.</summary>
    PerformanceReviews = 4,

    /// <summary>Interview scheduling, applicant stage moves and offer responses (for hiring users).</summary>
    RecruitmentUpdates = 5,

    /// <summary>Org-wide announcements broadcast by HR/Admin.</summary>
    SystemAnnouncements = 6,

    /// <summary>Security-relevant events (new-device login, password change, MFA). Mandatory — always delivered.</summary>
    SecurityAlerts = 7,
}
