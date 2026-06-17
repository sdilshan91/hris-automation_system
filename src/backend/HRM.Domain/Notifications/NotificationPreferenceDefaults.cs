using HRM.Domain.Enums;

namespace HRM.Domain.Notifications;

/// <summary>
/// Per-category default metadata for the preference matrix (US-NTF-003 FR-5/BR-1). Describes the
/// display name + description shown in the UI and the default channel state a user inherits before
/// they save any override (the bottom of the FR-5 cascade: system/tenant defaults &lt; user prefs).
///
/// <para><b>Mandatory</b> categories (currently only <see cref="NotificationCategory.SecurityAlerts"/>)
/// force both channels on and cannot be disabled by the user (BR-2/AC-3/AC-4). Per-tenant configuration
/// of which categories are mandatory (via the Tenant-Admin console, FR-4/AC-4) is OUT OF SCOPE for
/// US-NTF-003 — this static set is the single tenant-default baseline.</para>
/// </summary>
public sealed record NotificationCategoryDefault(
    NotificationCategory Category,
    string DisplayName,
    string Description,
    bool DefaultInApp,
    bool DefaultEmail,
    bool IsMandatory);

/// <summary>
/// The static defaults catalog that drives the preference matrix when a user has no saved row for a
/// category yet (US-NTF-003 FR-5/BR-1). Single source of truth for the category list, display names,
/// descriptions and the mandatory flag. Pure/static — no tenant state.
///
/// <para>SecurityAlerts is mandatory: both channels are forced on and the user cannot opt out (BR-2).
/// Full Tenant-Admin configuration of mandatory categories (FR-4) is deferred to the Admin Console;
/// this set is the fixed tenant-default baseline for now.</para>
/// </summary>
public static class NotificationPreferenceDefaults
{
    private static readonly IReadOnlyList<NotificationCategoryDefault> _all =
    [
        new(NotificationCategory.LeaveUpdates, "Leave Updates",
            "Approvals, rejections and balance changes for your leave requests.",
            DefaultInApp: true, DefaultEmail: true, IsMandatory: false),

        new(NotificationCategory.AttendanceAlerts, "Attendance Alerts",
            "Regularization decisions, missing punches and late/early alerts.",
            DefaultInApp: true, DefaultEmail: true, IsMandatory: false),

        new(NotificationCategory.PayrollNotifications, "Payroll Notifications",
            "Payslip availability and payroll-run notices.",
            DefaultInApp: true, DefaultEmail: true, IsMandatory: false),

        new(NotificationCategory.OnboardingOffboarding, "Onboarding & Offboarding",
            "Task assignments, reminders and clearance updates.",
            DefaultInApp: true, DefaultEmail: true, IsMandatory: false),

        new(NotificationCategory.PerformanceReviews, "Performance Reviews",
            "Appraisal cycle phases, goal and review reminders.",
            DefaultInApp: true, DefaultEmail: true, IsMandatory: false),

        new(NotificationCategory.RecruitmentUpdates, "Recruitment Updates",
            "Interview scheduling, applicant moves and offer responses.",
            DefaultInApp: true, DefaultEmail: false, IsMandatory: false),

        new(NotificationCategory.SystemAnnouncements, "System Announcements",
            "Organization-wide announcements from HR and administrators.",
            DefaultInApp: true, DefaultEmail: false, IsMandatory: false),

        new(NotificationCategory.SecurityAlerts, "Security Alerts",
            "New-device logins, password changes and security events. Required by your organization.",
            DefaultInApp: true, DefaultEmail: true, IsMandatory: true),
    ];

    /// <summary>All category defaults, in canonical (enum) display order (AC-1 matrix order).</summary>
    public static IReadOnlyList<NotificationCategoryDefault> All => _all;

    /// <summary>The default metadata for a single category.</summary>
    public static NotificationCategoryDefault For(NotificationCategory category) =>
        _all.First(d => d.Category == category);

    /// <summary>True when the category cannot be opted out of (BR-2). Currently SecurityAlerts only.</summary>
    public static bool IsMandatory(NotificationCategory category) => For(category).IsMandatory;
}
