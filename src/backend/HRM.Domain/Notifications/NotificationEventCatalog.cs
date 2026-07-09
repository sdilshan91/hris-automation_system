using HRM.Domain.Enums;

namespace HRM.Domain.Notifications;

/// <summary>
/// One entry in the <see cref="NotificationEventCatalog"/> (US-NTF-002 AC-1/AC-2). Describes an email event
/// type: its machine key, human-readable name, the placeholder variables available to that event's templates
/// (FR-3 variable-reference panel), the sample-data dictionary used for the live preview (FR-4/AC-2), and the
/// seeded system-default subject/HTML/text bodies (BR-2 — every event has a default).
///
/// <para>US-NTF-006: <see cref="Category"/> and <see cref="IsMandatory"/> make the catalog the single source of
/// truth for a notification's preference category and its non-suppressible (BR-1) flag — the dispatcher reads them
/// here by event key rather than guessing from a free-form type string.</para>
/// </summary>
public sealed record NotificationEventDefinition(
    string EventKey,
    string EventName,
    IReadOnlyList<string> Placeholders,
    IReadOnlyDictionary<string, object?> SampleData,
    string DefaultSubject,
    string DefaultBodyHtml,
    string DefaultBodyText,
    NotificationCategory Category,
    bool IsMandatory);

/// <summary>
/// The static registry of email notification event types the platform can send (US-NTF-002 AC-1/AC-2,
/// FR-2/FR-3/FR-4, BR-2). This is the single source of truth that drives: AC-1's event list, AC-2's variable
/// reference panel + live preview sample data, and the seeded system-default templates (DbInitializer).
///
/// <para>Pure/static — no tenant state. Placeholder paths are dotted (e.g. "employee.firstName"); the renderer
/// resolves them against a nested data dictionary, replacing unresolved paths with an empty string (BR-5).</para>
/// </summary>
public static class NotificationEventCatalog
{
    /// <summary>The default language every event is seeded in (FR-5 fallback target, BR-2).</summary>
    public const string DefaultLanguage = "en";

    // ── Shared tenant-branding placeholders available to every event (FR-2). ──
    private static readonly string[] TenantPlaceholders =
    [
        "tenant.companyName",
        "tenant.logoUrl",
        "tenant.supportEmail",
    ];

    // ── Shared placeholders for the tenant-lifecycle events (US-ADM-004). MUST be declared
    // before _byKey: the eager BuildCatalog() at type-init references it, so a later
    // declaration would leave it null → NRE in the type initializer. ──
    private static readonly string[] LifecyclePlaceholders =
    [
        "tenant.name", "event.type", "reason",
    ];

    // ── Shared placeholders for the tenant-welcome events (US-NTF-006 Phase 2b, US-ADM-001). MUST be declared
    // before _byKey: the eager BuildCatalog() at type-init references it, so a later declaration would leave it
    // null → NRE in the type initializer (Phase 2a lesson). ──
    private static readonly string[] WelcomePlaceholders =
    [
        "owner.name", "tenant.name", "subdomain", "forgotPassword.url",
    ];

    // ── Shared placeholders for the leave lifecycle events (US-NTF-006 Phase 3, US-LV-*). MUST be declared before
    // _byKey: the eager BuildCatalog() at type-init references it, so a later declaration would leave it null → NRE
    // in the type initializer (Phase 2a lesson). Mirrors the existing "leave_approved" event's placeholder set. ──
    private static readonly string[] LeavePlaceholders =
    [
        "employee.firstName", "employee.lastName", "employee.email",
        "leave.type", "leave.startDate", "leave.endDate", "leave.days", "leave.reason",
    ];

    // ── Placeholders for the onboarding-checklist-assigned event (US-NTF-006 Phase 3, US-ONB-002). These are FLAT
    // keys (not dotted) because the onboarding outbox payload is flat (employeeName/templateName/startDate/taskCount);
    // the renderer resolves them as top-level fields. MUST be declared before _byKey (Phase 2a lesson). ──
    private static readonly string[] OnboardingChecklistPlaceholders =
    [
        "employeeName", "templateName", "startDate", "taskCount",
    ];

    // ── Placeholders for the onboarding task-completed / task-overdue events (US-NTF-006 Phase 3, US-ONB-003). FLAT
    // keys matching the onboarding outbox payloads. MUST be declared before _byKey (Phase 2a NRE lesson). ──
    private static readonly string[] OnboardingTaskCompletedPlaceholders =
    [
        "taskTitle", "employeeName", "completedAt",
    ];

    private static readonly string[] OnboardingTaskOverduePlaceholders =
    [
        "taskTitle", "employeeName", "dueDate", "daysOverdue",
    ];

    // ── Shared placeholders for the payroll run + approval-workflow events (US-NTF-006 Phase 4, US-PAY-003/008). The
    // Real service loads the run for these fields. MUST be declared before _byKey: the eager BuildCatalog() at
    // type-init references it, so a later declaration would leave it null → NRE in the type initializer (Phase 2a
    // lesson). ──
    private static readonly string[] PayrollPlaceholders =
    [
        "payroll.period", "payroll.processed", "payroll.skipped", "payroll.runId",
    ];

    private static readonly Dictionary<string, NotificationEventDefinition> _byKey =
        BuildCatalog().ToDictionary(e => e.EventKey, StringComparer.OrdinalIgnoreCase);

    /// <summary>All catalog entries, ordered by event name (AC-1 list order).</summary>
    public static IReadOnlyList<NotificationEventDefinition> All { get; } =
        _byKey.Values.OrderBy(e => e.EventName, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>True when <paramref name="eventKey"/> is a known catalog event (case-insensitive).</summary>
    public static bool IsKnownEvent(string eventKey) =>
        !string.IsNullOrWhiteSpace(eventKey) && _byKey.ContainsKey(eventKey);

    /// <summary>Returns the catalog definition for <paramref name="eventKey"/>, or null if unknown.</summary>
    public static NotificationEventDefinition? Get(string eventKey) =>
        !string.IsNullOrWhiteSpace(eventKey) && _byKey.TryGetValue(eventKey, out var def) ? def : null;

    private static IEnumerable<NotificationEventDefinition> BuildCatalog()
    {
        yield return new NotificationEventDefinition(
            EventKey: "leave_approved",
            EventName: "Leave Approved",
            Placeholders:
            [
                "employee.firstName", "employee.lastName", "employee.email",
                "leave.type", "leave.startDate", "leave.endDate", "leave.days",
                "approver.name",
                .. TenantPlaceholders,
            ],
            SampleData: new Dictionary<string, object?>
            {
                ["employee"] = new Dictionary<string, object?>
                {
                    ["firstName"] = "Jane", ["lastName"] = "Doe", ["email"] = "jane.doe@example.com",
                },
                ["leave"] = new Dictionary<string, object?>
                {
                    ["type"] = "Annual Leave", ["startDate"] = "2026-07-01", ["endDate"] = "2026-07-05", ["days"] = 5,
                },
                ["approver"] = new Dictionary<string, object?> { ["name"] = "Sam Manager" },
                ["tenant"] = SampleTenant(),
            },
            DefaultSubject: "Your leave request has been approved",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your <strong>{{leave.type}}</strong> request from {{leave.startDate}} to {{leave.endDate}} " +
                "({{leave.days}} day(s)) has been <strong>approved</strong> by {{approver.name}}.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your {{leave.type}} request from {{leave.startDate}} to {{leave.endDate}} ({{leave.days}} day(s)) " +
                "has been approved by {{approver.name}}.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.LeaveUpdates,
            IsMandatory: false);

        // ── US-NTF-006 Phase 3 — leave lifecycle email leg (mirrors the in-app LeaveNotificationService). ──
        yield return new NotificationEventDefinition(
            EventKey: "leave_requested",
            EventName: "Leave Requested",
            Placeholders: [.. LeavePlaceholders, .. TenantPlaceholders],
            SampleData: LeaveSample(),
            DefaultSubject: "Leave request pending your approval",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p><strong>{{employee.firstName}} {{employee.lastName}}</strong> has submitted a " +
                "<strong>{{leave.type}}</strong> request from {{leave.startDate}} to {{leave.endDate}} " +
                "({{leave.days}} day(s)) that needs your approval.</p>" +
                "<p>Reason: {{leave.reason}}</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "{{employee.firstName}} {{employee.lastName}} has submitted a {{leave.type}} request from " +
                "{{leave.startDate}} to {{leave.endDate}} ({{leave.days}} day(s)) that needs your approval.\n\n" +
                "Reason: {{leave.reason}}\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.LeaveUpdates,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "leave_rejected",
            EventName: "Leave Rejected",
            Placeholders: [.. LeavePlaceholders, "approver.name", .. TenantPlaceholders],
            SampleData: LeaveSample(approverName: "Sam Manager", reason: "Insufficient coverage during that week."),
            DefaultSubject: "Your leave request has been rejected",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your <strong>{{leave.type}}</strong> request from {{leave.startDate}} to {{leave.endDate}} " +
                "({{leave.days}} day(s)) has been <strong>rejected</strong> by {{approver.name}}.</p>" +
                "<p>Reason: {{leave.reason}}</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your {{leave.type}} request from {{leave.startDate}} to {{leave.endDate}} ({{leave.days}} day(s)) " +
                "has been rejected by {{approver.name}}.\n\nReason: {{leave.reason}}\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.LeaveUpdates,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "leave_cancelled",
            EventName: "Leave Cancelled",
            Placeholders: [.. LeavePlaceholders, .. TenantPlaceholders],
            SampleData: LeaveSample(reason: "Plans changed."),
            DefaultSubject: "A leave request has been cancelled",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p><strong>{{employee.firstName}} {{employee.lastName}}</strong> has cancelled a " +
                "<strong>{{leave.type}}</strong> request from {{leave.startDate}} to {{leave.endDate}} " +
                "({{leave.days}} day(s)).</p>" +
                "<p>Reason: {{leave.reason}}</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "{{employee.firstName}} {{employee.lastName}} has cancelled a {{leave.type}} request from " +
                "{{leave.startDate}} to {{leave.endDate}} ({{leave.days}} day(s)).\n\n" +
                "Reason: {{leave.reason}}\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.LeaveUpdates,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "leave_lop",
            EventName: "Loss-of-Pay Leave Assigned",
            Placeholders:
            [
                "employee.firstName", "employee.lastName", "employee.email",
                "leave.days", "leave.source", "leave.reason",
                .. TenantPlaceholders,
            ],
            SampleData: LeaveSample(reason: "Unapproved absence.", lop: true),
            DefaultSubject: "Loss-of-pay leave assigned",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p><strong>{{leave.days}}</strong> loss-of-pay leave day(s) have been assigned to your record " +
                "({{leave.source}}).</p>" +
                "<p>Reason: {{leave.reason}}</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "{{leave.days}} loss-of-pay leave day(s) have been assigned to your record ({{leave.source}}).\n\n" +
                "Reason: {{leave.reason}}\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.LeaveUpdates,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "onboarding_welcome",
            EventName: "Onboarding Welcome",
            Placeholders:
            [
                "employee.firstName", "employee.lastName", "employee.email",
                "employee.startDate", "employee.jobTitle", "employee.department",
                "manager.name",
                .. TenantPlaceholders,
            ],
            SampleData: new Dictionary<string, object?>
            {
                ["employee"] = new Dictionary<string, object?>
                {
                    ["firstName"] = "Alex", ["lastName"] = "Newcomer", ["email"] = "alex.newcomer@example.com",
                    ["startDate"] = "2026-08-01", ["jobTitle"] = "Software Engineer", ["department"] = "Engineering",
                },
                ["manager"] = new Dictionary<string, object?> { ["name"] = "Sam Manager" },
                ["tenant"] = SampleTenant(),
            },
            DefaultSubject: "Welcome to {{tenant.companyName}}!",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Welcome to <strong>{{tenant.companyName}}</strong>! We're excited to have you join " +
                "{{employee.department}} as {{employee.jobTitle}} starting {{employee.startDate}}.</p>" +
                "<p>Your manager {{manager.name}} will be in touch.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Welcome to {{tenant.companyName}}! We're excited to have you join {{employee.department}} as " +
                "{{employee.jobTitle}} starting {{employee.startDate}}.\n\n" +
                "Your manager {{manager.name}} will be in touch.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.OnboardingOffboarding,
            IsMandatory: false);

        // ── US-NTF-006 Phase 3 — onboarding CHECKLIST ASSIGNED (US-ONB-002). A dedicated event whose FLAT
        // placeholders match the onboarding outbox payload (employeeName/templateName/startDate/taskCount), so the
        // rendered email has no blank placeholders — the generic "onboarding_welcome" template does NOT match that
        // payload. Recipients are the new hire + manager + IT, so the copy is role-neutral and carries no branding
        // placeholder (the outbox payload has none). ──
        yield return new NotificationEventDefinition(
            EventKey: "onboarding_checklist_assigned",
            EventName: "Onboarding Checklist Assigned",
            Placeholders: [.. OnboardingChecklistPlaceholders],
            SampleData: new Dictionary<string, object?>
            {
                ["employeeName"] = "Alex Newcomer",
                ["templateName"] = "Engineering Onboarding",
                ["startDate"] = "2026-08-01",
                ["taskCount"] = 7,
            },
            DefaultSubject: "Onboarding checklist assigned: {{templateName}}",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>An onboarding checklist — <strong>{{templateName}}</strong> — has been assigned for " +
                "<strong>{{employeeName}}</strong>, starting {{startDate}}. It has {{taskCount}} task(s).</p>" +
                "<p>Please review your assigned tasks in the HRM portal.</p>" +
                "<p>Regards,<br/>The HR Team</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "An onboarding checklist - {{templateName}} - has been assigned for {{employeeName}}, starting " +
                "{{startDate}}. It has {{taskCount}} task(s).\n\n" +
                "Please review your assigned tasks in the HRM portal.\n\nRegards,\nThe HR Team",
            Category: NotificationCategory.OnboardingOffboarding,
            IsMandatory: false);

        // ── US-NTF-006 Phase 3 — onboarding TASK COMPLETED (US-ONB-003 AC-3/FR-5). Sent to HR + manager. FLAT
        // placeholders match the completion outbox payload (taskTitle/employeeName/completedAt). ──
        yield return new NotificationEventDefinition(
            EventKey: "onboarding_task_completed",
            EventName: "Onboarding Task Completed",
            Placeholders: [.. OnboardingTaskCompletedPlaceholders],
            SampleData: new Dictionary<string, object?>
            {
                ["taskTitle"] = "Sign employment contract",
                ["employeeName"] = "Alex Newcomer",
                ["completedAt"] = "2026-08-02T09:30:00Z",
            },
            DefaultSubject: "Onboarding task completed: {{taskTitle}}",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p><strong>{{employeeName}}</strong> has completed the onboarding task " +
                "<strong>{{taskTitle}}</strong> on {{completedAt}}.</p>" +
                "<p>Regards,<br/>The HR Team</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "{{employeeName}} has completed the onboarding task {{taskTitle}} on {{completedAt}}.\n\n" +
                "Regards,\nThe HR Team",
            Category: NotificationCategory.OnboardingOffboarding,
            IsMandatory: false);

        // ── US-NTF-006 Phase 3 — onboarding TASK OVERDUE (US-ONB-003 AC-5/FR-6). Sent to employee + HR + manager by
        // the daily sweep. FLAT placeholders match the overdue outbox payload (taskTitle/dueDate/daysOverdue/name). ──
        yield return new NotificationEventDefinition(
            EventKey: "onboarding_task_overdue",
            EventName: "Onboarding Task Overdue",
            Placeholders: [.. OnboardingTaskOverduePlaceholders],
            SampleData: new Dictionary<string, object?>
            {
                ["taskTitle"] = "Submit tax forms",
                ["employeeName"] = "Alex Newcomer",
                ["dueDate"] = "2026-08-05",
                ["daysOverdue"] = 3,
            },
            DefaultSubject: "Onboarding task overdue: {{taskTitle}}",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>The onboarding task <strong>{{taskTitle}}</strong> for <strong>{{employeeName}}</strong> was due " +
                "on {{dueDate}} and is now <strong>{{daysOverdue}} day(s) overdue</strong>.</p>" +
                "<p>Please complete it as soon as possible.</p>" +
                "<p>Regards,<br/>The HR Team</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "The onboarding task {{taskTitle}} for {{employeeName}} was due on {{dueDate}} and is now " +
                "{{daysOverdue}} day(s) overdue.\n\nPlease complete it as soon as possible.\n\n" +
                "Regards,\nThe HR Team",
            Category: NotificationCategory.OnboardingOffboarding,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "payslip_published",
            EventName: "Payslip Published",
            Placeholders:
            [
                "employee.firstName", "employee.lastName", "employee.email",
                "payslip.month", "payslip.year", "payslip.periodLabel", "payslip.url",
                .. TenantPlaceholders,
            ],
            SampleData: new Dictionary<string, object?>
            {
                ["employee"] = new Dictionary<string, object?>
                {
                    ["firstName"] = "Jane", ["lastName"] = "Doe", ["email"] = "jane.doe@example.com",
                },
                ["payslip"] = new Dictionary<string, object?>
                {
                    ["month"] = "May", ["year"] = 2026, ["periodLabel"] = "May 2026",
                    ["url"] = "https://app.example.com/payslips/123",
                },
                ["tenant"] = SampleTenant(),
            },
            DefaultSubject: "Your payslip for {{payslip.periodLabel}} is ready",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your payslip for <strong>{{payslip.periodLabel}}</strong> is now available. " +
                "You can view it securely in the employee portal.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your payslip for {{payslip.periodLabel}} is now available. You can view it securely in the " +
                "employee portal.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PayrollNotifications,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "password_reset",
            EventName: "Password Reset",
            Placeholders:
            [
                "user.firstName", "user.email",
                "reset.url", "reset.expiryHours",
                .. TenantPlaceholders,
            ],
            SampleData: new Dictionary<string, object?>
            {
                ["user"] = new Dictionary<string, object?>
                {
                    ["firstName"] = "Jane", ["email"] = "jane.doe@example.com",
                },
                ["reset"] = new Dictionary<string, object?>
                {
                    ["url"] = "https://app.example.com/reset?token=sample", ["expiryHours"] = 24,
                },
                ["tenant"] = SampleTenant(),
            },
            DefaultSubject: "Reset your {{tenant.companyName}} password",
            DefaultBodyHtml:
                "<p>Hi {{user.firstName}},</p>" +
                "<p>We received a request to reset your password. Click the link below to choose a new one. " +
                "This link expires in {{reset.expiryHours}} hours.</p>" +
                "<p><a href=\"{{reset.url}}\">Reset your password</a></p>" +
                "<p>If you didn't request this, you can safely ignore this email.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{user.firstName}},\n\n" +
                "We received a request to reset your password. Open the link below to choose a new one. " +
                "This link expires in {{reset.expiryHours}} hours.\n\n{{reset.url}}\n\n" +
                "If you didn't request this, you can safely ignore this email.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.SecurityAlerts,
            IsMandatory: true);

        // ── US-NTF-006 Phase 2a — impersonation (US-ADM-003) ──
        yield return new NotificationEventDefinition(
            EventKey: "impersonation_started",
            EventName: "Impersonation Started",
            Placeholders:
            [
                "actor.email", "target.email", "reason", "session.id", "startedAt", "expiresAt",
                .. TenantPlaceholders,
            ],
            SampleData: new Dictionary<string, object?>
            {
                ["actor"] = new Dictionary<string, object?> { ["email"] = "support@platform.example.com" },
                ["target"] = new Dictionary<string, object?> { ["email"] = "jane.doe@example.com" },
                ["reason"] = "Investigating a payroll discrepancy (ticket #4821).",
                ["session"] = new Dictionary<string, object?> { ["id"] = "019f2607-0000-7000-8000-000000000000" },
                ["startedAt"] = "2026-07-09T10:15:00Z",
                ["expiresAt"] = "2026-07-09T11:15:00Z",
                ["tenant"] = SampleTenant(),
            },
            DefaultSubject: "A support session has started on your {{tenant.companyName}} account",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>Platform support user <strong>{{actor.email}}</strong> has started an impersonation session as " +
                "<strong>{{target.email}}</strong> on your account at {{startedAt}}.</p>" +
                "<p>Reason: {{reason}}</p>" +
                "<p>Reference: {{session.id}}. This session expires at {{expiresAt}}.</p>" +
                "<p>If you believe this session is unexpected, contact support immediately.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "Platform support user {{actor.email}} has started an impersonation session as {{target.email}} " +
                "on your account at {{startedAt}}.\n\n" +
                "Reason: {{reason}}\n\n" +
                "Reference: {{session.id}}. This session expires at {{expiresAt}}.\n\n" +
                "If you believe this session is unexpected, contact support immediately.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.SecurityAlerts,
            IsMandatory: true);

        // ── US-NTF-006 Phase 2a — tenant lifecycle (US-ADM-004) ──
        yield return new NotificationEventDefinition(
            EventKey: "tenant_suspended",
            EventName: "Tenant Suspended",
            Placeholders: [.. LifecyclePlaceholders, .. TenantPlaceholders],
            SampleData: LifecycleSample("suspended", reason: "Overdue invoice (30 days past due)."),
            DefaultSubject: "Your {{tenant.name}} account has been suspended",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>Your <strong>{{tenant.name}}</strong> account has been <strong>suspended</strong>.</p>" +
                "<p>Reason: {{reason}}</p>" +
                "<p>While suspended, users cannot sign in. Please contact support to resolve this.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "Your {{tenant.name}} account has been suspended.\n\n" +
                "Reason: {{reason}}\n\n" +
                "While suspended, users cannot sign in. Please contact support to resolve this.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.SystemAnnouncements,
            IsMandatory: true);

        yield return new NotificationEventDefinition(
            EventKey: "tenant_termination_initiated",
            EventName: "Tenant Termination Initiated",
            Placeholders: [.. LifecyclePlaceholders, "termination.scheduledAt", .. TenantPlaceholders],
            SampleData: LifecycleSample("termination_initiated",
                reason: "Account closure requested by the customer.", scheduledAt: "2026-08-08T00:00:00Z"),
            DefaultSubject: "Your {{tenant.name}} account is scheduled for termination",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>Termination has been initiated for your <strong>{{tenant.name}}</strong> account. All data is " +
                "scheduled for permanent deletion at <strong>{{termination.scheduledAt}}</strong>.</p>" +
                "<p>Reason: {{reason}}</p>" +
                "<p>Contact support before this date if this was not intended.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "Termination has been initiated for your {{tenant.name}} account. All data is scheduled for " +
                "permanent deletion at {{termination.scheduledAt}}.\n\n" +
                "Reason: {{reason}}\n\n" +
                "Contact support before this date if this was not intended.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.SystemAnnouncements,
            IsMandatory: true);

        yield return new NotificationEventDefinition(
            EventKey: "tenant_reactivated",
            EventName: "Tenant Reactivated",
            Placeholders: [.. LifecyclePlaceholders, .. TenantPlaceholders],
            SampleData: LifecycleSample("reactivated", reason: "Outstanding balance settled."),
            DefaultSubject: "Your {{tenant.name}} account has been reactivated",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>Good news — your <strong>{{tenant.name}}</strong> account has been <strong>reactivated</strong> " +
                "and users can sign in again.</p>" +
                "<p>Reason: {{reason}}</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "Good news - your {{tenant.name}} account has been reactivated and users can sign in again.\n\n" +
                "Reason: {{reason}}\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.SystemAnnouncements,
            IsMandatory: true);

        yield return new NotificationEventDefinition(
            EventKey: "tenant_restored",
            EventName: "Tenant Restored",
            Placeholders: [.. LifecyclePlaceholders, .. TenantPlaceholders],
            SampleData: LifecycleSample("restored", reason: "Termination cancelled during the grace period."),
            DefaultSubject: "Your {{tenant.name}} account has been restored",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>Your <strong>{{tenant.name}}</strong> account has been <strong>restored</strong>; the pending " +
                "termination has been cancelled and your data is intact.</p>" +
                "<p>Reason: {{reason}}</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "Your {{tenant.name}} account has been restored; the pending termination has been cancelled and " +
                "your data is intact.\n\n" +
                "Reason: {{reason}}\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.SystemAnnouncements,
            IsMandatory: true);

        // ── US-NTF-006 Phase 2b — user invitation (US-ADM-005 AC-2). Email-only: the invitee has no User row yet.
        // Carries a real accept link (the invitation persists a one-time token; the link embeds the RAW token). ──
        yield return new NotificationEventDefinition(
            EventKey: "user_invitation",
            EventName: "User Invitation",
            Placeholders:
            [
                "invitee.email", "tenant.name",
                "invitation.acceptUrl", "invitation.expiryHours",
                .. TenantPlaceholders,
            ],
            SampleData: new Dictionary<string, object?>
            {
                ["invitee"] = new Dictionary<string, object?> { ["email"] = "new.user@example.com" },
                ["tenant"] = MergeTenant(new Dictionary<string, object?> { ["name"] = "Acme Corporation" }),
                ["invitation"] = new Dictionary<string, object?>
                {
                    ["acceptUrl"] = "https://acme.example.com/accept-invite?token=sample", ["expiryHours"] = 72,
                },
            },
            DefaultSubject: "You've been invited to join {{tenant.name}}",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>You have been invited to join <strong>{{tenant.name}}</strong> on HRM. Click the link below to " +
                "accept the invitation and set up your account. This invitation expires in " +
                "{{invitation.expiryHours}} hours.</p>" +
                "<p><a href=\"{{invitation.acceptUrl}}\">Accept your invitation</a></p>" +
                "<p>If you weren't expecting this, you can safely ignore this email.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "You have been invited to join {{tenant.name}} on HRM. Open the link below to accept the invitation " +
                "and set up your account. This invitation expires in {{invitation.expiryHours}} hours.\n\n" +
                "{{invitation.acceptUrl}}\n\n" +
                "If you weren't expecting this, you can safely ignore this email.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.SystemAnnouncements,
            IsMandatory: false);

        // ── US-NTF-006 Phase 2b — admin-forced password reset (US-ADM-005 AC-5). INFORMATIONAL only: NO link/token
        // (per the product decision). The recipient uses the existing self-service Forgot Password flow. ──
        yield return new NotificationEventDefinition(
            EventKey: "admin_password_reset",
            EventName: "Password Reset by Administrator",
            Placeholders:
            [
                "user.email", "tenant.name",
                .. TenantPlaceholders,
            ],
            SampleData: new Dictionary<string, object?>
            {
                ["user"] = new Dictionary<string, object?> { ["email"] = "jane.doe@example.com" },
                ["tenant"] = MergeTenant(new Dictionary<string, object?> { ["name"] = "Acme Corporation" }),
            },
            DefaultSubject: "Your {{tenant.name}} password was reset by an administrator",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>An administrator has reset the password on your <strong>{{tenant.name}}</strong> account. For " +
                "security, no new password has been set for you.</p>" +
                "<p>To choose a new password, go to your organization's sign-in page and use the " +
                "<strong>Forgot Password</strong> option.</p>" +
                "<p>If you believe this was unexpected, contact your administrator.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "An administrator has reset the password on your {{tenant.name}} account. For security, no new " +
                "password has been set for you.\n\n" +
                "To choose a new password, go to your organization's sign-in page and use the Forgot Password " +
                "option.\n\n" +
                "If you believe this was unexpected, contact your administrator.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.SecurityAlerts,
            IsMandatory: true);

        // ── US-NTF-006 Phase 2b — tenant welcome (US-ADM-001 FR-4). INFORMATIONAL: no set-password token (per the
        // product decision). A not-yet-existing owner is pointed at the self-service Forgot Password page. Email-only
        // (the owner may not yet have a signed-in session). Split trial/active so the wording can differ. ──
        yield return new NotificationEventDefinition(
            EventKey: "tenant_welcome_trial",
            EventName: "Tenant Welcome (Trial)",
            Placeholders: [.. WelcomePlaceholders, .. TenantPlaceholders],
            SampleData: WelcomeSample(),
            DefaultSubject: "Welcome to HRM — your {{tenant.name}} trial workspace is ready",
            DefaultBodyHtml:
                "<p>Hi {{owner.name}},</p>" +
                "<p>Your HRM workspace <strong>{{subdomain}}</strong> is ready and your free trial has started. " +
                "We're excited to have <strong>{{tenant.name}}</strong> on board.</p>" +
                "<p>To set your password and sign in for the first time, use the Forgot Password option at " +
                "<a href=\"{{forgotPassword.url}}\">{{forgotPassword.url}}</a>.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{owner.name}},\n\n" +
                "Your HRM workspace {{subdomain}} is ready and your free trial has started. We're excited to have " +
                "{{tenant.name}} on board.\n\n" +
                "To set your password and sign in for the first time, use the Forgot Password option at " +
                "{{forgotPassword.url}}.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.SystemAnnouncements,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "tenant_welcome_active",
            EventName: "Tenant Welcome (Active)",
            Placeholders: [.. WelcomePlaceholders, .. TenantPlaceholders],
            SampleData: WelcomeSample(),
            DefaultSubject: "Welcome to HRM — your {{tenant.name}} workspace is ready",
            DefaultBodyHtml:
                "<p>Hi {{owner.name}},</p>" +
                "<p>Your HRM workspace <strong>{{subdomain}}</strong> is ready. We're excited to have " +
                "<strong>{{tenant.name}}</strong> on board.</p>" +
                "<p>To set your password and sign in for the first time, use the Forgot Password option at " +
                "<a href=\"{{forgotPassword.url}}\">{{forgotPassword.url}}</a>.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{owner.name}},\n\n" +
                "Your HRM workspace {{subdomain}} is ready. We're excited to have {{tenant.name}} on board.\n\n" +
                "To set your password and sign in for the first time, use the Forgot Password option at " +
                "{{forgotPassword.url}}.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.SystemAnnouncements,
            IsMandatory: false);

        // ── US-NTF-006 Phase 4 — data export ready (US-ADM-010 AC-2/BR-6). Email-only: recipients are RAW addresses
        // (the requester + the tenant billing/contact) that may have no User row. Carries the download link. ──
        yield return new NotificationEventDefinition(
            EventKey: "data_export_ready",
            EventName: "Data Export Ready",
            Placeholders: ["export.downloadUrl", .. TenantPlaceholders],
            SampleData: new Dictionary<string, object?>
            {
                ["export"] = new Dictionary<string, object?>
                {
                    ["downloadUrl"] = "https://app.example.com/exports/019f2607/download?token=sample",
                },
                ["tenant"] = SampleTenant(),
            },
            DefaultSubject: "Your data export is ready to download",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>Your requested data export bundle is ready. Use the secure link below to download it. The link " +
                "is time-limited.</p>" +
                "<p><a href=\"{{export.downloadUrl}}\">Download your export</a></p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "Your requested data export bundle is ready. Use the secure link below to download it. The link is " +
                "time-limited.\n\n{{export.downloadUrl}}\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.SystemAnnouncements,
            IsMandatory: false);

        // ── US-NTF-006 Phase 4 — payroll run + approval workflow (US-PAY-003 AC-3 / US-PAY-008 AC-1). Recipients are
        // resolved in the Real service (approver pool / run submitter); the payload carries the run period + counts. ──
        yield return new NotificationEventDefinition(
            EventKey: "payroll_run_ready",
            EventName: "Payroll Run Ready for Review",
            Placeholders: [.. PayrollPlaceholders, .. TenantPlaceholders],
            SampleData: PayrollSample(),
            DefaultSubject: "Payroll run for {{payroll.period}} is ready for review",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>The payroll run for <strong>{{payroll.period}}</strong> has finished computing " +
                "({{payroll.processed}} processed, {{payroll.skipped}} skipped) and is awaiting your review.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "The payroll run for {{payroll.period}} has finished computing ({{payroll.processed}} processed, " +
                "{{payroll.skipped}} skipped) and is awaiting your review.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PayrollNotifications,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "payroll_approval_submitted",
            EventName: "Payroll Run Submitted for Approval",
            Placeholders: [.. PayrollPlaceholders, .. TenantPlaceholders],
            SampleData: PayrollSample(),
            DefaultSubject: "Payroll run for {{payroll.period}} needs your approval",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>The payroll run for <strong>{{payroll.period}}</strong> has been submitted and is awaiting your " +
                "approval.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "The payroll run for {{payroll.period}} has been submitted and is awaiting your approval.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PayrollNotifications,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "payroll_approval_approved",
            EventName: "Payroll Run Approved",
            Placeholders: [.. PayrollPlaceholders, .. TenantPlaceholders],
            SampleData: PayrollSample(),
            DefaultSubject: "Your payroll run for {{payroll.period}} was approved",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>The payroll run for <strong>{{payroll.period}}</strong> you submitted has been " +
                "<strong>approved</strong>.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "The payroll run for {{payroll.period}} you submitted has been approved.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PayrollNotifications,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "payroll_approval_rejected",
            EventName: "Payroll Run Rejected",
            Placeholders: [.. PayrollPlaceholders, .. TenantPlaceholders],
            SampleData: PayrollSample(),
            DefaultSubject: "Your payroll run for {{payroll.period}} was rejected",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>The payroll run for <strong>{{payroll.period}}</strong> you submitted has been " +
                "<strong>rejected</strong>.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "The payroll run for {{payroll.period}} you submitted has been rejected.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PayrollNotifications,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "payroll_approval_returned",
            EventName: "Payroll Run Returned to HR",
            Placeholders: [.. PayrollPlaceholders, .. TenantPlaceholders],
            SampleData: PayrollSample(),
            DefaultSubject: "Your payroll run for {{payroll.period}} was returned to HR",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>The payroll run for <strong>{{payroll.period}}</strong> you submitted has been " +
                "<strong>returned to HR</strong> for changes.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "The payroll run for {{payroll.period}} you submitted has been returned to HR for changes.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PayrollNotifications,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "payroll_finalized",
            EventName: "Payroll Run Finalized",
            Placeholders: [.. PayrollPlaceholders, .. TenantPlaceholders],
            SampleData: PayrollSample(),
            DefaultSubject: "Payroll run for {{payroll.period}} was finalized",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>The payroll run for <strong>{{payroll.period}}</strong> has been <strong>finalized</strong>.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "The payroll run for {{payroll.period}} has been finalized.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PayrollNotifications,
            IsMandatory: false);
    }

    // ── Sample-data for the payroll run + approval events (US-NTF-006 Phase 4). ──
    private static Dictionary<string, object?> PayrollSample() => new()
    {
        ["payroll"] = new Dictionary<string, object?>
        {
            ["period"] = "May 2026", ["processed"] = 42, ["skipped"] = 1,
            ["runId"] = "019f2607-0000-7000-8000-000000000000",
        },
        ["tenant"] = SampleTenant(),
    };

    /// <summary>Merges the shared tenant-branding sample values into a tenant sample node (for Phase 2b events).</summary>
    private static Dictionary<string, object?> MergeTenant(Dictionary<string, object?> tenant)
    {
        foreach (var kv in SampleTenant())
            tenant[kv.Key] = kv.Value;
        return tenant;
    }

    // ── Sample-data for the tenant-welcome events (US-NTF-006 Phase 2b). ──
    private static Dictionary<string, object?> WelcomeSample() => new()
    {
        ["owner"] = new Dictionary<string, object?> { ["name"] = "Sam Owner" },
        ["subdomain"] = "acme",
        ["forgotPassword"] = new Dictionary<string, object?>
        {
            ["url"] = "https://acme.example.com/forgot-password",
        },
        ["tenant"] = MergeTenant(new Dictionary<string, object?> { ["name"] = "Acme Corporation" }),
    };

    // ── Sample-data for the leave lifecycle events (US-NTF-006 Phase 3). ──
    private static Dictionary<string, object?> LeaveSample(
        string? approverName = null, string? reason = null, bool lop = false)
    {
        var data = new Dictionary<string, object?>
        {
            ["employee"] = new Dictionary<string, object?>
            {
                ["firstName"] = "Jane", ["lastName"] = "Doe", ["email"] = "jane.doe@example.com",
            },
            ["leave"] = lop
                ? new Dictionary<string, object?>
                {
                    ["days"] = 2, ["source"] = "HrAssigned", ["reason"] = reason ?? "Unapproved absence.",
                }
                : new Dictionary<string, object?>
                {
                    ["type"] = "Annual Leave",
                    ["startDate"] = "2026-07-01", ["endDate"] = "2026-07-05", ["days"] = 5,
                    ["reason"] = reason ?? "Family vacation.",
                },
            ["tenant"] = SampleTenant(),
        };
        if (approverName is not null)
            data["approver"] = new Dictionary<string, object?> { ["name"] = approverName };
        return data;
    }

    // ── Sample-data for the tenant-lifecycle events (US-ADM-004). ──
    private static Dictionary<string, object?> LifecycleSample(
        string eventType, string reason, string? scheduledAt = null)
    {
        var data = new Dictionary<string, object?>
        {
            ["tenant"] = new Dictionary<string, object?> { ["name"] = "Acme Corporation" },
            ["event"] = new Dictionary<string, object?> { ["type"] = eventType },
            ["reason"] = reason,
        };
        // Merge the shared branding placeholders (tenant.companyName/logoUrl/supportEmail) into the tenant node.
        var tenant = (Dictionary<string, object?>)data["tenant"]!;
        foreach (var kv in SampleTenant())
            tenant[kv.Key] = kv.Value;
        if (scheduledAt is not null)
            data["termination"] = new Dictionary<string, object?> { ["scheduledAt"] = scheduledAt };
        return data;
    }

    private static Dictionary<string, object?> SampleTenant() => new()
    {
        ["companyName"] = "Acme Corporation",
        ["logoUrl"] = "https://app.example.com/logo.png",
        ["supportEmail"] = "support@acme.example.com",
    };
}
