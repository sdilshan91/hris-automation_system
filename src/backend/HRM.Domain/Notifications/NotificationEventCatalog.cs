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

    // ── Shared placeholders for the recruitment applicant events (US-NTF-006 Phase 5a, US-REC-002/004). MUST be
    // declared before _byKey: the eager BuildCatalog() at type-init references it, so a later declaration would leave
    // it null → NRE in the type initializer (Phase 2a lesson). ──
    private static readonly string[] ApplicantPlaceholders =
    [
        "applicant.firstName", "applicant.lastName", "applicant.email",
        "vacancy.title", "application.reference", "application.fromStage", "application.toStage",
    ];

    // ── Shared placeholders for the recruitment interview events (US-NTF-006 Phase 5a, US-REC-005). MUST be declared
    // before _byKey (Phase 2a NRE lesson). ──
    private static readonly string[] InterviewPlaceholders =
    [
        "applicant.email", "vacancy.title",
        "interview.date", "interview.time", "interview.type", "interview.location",
    ];

    // ── Shared placeholders for the recruitment offer events (US-NTF-006 Phase 5a, US-REC-007). MUST be declared
    // before _byKey (Phase 2a NRE lesson). ──
    private static readonly string[] OfferPlaceholders =
    [
        "applicant.firstName", "applicant.lastName", "applicant.email", "vacancy.title",
        "offer.reference", "offer.position", "offer.startDate", "offer.expiryDate",
        // DF-42: candidate magic-link to the offer in the portal (only rendered by offer_sent).
        "offer.portalUrl",
    ];

    // ── Shared placeholders for the performance events (US-NTF-006 Phase 5b, US-PRF-001..009). ALL performance
    // recipients are internal employees; the Real service loads the subject employee + cycle/goal for these fields.
    // MUST be declared before _byKey: the eager BuildCatalog() at type-init references them, so a later declaration
    // would leave them null → NRE in the type initializer (Phase 2a lesson). ──
    private static readonly string[] GoalPlaceholders =
    [
        "employee.firstName", "employee.lastName", "cycle.name", "goal.title",
    ];

    private static readonly string[] PerformancePlaceholders =
    [
        "employee.firstName", "employee.lastName", "cycle.name",
    ];

    private static readonly string[] CyclePlaceholders =
    [
        "employee.firstName", "employee.lastName", "cycle.name", "event.subtype", "event.detail",
    ];

    private static readonly string[] ReviewerPlaceholders =
    [
        "reviewer.firstName", "reviewee.firstName", "reviewee.lastName", "cycle.name",
    ];

    private static readonly string[] PipPlaceholders =
    [
        "employee.firstName", "employee.lastName", "pip.subtype", "pip.detail",
    ];

    private static readonly string[] GoalProgressPlaceholders =
    [
        "employee.firstName", "employee.lastName", "goal.title", "progress.detail",
    ];

    // ── US-ADM-011b workflow-runtime approval events (US-NTF-006). MUST be declared before _byKey: the eager
    // BuildCatalog() at type-init references it, so a later declaration would leave it null → NRE in the type
    // initializer (Phase 2a lesson). The "decided" event adds "workflow.decision" on top of these. ──
    private static readonly string[] WorkflowPlaceholders =
    [
        "workflow.entityType", "workflow.stepOrder", "workflow.requestId",
        .. TenantPlaceholders,
    ];

    // ── US-TRN-001 training catalog events (US-NTF-006). MUST be declared before _byKey: the eager BuildCatalog()
    // at type-init references it, so a later declaration would leave it null → NRE in the type initializer (Phase 2a
    // lesson). The service builds a payload with course.title + employee.firstName/lastName + enrollment.status. ──
    private static readonly string[] TrainingPlaceholders =
    [
        "course.title", "employee.firstName", "employee.lastName", "enrollment.status",
        .. TenantPlaceholders,
    ];

    // ── Shared placeholders for the US-TRN-003 benefit-enrollment events (US-NTF-006). MUST be declared before
    // _byKey: the eager BuildCatalog() at type-init references it, so a later declaration would leave it null →
    // NRE in the type initializer (Phase 2a lesson). ──
    private static readonly string[] BenefitPlaceholders =
    [
        "plan.name", "employee.firstName", "employee.lastName", "enrollment.status",
        .. TenantPlaceholders,
    ];

    // ── Shared placeholders for the attendance-family events (US-NTF-006 Phase 6, US-ATT-001/004/006). The Real
    // service loads the subject employee for the name fields; the trigger site supplies the date / time / hours.
    // MUST be declared before _byKey: the eager BuildCatalog() at type-init references them, so a later declaration
    // would leave them null → NRE in the type initializer (Phase 2a lesson). ──
    private static readonly string[] AttendanceLatePlaceholders =
    [
        "employee.firstName", "employee.lastName",
        "attendance.date", "attendance.checkIn", "attendance.expected",
    ];

    private static readonly string[] RegularizationPlaceholders =
    [
        "employee.firstName", "employee.lastName",
        "attendance.date", "regularization.reason",
    ];

    private static readonly string[] OvertimeMaximaPlaceholders =
    [
        "employee.firstName", "employee.lastName",
        "overtime.hours", "overtime.limit", "overtime.period",
    ];

    private static readonly string[] OvertimePlaceholders =
    [
        "employee.firstName", "employee.lastName",
        "overtime.date", "overtime.hours",
    ];

    // ── Shared placeholders for the Core-HR events (US-NTF-006 Phase 7, US-CHR-008/009/011). The Real service loads
    // the subject employee for the name fields; the trigger site supplies the dates / counts / document metadata.
    // MUST be declared before _byKey: the eager BuildCatalog() at type-init references them, so a later declaration
    // would leave them null → NRE in the type initializer (Phase 2a lesson). ──
    private static readonly string[] ProbationEndingPlaceholders =
    [
        "employee.firstName", "employee.lastName", "employee.employeeNo",
        "probation.endDate", "probation.daysRemaining",
    ];

    private static readonly string[] ManagerReassignmentPlaceholders =
    [
        "manager.firstName", "manager.lastName", "manager.newStatus",
        "reassignment.directReportCount",
    ];

    private static readonly string[] DocumentExpiryPlaceholders =
    [
        "employee.firstName", "employee.lastName",
        "document.fileName", "document.category", "document.expiryDate", "document.daysUntilExpiry",
    ];

    private static readonly string[] ScheduledReportPlaceholders =
    [
        "report.type", "report.frequency", "report.downloadUrl",
    ];

    // ── Placeholders for the US-NTF-006 Phase 8 tail events. MUST be declared before _byKey: the eager
    // BuildCatalog() at type-init references them, so a later declaration would leave them null → NRE in the type
    // initializer (Phase 2a lesson). ──

    // bulk_import_completed (US-CHR-010): the initiator gets the import summary counts. Email-only — the recipient
    // is a raw address (BulkImportJob.InitiatedBy), so branding is via the shared tenant placeholders.
    private static readonly string[] BulkImportPlaceholders =
    [
        "import.total", "import.success", "import.failed", "import.jobId",
    ];

    // leave_report_ready (US-LV-012 FR-5/AC-5): the requester gets the "your export is ready" download link.
    private static readonly string[] LeaveReportPlaceholders =
    [
        "report.type", "report.downloadUrl",
    ];

    // applicant_portal_link (US-REC-008 FR-7, DF-41): the candidate's status-tracking magic link. Email-only — the
    // recipient is a raw applicant address (no User row). MUST be declared before _byKey (Phase 2a NRE lesson).
    private static readonly string[] ApplicantPortalLinkPlaceholders =
    [
        "applicant.firstName", "portal.url", "portal.expiresAt",
        .. TenantPlaceholders,
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

        // ── US-NTF-006 Phase 5a — recruitment applicant lifecycle (US-REC-002 FR-5/FR-7, US-REC-004 FR-6). The Real
        // service loads the applicant + vacancy for these fields; recipients are the candidate (email-only) or the
        // hiring manager + recruiter pool (in-app + email). ──
        yield return new NotificationEventDefinition(
            EventKey: "application_received",
            EventName: "Application Received",
            Placeholders: [.. ApplicantPlaceholders, .. TenantPlaceholders],
            SampleData: ApplicantSample(),
            DefaultSubject: "We've received your application for {{vacancy.title}}",
            DefaultBodyHtml:
                "<p>Hi {{applicant.firstName}},</p>" +
                "<p>Thank you for applying for the <strong>{{vacancy.title}}</strong> position. We've received your " +
                "application (reference <strong>{{application.reference}}</strong>) and our team will review it " +
                "shortly.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{applicant.firstName}},\n\n" +
                "Thank you for applying for the {{vacancy.title}} position. We've received your application " +
                "(reference {{application.reference}}) and our team will review it shortly.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.RecruitmentUpdates,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "application_new",
            EventName: "New Application Received",
            Placeholders: [.. ApplicantPlaceholders, .. TenantPlaceholders],
            SampleData: ApplicantSample(),
            DefaultSubject: "New application for {{vacancy.title}}",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>A new application has been received from <strong>{{applicant.firstName}} " +
                "{{applicant.lastName}}</strong> for the <strong>{{vacancy.title}}</strong> position " +
                "(reference {{application.reference}}).</p>" +
                "<p>Review it in the recruitment portal.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "A new application has been received from {{applicant.firstName}} {{applicant.lastName}} for the " +
                "{{vacancy.title}} position (reference {{application.reference}}).\n\n" +
                "Review it in the recruitment portal.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.RecruitmentUpdates,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "application_stage_changed",
            EventName: "Application Stage Updated",
            Placeholders: [.. ApplicantPlaceholders, .. TenantPlaceholders],
            SampleData: ApplicantSample(fromStage: "Screening", toStage: "Interview"),
            DefaultSubject: "Update on your application for {{vacancy.title}}",
            DefaultBodyHtml:
                "<p>Hi {{applicant.firstName}},</p>" +
                "<p>There's an update on your application for the <strong>{{vacancy.title}}</strong> position: it has " +
                "moved from <strong>{{application.fromStage}}</strong> to <strong>{{application.toStage}}</strong>.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{applicant.firstName}},\n\n" +
                "There's an update on your application for the {{vacancy.title}} position: it has moved from " +
                "{{application.fromStage}} to {{application.toStage}}.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.RecruitmentUpdates,
            IsMandatory: false);

        // ── US-NTF-006 Phase 5a — recruitment interview lifecycle (US-REC-005 FR-3/FR-4/BR-7). Recipients are the
        // candidate + interviewers (email-only — interviewers arrive as raw emails). ──
        yield return new NotificationEventDefinition(
            EventKey: "interview_scheduled",
            EventName: "Interview Scheduled",
            Placeholders: [.. InterviewPlaceholders, .. TenantPlaceholders],
            SampleData: InterviewSample(),
            DefaultSubject: "Interview scheduled for {{vacancy.title}}",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>An interview for the <strong>{{vacancy.title}}</strong> position has been scheduled for " +
                "<strong>{{interview.date}} at {{interview.time}}</strong> ({{interview.type}}). " +
                "Location / link: {{interview.location}}.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "An interview for the {{vacancy.title}} position has been scheduled for {{interview.date}} at " +
                "{{interview.time}} ({{interview.type}}). Location / link: {{interview.location}}.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.RecruitmentUpdates,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "interview_updated",
            EventName: "Interview Updated",
            Placeholders: [.. InterviewPlaceholders, .. TenantPlaceholders],
            SampleData: InterviewSample(),
            DefaultSubject: "Your interview for {{vacancy.title}} has been updated",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>The interview for the <strong>{{vacancy.title}}</strong> position has been updated. The new time " +
                "is <strong>{{interview.date}} at {{interview.time}}</strong> ({{interview.type}}). " +
                "Location / link: {{interview.location}}.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "The interview for the {{vacancy.title}} position has been updated. The new time is {{interview.date}} " +
                "at {{interview.time}} ({{interview.type}}). Location / link: {{interview.location}}.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.RecruitmentUpdates,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "interview_cancelled",
            EventName: "Interview Cancelled",
            Placeholders: [.. InterviewPlaceholders, .. TenantPlaceholders],
            SampleData: InterviewSample(),
            DefaultSubject: "Your interview for {{vacancy.title}} has been cancelled",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>The interview for the <strong>{{vacancy.title}}</strong> position previously scheduled for " +
                "{{interview.date}} at {{interview.time}} has been <strong>cancelled</strong>. We'll be in touch if " +
                "it is rescheduled.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "The interview for the {{vacancy.title}} position previously scheduled for {{interview.date}} at " +
                "{{interview.time}} has been cancelled. We'll be in touch if it is rescheduled.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.RecruitmentUpdates,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "interview_reminder",
            EventName: "Interview Reminder",
            Placeholders: [.. InterviewPlaceholders, .. TenantPlaceholders],
            SampleData: InterviewSample(),
            DefaultSubject: "Reminder: interview for {{vacancy.title}} on {{interview.date}}",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>This is a reminder that the interview for the <strong>{{vacancy.title}}</strong> position is " +
                "coming up on <strong>{{interview.date}} at {{interview.time}}</strong> ({{interview.type}}). " +
                "Location / link: {{interview.location}}.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "This is a reminder that the interview for the {{vacancy.title}} position is coming up on " +
                "{{interview.date}} at {{interview.time}} ({{interview.type}}). Location / link: " +
                "{{interview.location}}.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.RecruitmentUpdates,
            IsMandatory: false);

        // ── US-NTF-006 Phase 5a — scorecard submitted (US-REC-006 FR-5). Recipients are the recruiter pool
        // (in-app + email). ──
        yield return new NotificationEventDefinition(
            EventKey: "scorecard_submitted",
            EventName: "Scorecard Submitted",
            Placeholders: [.. ApplicantPlaceholders, .. TenantPlaceholders],
            SampleData: ApplicantSample(),
            DefaultSubject: "A scorecard was submitted for {{vacancy.title}}",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>An interviewer has submitted a scorecard for <strong>{{applicant.firstName}} " +
                "{{applicant.lastName}}</strong> ({{vacancy.title}}). Review it in the recruitment portal.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "An interviewer has submitted a scorecard for {{applicant.firstName}} {{applicant.lastName}} " +
                "({{vacancy.title}}). Review it in the recruitment portal.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.RecruitmentUpdates,
            IsMandatory: false);

        // ── US-NTF-006 Phase 5a — recruitment offer lifecycle (US-REC-007 FR-5/FR-7/FR-8). Candidate is the primary
        // recipient (email-only); expiry-reminder/expired also copy the recruiter pool. The offer_sent candidate leg
        // additionally carries the offer-letter PDF inline (Real service, via IEmailSender + IFileStorage). ──
        yield return new NotificationEventDefinition(
            EventKey: "offer_sent",
            EventName: "Offer Sent",
            Placeholders: [.. OfferPlaceholders, .. TenantPlaceholders],
            SampleData: OfferSample(),
            DefaultSubject: "Your offer for {{offer.position}}",
            DefaultBodyHtml:
                "<p>Hi {{applicant.firstName}},</p>" +
                "<p>Congratulations! We're delighted to offer you the <strong>{{offer.position}}</strong> position " +
                "(reference {{offer.reference}}), starting {{offer.startDate}}. Your offer letter is attached.</p>" +
                "<p>Please review and respond by <strong>{{offer.expiryDate}}</strong>.</p>" +
                "<p>Track or respond to your offer: <a href=\"{{offer.portalUrl}}\">{{offer.portalUrl}}</a></p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{applicant.firstName}},\n\n" +
                "Congratulations! We're delighted to offer you the {{offer.position}} position (reference " +
                "{{offer.reference}}), starting {{offer.startDate}}. Your offer letter is attached.\n\n" +
                "Please review and respond by {{offer.expiryDate}}.\n\n" +
                "Track or respond to your offer: {{offer.portalUrl}}\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.RecruitmentUpdates,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "offer_withdrawn",
            EventName: "Offer Withdrawn",
            Placeholders: [.. OfferPlaceholders, .. TenantPlaceholders],
            SampleData: OfferSample(),
            DefaultSubject: "Your offer for {{offer.position}} has been withdrawn",
            DefaultBodyHtml:
                "<p>Hi {{applicant.firstName}},</p>" +
                "<p>We're writing to let you know that the offer for the <strong>{{offer.position}}</strong> position " +
                "(reference {{offer.reference}}) has been <strong>withdrawn</strong>. Please contact us if you have " +
                "any questions.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{applicant.firstName}},\n\n" +
                "We're writing to let you know that the offer for the {{offer.position}} position (reference " +
                "{{offer.reference}}) has been withdrawn. Please contact us if you have any questions.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.RecruitmentUpdates,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "offer_expiry_reminder",
            EventName: "Offer Expiry Reminder",
            Placeholders: [.. OfferPlaceholders, .. TenantPlaceholders],
            SampleData: OfferSample(),
            DefaultSubject: "Reminder: your offer for {{offer.position}} expires soon",
            DefaultBodyHtml:
                "<p>Hi {{applicant.firstName}},</p>" +
                "<p>This is a reminder that your offer for the <strong>{{offer.position}}</strong> position " +
                "(reference {{offer.reference}}) expires on <strong>{{offer.expiryDate}}</strong>. Please respond " +
                "before then.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{applicant.firstName}},\n\n" +
                "This is a reminder that your offer for the {{offer.position}} position (reference {{offer.reference}}) " +
                "expires on {{offer.expiryDate}}. Please respond before then.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.RecruitmentUpdates,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "offer_expired",
            EventName: "Offer Expired",
            Placeholders: [.. OfferPlaceholders, .. TenantPlaceholders],
            SampleData: OfferSample(),
            DefaultSubject: "Your offer for {{offer.position}} has expired",
            DefaultBodyHtml:
                "<p>Hi {{applicant.firstName}},</p>" +
                "<p>Your offer for the <strong>{{offer.position}}</strong> position (reference {{offer.reference}}) " +
                "has <strong>expired</strong> as we did not receive a response by {{offer.expiryDate}}. Please " +
                "contact us if you're still interested.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{applicant.firstName}},\n\n" +
                "Your offer for the {{offer.position}} position (reference {{offer.reference}}) has expired as we did " +
                "not receive a response by {{offer.expiryDate}}. Please contact us if you're still interested.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.RecruitmentUpdates,
            IsMandatory: false);

        // ── US-REC-008 FR-7 (DF-41) — applicant status-tracking magic link. The candidate enters their email and,
        // when an application exists (BR-5), is emailed a self-contained magic link to the anonymous /portal route.
        // Email-only (external candidate, no User row); the raw token is embedded in {{portal.url}}. ──
        yield return new NotificationEventDefinition(
            EventKey: "applicant_portal_link",
            EventName: "Applicant Portal Link",
            Placeholders: [.. ApplicantPortalLinkPlaceholders],
            SampleData: ApplicantPortalLinkSample(),
            DefaultSubject: "Your application tracking link",
            DefaultBodyHtml:
                "<p>Hi {{applicant.firstName}},</p>" +
                "<p>Use the link below to track and manage your application. It expires on " +
                "<strong>{{portal.expiresAt}}</strong>.</p>" +
                "<p><a href=\"{{portal.url}}\">{{portal.url}}</a></p>" +
                "<p>If you didn't request this, you can safely ignore this email.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{applicant.firstName}},\n\n" +
                "Use the link below to track and manage your application. It expires on {{portal.expiresAt}}.\n\n" +
                "{{portal.url}}\n\n" +
                "If you didn't request this, you can safely ignore this email.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.RecruitmentUpdates,
            IsMandatory: false);

        // ── US-NTF-006 Phase 5b — performance goal-setting (US-PRF-001 AC-2/FR-7). Recipient is the goal's employee
        // (in-app + email via the linked user, else email-only fallback). The Real service loads the employee + cycle +
        // goal for these fields. ──
        yield return new NotificationEventDefinition(
            EventKey: "goal_assigned",
            EventName: "Goal Assigned",
            Placeholders: [.. GoalPlaceholders, .. TenantPlaceholders],
            SampleData: GoalSample(),
            DefaultSubject: "A new goal has been assigned to you",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your manager has assigned you a new goal — <strong>{{goal.title}}</strong> — for the " +
                "<strong>{{cycle.name}}</strong> cycle.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your manager has assigned you a new goal - {{goal.title}} - for the {{cycle.name}} cycle.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PerformanceReviews,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "goal_modified",
            EventName: "Goal Modified",
            Placeholders: [.. GoalPlaceholders, .. TenantPlaceholders],
            SampleData: GoalSample(),
            DefaultSubject: "One of your goals has been updated",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your manager has updated your goal — <strong>{{goal.title}}</strong> — for the " +
                "<strong>{{cycle.name}}</strong> cycle.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your manager has updated your goal - {{goal.title}} - for the {{cycle.name}} cycle.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PerformanceReviews,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "goal_removed",
            EventName: "Goal Removed",
            Placeholders: [.. GoalPlaceholders, .. TenantPlaceholders],
            SampleData: GoalSample(),
            DefaultSubject: "One of your goals has been removed",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your manager has removed your goal — <strong>{{goal.title}}</strong> — from the " +
                "<strong>{{cycle.name}}</strong> cycle.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your manager has removed your goal - {{goal.title}} - from the {{cycle.name}} cycle.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PerformanceReviews,
            IsMandatory: false);

        // ── US-NTF-006 Phase 5b — self-assessment (US-PRF-002 AC-2/AC-5/FR-7). ──
        yield return new NotificationEventDefinition(
            EventKey: "self_assessment_submitted",
            EventName: "Self-Assessment Submitted",
            Placeholders: [.. PerformancePlaceholders, .. TenantPlaceholders],
            SampleData: PerfSample(),
            DefaultSubject: "A self-assessment has been submitted for your review",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p><strong>{{employee.firstName}} {{employee.lastName}}</strong> has submitted their " +
                "self-assessment for the <strong>{{cycle.name}}</strong> cycle. It is ready for your review.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "{{employee.firstName}} {{employee.lastName}} has submitted their self-assessment for the " +
                "{{cycle.name}} cycle. It is ready for your review.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PerformanceReviews,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "self_assessment_reminder",
            EventName: "Self-Assessment Reminder",
            Placeholders: [.. PerformancePlaceholders, "reminder.daysUntilDeadline", .. TenantPlaceholders],
            SampleData: PerfSample(daysUntilDeadline: 3),
            DefaultSubject: "Reminder: your self-assessment is due soon",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your self-assessment for the <strong>{{cycle.name}}</strong> cycle has not been submitted yet " +
                "and is due in <strong>{{reminder.daysUntilDeadline}}</strong> day(s). Please complete it soon.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your self-assessment for the {{cycle.name}} cycle has not been submitted yet and is due in " +
                "{{reminder.daysUntilDeadline}} day(s). Please complete it soon.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PerformanceReviews,
            IsMandatory: false);

        // ── US-NTF-006 Phase 5b — manager review (US-PRF-003 AC-2). ──
        yield return new NotificationEventDefinition(
            EventKey: "manager_review_submitted",
            EventName: "Manager Review Submitted",
            Placeholders: [.. PerformancePlaceholders, .. TenantPlaceholders],
            SampleData: PerfSample(),
            DefaultSubject: "Your performance review has been submitted",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your manager has submitted your performance review for the <strong>{{cycle.name}}</strong> " +
                "cycle. You can view it in the performance portal.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your manager has submitted your performance review for the {{cycle.name}} cycle. You can view it " +
                "in the performance portal.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PerformanceReviews,
            IsMandatory: false);

        // ── US-NTF-006 Phase 5b — appraisal cycle events (US-PRF-004 AC-2/AC-4/AC-5/BR-6). A SINGLE parametrized event:
        // the caller's subtype (phase-start / deadline-reminder / phase-close / overdue-escalation / cycle-updated /
        // cycle-cancelled) is carried in event.subtype and a free-text note in event.detail. Recipient is the
        // participant employee. ──
        yield return new NotificationEventDefinition(
            EventKey: "cycle_event",
            EventName: "Appraisal Cycle Update",
            Placeholders: [.. CyclePlaceholders, .. TenantPlaceholders],
            SampleData: CycleEventSample(),
            DefaultSubject: "Update on the {{cycle.name}} performance cycle",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>There's an update on the <strong>{{cycle.name}}</strong> performance cycle: " +
                "<strong>{{event.subtype}}</strong>. {{event.detail}}</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "There's an update on the {{cycle.name}} performance cycle: {{event.subtype}}. {{event.detail}}\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PerformanceReviews,
            IsMandatory: false);

        // ── US-NTF-006 Phase 5b — 360-degree feedback (US-PRF-005 AC-2/AC-5/FR-8). Recipient is the reviewer. ──
        yield return new NotificationEventDefinition(
            EventKey: "reviewer_assigned",
            EventName: "360 Reviewer Assigned",
            Placeholders: [.. ReviewerPlaceholders, .. TenantPlaceholders],
            SampleData: ReviewerSample(),
            DefaultSubject: "You've been asked to provide feedback",
            DefaultBodyHtml:
                "<p>Hi {{reviewer.firstName}},</p>" +
                "<p>You've been asked to provide 360-degree feedback for <strong>{{reviewee.firstName}} " +
                "{{reviewee.lastName}}</strong> in the <strong>{{cycle.name}}</strong> cycle. A feedback form is " +
                "waiting for you.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{reviewer.firstName}},\n\n" +
                "You've been asked to provide 360-degree feedback for {{reviewee.firstName}} {{reviewee.lastName}} " +
                "in the {{cycle.name}} cycle. A feedback form is waiting for you.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PerformanceReviews,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "reviewer_reminder",
            EventName: "360 Reviewer Reminder",
            Placeholders: [.. ReviewerPlaceholders, .. TenantPlaceholders],
            SampleData: ReviewerSample(),
            DefaultSubject: "Reminder: your feedback is still pending",
            DefaultBodyHtml:
                "<p>Hi {{reviewer.firstName}},</p>" +
                "<p>This is a reminder to submit your 360-degree feedback for <strong>{{reviewee.firstName}} " +
                "{{reviewee.lastName}}</strong> in the <strong>{{cycle.name}}</strong> cycle before the deadline.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{reviewer.firstName}},\n\n" +
                "This is a reminder to submit your 360-degree feedback for {{reviewee.firstName}} " +
                "{{reviewee.lastName}} in the {{cycle.name}} cycle before the deadline.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PerformanceReviews,
            IsMandatory: false);

        // ── US-NTF-006 Phase 5b — review sign-off (US-PRF-006 AC-2/AC-3/BR-3/FR-5). ──
        yield return new NotificationEventDefinition(
            EventKey: "review_signoff_requested",
            EventName: "Review Sign-Off Requested",
            Placeholders: [.. PerformancePlaceholders, .. TenantPlaceholders],
            SampleData: PerfSample(),
            DefaultSubject: "Please sign off on your performance review",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your manager has requested your sign-off on your <strong>{{cycle.name}}</strong> performance " +
                "review. Please review the meeting notes and acknowledge them.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your manager has requested your sign-off on your {{cycle.name}} performance review. Please review " +
                "the meeting notes and acknowledge them.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PerformanceReviews,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "review_disputed",
            EventName: "Review Disputed",
            Placeholders: [.. PerformancePlaceholders, .. TenantPlaceholders],
            SampleData: PerfSample(),
            DefaultSubject: "A performance review has been disputed",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p><strong>{{employee.firstName}} {{employee.lastName}}</strong> has disputed their " +
                "<strong>{{cycle.name}}</strong> performance review. Please follow up to resolve it.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "{{employee.firstName}} {{employee.lastName}} has disputed their {{cycle.name}} performance review. " +
                "Please follow up to resolve it.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PerformanceReviews,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "review_auto_closed",
            EventName: "Review Auto-Closed",
            Placeholders: [.. PerformancePlaceholders, .. TenantPlaceholders],
            SampleData: PerfSample(),
            DefaultSubject: "A performance review was auto-closed",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>The <strong>{{cycle.name}}</strong> performance review for <strong>{{employee.firstName}} " +
                "{{employee.lastName}}</strong> was auto-closed with <em>No Response</em> because it was not signed " +
                "within the configured window.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "The {{cycle.name}} performance review for {{employee.firstName}} {{employee.lastName}} was " +
                "auto-closed with No Response because it was not signed within the configured window.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PerformanceReviews,
            IsMandatory: false);

        // ── US-NTF-006 Phase 5b — PIP events (US-PRF-008 AC-2/AC-3/AC-5/FR-3). A SINGLE parametrized event: the
        // caller's subtype (pip-initiated / pip-checkpoint-recorded / pip-checkpoint-reminder / pip-end-date-reminder /
        // pip-checkpoint-overdue / pip-extended / pip-completed / pip-not-met / pip-escalation-confirmed /
        // pip-not-acknowledged) is carried in pip.subtype and a free-text note in pip.detail. Recipient is the passed
        // stakeholder (employee / manager / mentor). ──
        yield return new NotificationEventDefinition(
            EventKey: "pip_event",
            EventName: "Performance Improvement Plan Update",
            Placeholders: [.. PipPlaceholders, .. TenantPlaceholders],
            SampleData: PipSample(),
            DefaultSubject: "Update on a performance improvement plan",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>There's an update on the performance improvement plan for <strong>{{employee.firstName}} " +
                "{{employee.lastName}}</strong>: <strong>{{pip.subtype}}</strong>. {{pip.detail}}</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "There's an update on the performance improvement plan for {{employee.firstName}} " +
                "{{employee.lastName}}: {{pip.subtype}}. {{pip.detail}}\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PerformanceReviews,
            IsMandatory: false);

        // ── US-NTF-006 Phase 5b — goal progress (US-PRF-009 AC-2/AC-5/FR-5/FR-6/BR-3). Three distinct subtypes +
        // a manager comment. Recipient is the manager (updated/blocked), HR (blocked broadcast, detail=="hr") or the
        // employee (stale-nudge / comment). ──
        yield return new NotificationEventDefinition(
            EventKey: "goal_progress_updated",
            EventName: "Goal Progress Updated",
            Placeholders: [.. GoalProgressPlaceholders, .. TenantPlaceholders],
            SampleData: GoalProgressSample(progressDetail: "60% / OnTrack"),
            DefaultSubject: "A goal progress update has been posted",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p><strong>{{employee.firstName}} {{employee.lastName}}</strong> posted a progress update on their " +
                "goal — <strong>{{goal.title}}</strong>: {{progress.detail}}.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "{{employee.firstName}} {{employee.lastName}} posted a progress update on their goal - " +
                "{{goal.title}}: {{progress.detail}}.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PerformanceReviews,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "goal_blocked",
            EventName: "Goal Blocked",
            Placeholders: [.. GoalProgressPlaceholders, .. TenantPlaceholders],
            SampleData: GoalProgressSample(),
            DefaultSubject: "A goal has been marked as blocked",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p><strong>{{employee.firstName}} {{employee.lastName}}</strong> has marked their goal — " +
                "<strong>{{goal.title}}</strong> — as <strong>Blocked</strong>. It may need your attention.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "{{employee.firstName}} {{employee.lastName}} has marked their goal - {{goal.title}} - as Blocked. " +
                "It may need your attention.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PerformanceReviews,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "goal_stale_nudge",
            EventName: "Goal Stale Nudge",
            Placeholders: [.. GoalProgressPlaceholders, .. TenantPlaceholders],
            SampleData: GoalProgressSample(),
            DefaultSubject: "One of your goals needs an update",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your goal — <strong>{{goal.title}}</strong> — hasn't been updated recently. Please post a " +
                "progress update to keep it on track.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your goal - {{goal.title}} - hasn't been updated recently. Please post a progress update to keep " +
                "it on track.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PerformanceReviews,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "goal_comment_added",
            EventName: "Goal Comment Added",
            Placeholders: [.. GoalProgressPlaceholders, .. TenantPlaceholders],
            SampleData: GoalProgressSample(),
            DefaultSubject: "A comment was added to your goal",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your manager added a comment to your goal — <strong>{{goal.title}}</strong>. View it in the " +
                "performance portal.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your manager added a comment to your goal - {{goal.title}}. View it in the performance portal.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.PerformanceReviews,
            IsMandatory: false);

        // ── US-ADM-011b — approval-workflow runtime events (US-NTF-006). Phase 2 wires Leave only, so these use
        // the LeaveUpdates category for now (011c can refine per entity type). Recipients: assignment → the newly
        // assigned approver; escalation → the escalation target or tenant admins; decided → the requester. ──
        yield return new NotificationEventDefinition(
            EventKey: "workflow_step_assigned",
            EventName: "Approval Step Assigned",
            Placeholders: [.. WorkflowPlaceholders],
            SampleData: WorkflowSample(),
            DefaultSubject: "You have a pending {{workflow.entityType}} approval",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>You have a pending <strong>{{workflow.entityType}}</strong> approval " +
                "(request {{workflow.requestId}}, step {{workflow.stepOrder}}).</p>" +
                "<p>Please review it in the HRM portal.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "You have a pending {{workflow.entityType}} approval (request {{workflow.requestId}}, " +
                "step {{workflow.stepOrder}}).\n\nPlease review it in the HRM portal.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.LeaveUpdates,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "workflow_step_escalated",
            EventName: "Approval Escalated",
            Placeholders: [.. WorkflowPlaceholders],
            SampleData: WorkflowSample(),
            DefaultSubject: "An overdue {{workflow.entityType}} approval has been escalated to you",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>An overdue <strong>{{workflow.entityType}}</strong> approval " +
                "(request {{workflow.requestId}}, step {{workflow.stepOrder}}) has been escalated to you.</p>" +
                "<p>Please review it in the HRM portal.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "An overdue {{workflow.entityType}} approval (request {{workflow.requestId}}, " +
                "step {{workflow.stepOrder}}) has been escalated to you.\n\nPlease review it in the HRM portal.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.LeaveUpdates,
            IsMandatory: false);

        // US-ADM-011c (AC-6/FR-9) — an approval step was delegated because its primary approver is on leave.
        // Recipient: the backup approver the step routed to (or, when no backup is configured, the tenant admins).
        yield return new NotificationEventDefinition(
            EventKey: "workflow_step_delegated",
            EventName: "Approval Delegated",
            Placeholders: [.. WorkflowPlaceholders],
            SampleData: WorkflowSample(),
            DefaultSubject: "A {{workflow.entityType}} approval has been delegated to you",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>A <strong>{{workflow.entityType}}</strong> approval " +
                "(request {{workflow.requestId}}, step {{workflow.stepOrder}}) has been delegated to you because " +
                "the assigned approver is on leave.</p>" +
                "<p>Please review it in the HRM portal.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "A {{workflow.entityType}} approval (request {{workflow.requestId}}, step {{workflow.stepOrder}}) " +
                "has been delegated to you because the assigned approver is on leave.\n\n" +
                "Please review it in the HRM portal.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.LeaveUpdates,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "workflow_request_decided",
            EventName: "Approval Decision",
            Placeholders: [.. WorkflowPlaceholders, "workflow.decision"],
            SampleData: WorkflowSample(decision: "approved"),
            DefaultSubject: "Your {{workflow.entityType}} request was {{workflow.decision}}",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>Your <strong>{{workflow.entityType}}</strong> request {{workflow.requestId}} was " +
                "<strong>{{workflow.decision}}</strong>.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "Your {{workflow.entityType}} request {{workflow.requestId}} was {{workflow.decision}}.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.LeaveUpdates,
            IsMandatory: false);

        // ── US-TRN-001 training catalog lifecycle (US-NTF-006). Recipient = the enrolled/affected employee.
        // No dedicated Training preference category exists, so these use OnboardingOffboarding (the closest
        // HR-development bucket) — see the ENH flagged in the implementation notes. ──
        yield return new NotificationEventDefinition(
            EventKey: "training_enrollment_confirmed",
            EventName: "Training Enrollment Confirmed",
            Placeholders: [.. TrainingPlaceholders],
            SampleData: TrainingSample(),
            DefaultSubject: "You're enrolled in {{course.title}}",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>You have been <strong>enrolled</strong> in <strong>{{course.title}}</strong>.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "You have been enrolled in {{course.title}}.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.OnboardingOffboarding,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "training_waitlisted",
            EventName: "Training Waitlisted",
            Placeholders: [.. TrainingPlaceholders],
            SampleData: TrainingSample(status: "Waitlisted"),
            DefaultSubject: "You're on the waitlist for {{course.title}}",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p><strong>{{course.title}}</strong> is currently full, so you have been placed on the " +
                "<strong>waitlist</strong>. We'll notify you if a seat frees up.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "{{course.title}} is currently full, so you have been placed on the waitlist. We'll notify you if " +
                "a seat frees up.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.OnboardingOffboarding,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "training_waitlist_promoted",
            EventName: "Training Waitlist Promoted",
            Placeholders: [.. TrainingPlaceholders],
            SampleData: TrainingSample(),
            DefaultSubject: "A seat opened up — you're now enrolled in {{course.title}}",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Good news — a seat has opened up and you have been <strong>promoted from the waitlist</strong> " +
                "to enrolled in <strong>{{course.title}}</strong>.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Good news - a seat has opened up and you have been promoted from the waitlist to enrolled in " +
                "{{course.title}}.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.OnboardingOffboarding,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "training_enrollment_cancelled",
            EventName: "Training Enrollment Cancelled",
            Placeholders: [.. TrainingPlaceholders],
            SampleData: TrainingSample(status: "Cancelled"),
            DefaultSubject: "Your enrollment in {{course.title}} was cancelled",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your enrollment in <strong>{{course.title}}</strong> has been <strong>cancelled</strong>.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your enrollment in {{course.title}} has been cancelled.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.OnboardingOffboarding,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "training_completed",
            EventName: "Training Completed",
            Placeholders: [.. TrainingPlaceholders],
            SampleData: TrainingSample(status: "Completed"),
            DefaultSubject: "You've completed {{course.title}}",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your completion of <strong>{{course.title}}</strong> has been recorded. Well done!</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your completion of {{course.title}} has been recorded. Well done!\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.OnboardingOffboarding,
            IsMandatory: false);

        // ── US-TRN-003 benefit-enrollment lifecycle (US-NTF-006). Recipient = the affected employee. No dedicated
        // Benefits preference category exists, so these use OnboardingOffboarding (the closest HR bucket) — see the
        // ENH flagged for the training events; the same applies here. ──
        yield return new NotificationEventDefinition(
            EventKey: "benefit_enrolled",
            EventName: "Benefit Enrollment Confirmed",
            Placeholders: [.. BenefitPlaceholders],
            SampleData: BenefitSample(),
            DefaultSubject: "You're enrolled in {{plan.name}}",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your enrollment in <strong>{{plan.name}}</strong> is <strong>confirmed</strong>.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your enrollment in {{plan.name}} is confirmed.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.OnboardingOffboarding,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "benefit_terminated",
            EventName: "Benefit Enrollment Terminated",
            Placeholders: [.. BenefitPlaceholders],
            SampleData: BenefitSample(status: "Terminated"),
            DefaultSubject: "Your enrollment in {{plan.name}} has ended",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your enrollment in <strong>{{plan.name}}</strong> has been <strong>terminated</strong>.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your enrollment in {{plan.name}} has been terminated.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.OnboardingOffboarding,
            IsMandatory: false);

        // ── US-NTF-006 Phase 6 — attendance / overtime / regularization delivery (US-ATT-001 FR-5, US-ATT-004
        // FR-4/FR-5, US-ATT-006 FR-8). Recipients are resolved in the Real service (the employee, the reporting-line
        // manager, or the attendance-admin/HR pool); the payload carries the date / time / hours the trigger site
        // has in scope. All AttendanceAlerts, none mandatory. ──
        yield return new NotificationEventDefinition(
            EventKey: "attendance_late",
            EventName: "Attendance Marked Late",
            Placeholders: [.. AttendanceLatePlaceholders, .. TenantPlaceholders],
            SampleData: AttendanceLateSample(),
            DefaultSubject: "You were marked late on {{attendance.date}}",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your clock-in on <strong>{{attendance.date}}</strong> at {{attendance.checkIn}} was after the " +
                "expected start time of {{attendance.expected}}, so it has been marked <strong>late</strong>.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your clock-in on {{attendance.date}} at {{attendance.checkIn}} was after the expected start time " +
                "of {{attendance.expected}}, so it has been marked late.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.AttendanceAlerts,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "attendance_regularization_requested",
            EventName: "Attendance Regularization Requested",
            Placeholders: [.. RegularizationPlaceholders, .. TenantPlaceholders],
            SampleData: RegularizationSample(),
            DefaultSubject: "A regularization request needs your approval",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p><strong>{{employee.firstName}} {{employee.lastName}}</strong> has submitted an attendance " +
                "regularization request for <strong>{{attendance.date}}</strong> that needs your approval.</p>" +
                "<p>Reason: {{regularization.reason}}</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "{{employee.firstName}} {{employee.lastName}} has submitted an attendance regularization request " +
                "for {{attendance.date}} that needs your approval.\n\n" +
                "Reason: {{regularization.reason}}\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.AttendanceAlerts,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "attendance_regularization_approved",
            EventName: "Attendance Regularization Approved",
            Placeholders: [.. RegularizationPlaceholders, .. TenantPlaceholders],
            SampleData: RegularizationSample(),
            DefaultSubject: "Your regularization request for {{attendance.date}} was approved",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your attendance regularization request for <strong>{{attendance.date}}</strong> has been " +
                "<strong>approved</strong>.</p>" +
                "<p>Reason: {{regularization.reason}}</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your attendance regularization request for {{attendance.date}} has been approved.\n\n" +
                "Reason: {{regularization.reason}}\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.AttendanceAlerts,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "attendance_regularization_rejected",
            EventName: "Attendance Regularization Rejected",
            Placeholders: [.. RegularizationPlaceholders, .. TenantPlaceholders],
            SampleData: RegularizationSample(reason: "Insufficient supporting evidence for the requested change."),
            DefaultSubject: "Your regularization request for {{attendance.date}} was rejected",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your attendance regularization request for <strong>{{attendance.date}}</strong> has been " +
                "<strong>rejected</strong>.</p>" +
                "<p>Reason: {{regularization.reason}}</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your attendance regularization request for {{attendance.date}} has been rejected.\n\n" +
                "Reason: {{regularization.reason}}\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.AttendanceAlerts,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "overtime_maxima_exceeded",
            EventName: "Overtime Maxima Exceeded",
            Placeholders: [.. OvertimeMaximaPlaceholders, .. TenantPlaceholders],
            SampleData: OvertimeMaximaSample(),
            DefaultSubject: "Overtime limit exceeded for {{employee.firstName}} {{employee.lastName}}",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>Recorded overtime for <strong>{{employee.firstName}} {{employee.lastName}}</strong> " +
                "({{overtime.hours}} hour(s)) has exceeded the {{overtime.period}} maximum of " +
                "{{overtime.limit}} hour(s).</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "Recorded overtime for {{employee.firstName}} {{employee.lastName}} ({{overtime.hours}} hour(s)) " +
                "has exceeded the {{overtime.period}} maximum of {{overtime.limit}} hour(s).\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.AttendanceAlerts,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "overtime_preapproval_requested",
            EventName: "Overtime Pre-Approval Requested",
            Placeholders: [.. OvertimePlaceholders, .. TenantPlaceholders],
            SampleData: OvertimeSample(),
            DefaultSubject: "An overtime pre-approval needs your review",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p><strong>{{employee.firstName}} {{employee.lastName}}</strong> has requested pre-approval for " +
                "<strong>{{overtime.hours}}</strong> hour(s) of overtime on {{overtime.date}}.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "{{employee.firstName}} {{employee.lastName}} has requested pre-approval for {{overtime.hours}} " +
                "hour(s) of overtime on {{overtime.date}}.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.AttendanceAlerts,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "overtime_approved",
            EventName: "Overtime Approved",
            Placeholders: [.. OvertimePlaceholders, .. TenantPlaceholders],
            SampleData: OvertimeSample(),
            DefaultSubject: "Your overtime for {{overtime.date}} was approved",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your overtime of <strong>{{overtime.hours}}</strong> hour(s) on {{overtime.date}} has been " +
                "<strong>approved</strong>.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your overtime of {{overtime.hours}} hour(s) on {{overtime.date}} has been approved.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.AttendanceAlerts,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "overtime_rejected",
            EventName: "Overtime Rejected",
            Placeholders: [.. OvertimePlaceholders, "overtime.reason", .. TenantPlaceholders],
            SampleData: OvertimeSample(reason: "The overtime was not pre-authorized for this period."),
            DefaultSubject: "Your overtime for {{overtime.date}} was rejected",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your overtime of <strong>{{overtime.hours}}</strong> hour(s) on {{overtime.date}} has been " +
                "<strong>rejected</strong>.</p>" +
                "<p>Reason: {{overtime.reason}}</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your overtime of {{overtime.hours}} hour(s) on {{overtime.date}} has been rejected.\n\n" +
                "Reason: {{overtime.reason}}\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.AttendanceAlerts,
            IsMandatory: false);

        // ── US-NTF-006 Phase 7 — Core-HR inline "deferred notify" sites (US-CHR-008 FR-8/BR-4, US-CHR-009 FR-6/BR-6,
        // US-CHR-011 BR-4). Recipients are resolved in the Real service (the HR pool, or the document-owner employee);
        // the payload carries the dates / counts / document metadata the trigger site has in scope. None mandatory. ──
        yield return new NotificationEventDefinition(
            EventKey: "employee_probation_ending",
            EventName: "Employee Probation Ending",
            Placeholders: [.. ProbationEndingPlaceholders, .. TenantPlaceholders],
            SampleData: ProbationEndingSample(),
            DefaultSubject: "Probation ending soon for {{employee.firstName}} {{employee.lastName}}",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p><strong>{{employee.firstName}} {{employee.lastName}}</strong> (employee no. " +
                "{{employee.employeeNo}}) has probation ending on <strong>{{probation.endDate}}</strong> " +
                "({{probation.daysRemaining}} day(s) remaining).</p>" +
                "<p>Please confirm the transition to Active or extend the probation.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "{{employee.firstName}} {{employee.lastName}} (employee no. {{employee.employeeNo}}) has probation " +
                "ending on {{probation.endDate}} ({{probation.daysRemaining}} day(s) remaining).\n\n" +
                "Please confirm the transition to Active or extend the probation.\n\n" +
                "Regards,\n{{tenant.companyName}}",
            Category: NotificationCategory.OnboardingOffboarding,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "manager_reassignment_needed",
            EventName: "Manager Reassignment Needed",
            Placeholders: [.. ManagerReassignmentPlaceholders, .. TenantPlaceholders],
            SampleData: ManagerReassignmentSample(),
            DefaultSubject: "Direct reports need reassignment for {{manager.firstName}} {{manager.lastName}}",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p><strong>{{manager.firstName}} {{manager.lastName}}</strong> has been " +
                "<strong>{{manager.newStatus}}</strong> and has {{reassignment.directReportCount}} direct report(s) " +
                "that need to be reassigned to another manager.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "{{manager.firstName}} {{manager.lastName}} has been {{manager.newStatus}} and has " +
                "{{reassignment.directReportCount}} direct report(s) that need to be reassigned to another " +
                "manager.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.OnboardingOffboarding,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "document_expiry_warning",
            EventName: "Document Expiry Warning",
            Placeholders: [.. DocumentExpiryPlaceholders, .. TenantPlaceholders],
            SampleData: DocumentExpirySample(),
            DefaultSubject: "Your document {{document.fileName}} expires on {{document.expiryDate}}",
            DefaultBodyHtml:
                "<p>Hi {{employee.firstName}},</p>" +
                "<p>Your document <strong>{{document.fileName}}</strong> ({{document.category}}) expires on " +
                "<strong>{{document.expiryDate}}</strong> ({{document.daysUntilExpiry}} day(s) remaining).</p>" +
                "<p>Please upload a renewed copy in the HRM portal.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hi {{employee.firstName}},\n\n" +
                "Your document {{document.fileName}} ({{document.category}}) expires on {{document.expiryDate}} " +
                "({{document.daysUntilExpiry}} day(s) remaining).\n\n" +
                "Please upload a renewed copy in the HRM portal.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.SystemAnnouncements,
            IsMandatory: false);

        yield return new NotificationEventDefinition(
            EventKey: "scheduled_report_ready",
            EventName: "Scheduled Report Ready",
            Placeholders: [.. ScheduledReportPlaceholders, .. TenantPlaceholders],
            SampleData: ScheduledReportSample(),
            DefaultSubject: "Your scheduled {{report.type}} report is ready",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>Your scheduled <strong>{{report.type}}</strong> report ({{report.frequency}}) has been " +
                "generated and is ready to download.</p>" +
                "<p><a href=\"{{report.downloadUrl}}\">Download your report</a></p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "Your scheduled {{report.type}} report ({{report.frequency}}) has been generated and is ready to " +
                "download.\n\n{{report.downloadUrl}}\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.SystemAnnouncements,
            IsMandatory: false);

        // ── US-NTF-006 Phase 8 — bulk employee import completed (US-CHR-010). Email-only to the initiator (a raw
        // address recorded on the job); carries the total/success/failed counts. ──
        yield return new NotificationEventDefinition(
            EventKey: "bulk_import_completed",
            EventName: "Bulk Employee Import Completed",
            Placeholders: [.. BulkImportPlaceholders, .. TenantPlaceholders],
            SampleData: BulkImportSample(),
            DefaultSubject: "Your employee import has completed",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>Your employee import has finished processing. Of <strong>{{import.total}}</strong> row(s), " +
                "<strong>{{import.success}}</strong> succeeded and <strong>{{import.failed}}</strong> failed.</p>" +
                "<p>Sign in to the HRM portal to review the results and download the error report for any failed " +
                "rows.</p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "Your employee import has finished processing. Of {{import.total}} row(s), {{import.success}} " +
                "succeeded and {{import.failed}} failed.\n\n" +
                "Sign in to the HRM portal to review the results and download the error report for any failed " +
                "rows.\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.SystemAnnouncements,
            IsMandatory: false);

        // ── US-NTF-006 Phase 8 — leave report export ready (US-LV-012 FR-5/AC-5). In-app + email to the requester;
        // carries the report type + download link. ──
        yield return new NotificationEventDefinition(
            EventKey: "leave_report_ready",
            EventName: "Leave Report Ready",
            Placeholders: [.. LeaveReportPlaceholders, .. TenantPlaceholders],
            SampleData: LeaveReportSample(),
            DefaultSubject: "Your {{report.type}} leave report is ready",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>Your <strong>{{report.type}}</strong> leave report has been generated and is ready to " +
                "download.</p>" +
                "<p><a href=\"{{report.downloadUrl}}\">Download your report</a></p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "Your {{report.type}} leave report has been generated and is ready to download.\n\n" +
                "{{report.downloadUrl}}\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.LeaveUpdates,
            IsMandatory: false);

        // ── US-NTF-006 (P2-1d) — attendance summary export ready (US-ATT-007 FR-7). In-app + email to the
        // requester; carries the report period (YYYY-MM) + download link. Mirrors leave_report_ready. ──
        yield return new NotificationEventDefinition(
            EventKey: "attendance_summary_export_ready",
            EventName: "Attendance Summary Export Ready",
            Placeholders: [.. LeaveReportPlaceholders, .. TenantPlaceholders],
            SampleData: AttendanceExportSample(),
            DefaultSubject: "Your {{report.type}} attendance summary is ready",
            DefaultBodyHtml:
                "<p>Hello,</p>" +
                "<p>Your <strong>{{report.type}}</strong> attendance summary has been generated and is ready to " +
                "download.</p>" +
                "<p><a href=\"{{report.downloadUrl}}\">Download your report</a></p>" +
                "<p>Regards,<br/>{{tenant.companyName}}</p>",
            DefaultBodyText:
                "Hello,\n\n" +
                "Your {{report.type}} attendance summary has been generated and is ready to download.\n\n" +
                "{{report.downloadUrl}}\n\nRegards,\n{{tenant.companyName}}",
            Category: NotificationCategory.SystemAnnouncements,
            IsMandatory: false);
    }

    private static Dictionary<string, object?> AttendanceExportSample() => new()
    {
        ["report"] = new Dictionary<string, object?>
        {
            ["type"] = "2026-06",
            ["downloadUrl"] = "https://app.example.com/reports/019f2607/download?token=sample",
        },
        ["tenant"] = SampleTenant(),
    };

    // ── Sample-data for the US-NTF-006 Phase 8 tail events. ──
    private static Dictionary<string, object?> BulkImportSample() => new()
    {
        ["import"] = new Dictionary<string, object?>
        {
            ["total"] = 50, ["success"] = 47, ["failed"] = 3,
            ["jobId"] = "019f2607-0000-7000-8000-000000000000",
        },
        ["tenant"] = SampleTenant(),
    };

    private static Dictionary<string, object?> LeaveReportSample() => new()
    {
        ["report"] = new Dictionary<string, object?>
        {
            ["type"] = "BalanceSummary",
            ["downloadUrl"] = "https://app.example.com/reports/019f2607/download?token=sample",
        },
        ["tenant"] = SampleTenant(),
    };

    // ── Sample-data for the Core-HR events (US-NTF-006 Phase 7). ──
    private static Dictionary<string, object?> ProbationEndingSample() => new()
    {
        ["employee"] = new Dictionary<string, object?>
        {
            ["firstName"] = "Jane", ["lastName"] = "Doe", ["employeeNo"] = "EMP-0042",
        },
        ["probation"] = new Dictionary<string, object?> { ["endDate"] = "2026-07-08", ["daysRemaining"] = 5 },
        ["tenant"] = SampleTenant(),
    };

    private static Dictionary<string, object?> ManagerReassignmentSample() => new()
    {
        ["manager"] = new Dictionary<string, object?>
        {
            ["firstName"] = "Morgan", ["lastName"] = "Hale", ["newStatus"] = "Terminated",
        },
        ["reassignment"] = new Dictionary<string, object?> { ["directReportCount"] = 4 },
        ["tenant"] = SampleTenant(),
    };

    private static Dictionary<string, object?> DocumentExpirySample() => new()
    {
        ["employee"] = new Dictionary<string, object?> { ["firstName"] = "Jane", ["lastName"] = "Doe" },
        ["document"] = new Dictionary<string, object?>
        {
            ["fileName"] = "passport.pdf", ["category"] = "Passport",
            ["expiryDate"] = "2026-08-01", ["daysUntilExpiry"] = 30,
        },
        ["tenant"] = SampleTenant(),
    };

    private static Dictionary<string, object?> ScheduledReportSample() => new()
    {
        ["report"] = new Dictionary<string, object?>
        {
            ["type"] = "Attendance Summary", ["frequency"] = "MONTHLY",
            ["downloadUrl"] = "https://app.example.com/reports/019f2607/download?token=sample",
        },
        ["tenant"] = SampleTenant(),
    };

    // ── Sample-data for the attendance-family events (US-NTF-006 Phase 6). ──
    private static Dictionary<string, object?> AttendanceLateSample() => new()
    {
        ["employee"] = new Dictionary<string, object?> { ["firstName"] = "Jane", ["lastName"] = "Doe" },
        ["attendance"] = new Dictionary<string, object?>
        {
            ["date"] = "2026-07-01", ["checkIn"] = "09:22", ["expected"] = "09:00",
        },
        ["tenant"] = SampleTenant(),
    };

    private static Dictionary<string, object?> RegularizationSample(string? reason = null) => new()
    {
        ["employee"] = new Dictionary<string, object?> { ["firstName"] = "Jane", ["lastName"] = "Doe" },
        ["attendance"] = new Dictionary<string, object?> { ["date"] = "2026-07-01" },
        ["regularization"] = new Dictionary<string, object?>
        {
            ["reason"] = reason ?? "Forgot to clock in; badge records confirm on-site presence.",
        },
        ["tenant"] = SampleTenant(),
    };

    private static Dictionary<string, object?> OvertimeMaximaSample() => new()
    {
        ["employee"] = new Dictionary<string, object?> { ["firstName"] = "Jane", ["lastName"] = "Doe" },
        ["overtime"] = new Dictionary<string, object?>
        {
            ["hours"] = "3.5", ["limit"] = "3.0", ["period"] = "daily",
        },
        ["tenant"] = SampleTenant(),
    };

    private static Dictionary<string, object?> OvertimeSample(string? reason = null)
    {
        var overtime = new Dictionary<string, object?> { ["date"] = "2026-07-01", ["hours"] = "2.0" };
        if (reason is not null) overtime["reason"] = reason;
        return new Dictionary<string, object?>
        {
            ["employee"] = new Dictionary<string, object?> { ["firstName"] = "Jane", ["lastName"] = "Doe" },
            ["overtime"] = overtime,
            ["tenant"] = SampleTenant(),
        };
    }

    // ── Sample-data for the US-TRN-001 training events. ──
    private static Dictionary<string, object?> TrainingSample(string status = "Enrolled") => new()
    {
        ["course"] = new Dictionary<string, object?> { ["title"] = "Workplace Safety 101" },
        ["employee"] = new Dictionary<string, object?> { ["firstName"] = "Jane", ["lastName"] = "Doe" },
        ["enrollment"] = new Dictionary<string, object?> { ["status"] = status },
        ["tenant"] = SampleTenant(),
    };

    // ── Sample-data for the US-TRN-003 benefit-enrollment events. ──
    private static Dictionary<string, object?> BenefitSample(string status = "Active") => new()
    {
        ["plan"] = new Dictionary<string, object?> { ["name"] = "Gold Health" },
        ["employee"] = new Dictionary<string, object?> { ["firstName"] = "Jane", ["lastName"] = "Doe" },
        ["enrollment"] = new Dictionary<string, object?> { ["status"] = status },
        ["tenant"] = SampleTenant(),
    };

    // ── Sample-data for the US-ADM-011b workflow-runtime events. ──
    private static Dictionary<string, object?> WorkflowSample(string? decision = null)
    {
        var workflow = new Dictionary<string, object?>
        {
            ["entityType"] = "Leave",
            ["stepOrder"] = 1,
            ["requestId"] = "019f2607-0000-7000-8000-000000000000",
        };
        if (decision is not null)
            workflow["decision"] = decision;
        return new Dictionary<string, object?>
        {
            ["workflow"] = workflow,
            ["tenant"] = SampleTenant(),
        };
    }

    // ── Sample-data for the performance events (US-NTF-006 Phase 5b). ──
    private static Dictionary<string, object?> GoalSample() => new()
    {
        ["employee"] = new Dictionary<string, object?>
        {
            ["firstName"] = "Jane", ["lastName"] = "Doe", ["email"] = "jane.doe@example.com",
        },
        ["cycle"] = new Dictionary<string, object?> { ["name"] = "H1 2026 Appraisal" },
        ["goal"] = new Dictionary<string, object?> { ["title"] = "Improve release cadence" },
        ["tenant"] = SampleTenant(),
    };

    private static Dictionary<string, object?> PerfSample(int? daysUntilDeadline = null)
    {
        var data = new Dictionary<string, object?>
        {
            ["employee"] = new Dictionary<string, object?>
            {
                ["firstName"] = "Jane", ["lastName"] = "Doe", ["email"] = "jane.doe@example.com",
            },
            ["cycle"] = new Dictionary<string, object?> { ["name"] = "H1 2026 Appraisal" },
            ["tenant"] = SampleTenant(),
        };
        if (daysUntilDeadline is not null)
            data["reminder"] = new Dictionary<string, object?> { ["daysUntilDeadline"] = daysUntilDeadline };
        return data;
    }

    private static Dictionary<string, object?> CycleEventSample() => new()
    {
        ["employee"] = new Dictionary<string, object?>
        {
            ["firstName"] = "Jane", ["lastName"] = "Doe", ["email"] = "jane.doe@example.com",
        },
        ["cycle"] = new Dictionary<string, object?> { ["name"] = "H1 2026 Appraisal" },
        ["event"] = new Dictionary<string, object?>
        {
            ["subtype"] = "phase-start", ["detail"] = "The Self-Assessment phase has started.",
        },
        ["tenant"] = SampleTenant(),
    };

    private static Dictionary<string, object?> ReviewerSample() => new()
    {
        ["reviewer"] = new Dictionary<string, object?> { ["firstName"] = "Sam" },
        ["reviewee"] = new Dictionary<string, object?> { ["firstName"] = "Jane", ["lastName"] = "Doe" },
        ["cycle"] = new Dictionary<string, object?> { ["name"] = "H1 2026 Appraisal" },
        ["tenant"] = SampleTenant(),
    };

    private static Dictionary<string, object?> PipSample() => new()
    {
        ["employee"] = new Dictionary<string, object?>
        {
            ["firstName"] = "Jane", ["lastName"] = "Doe", ["email"] = "jane.doe@example.com",
        },
        ["pip"] = new Dictionary<string, object?>
        {
            ["subtype"] = "pip-initiated", ["detail"] = "A 60-day improvement plan has been created.",
        },
        ["tenant"] = SampleTenant(),
    };

    private static Dictionary<string, object?> GoalProgressSample(string? progressDetail = null) => new()
    {
        ["employee"] = new Dictionary<string, object?>
        {
            ["firstName"] = "Jane", ["lastName"] = "Doe", ["email"] = "jane.doe@example.com",
        },
        ["goal"] = new Dictionary<string, object?> { ["title"] = "Improve release cadence" },
        ["progress"] = new Dictionary<string, object?> { ["detail"] = progressDetail ?? string.Empty },
        ["tenant"] = SampleTenant(),
    };

    // ── Sample-data for the recruitment applicant events (US-NTF-006 Phase 5a). ──
    private static Dictionary<string, object?> ApplicantSample(string? fromStage = null, string? toStage = null)
    {
        var application = new Dictionary<string, object?> { ["reference"] = "APP-2026-000123" };
        if (fromStage is not null) application["fromStage"] = fromStage;
        if (toStage is not null) application["toStage"] = toStage;
        return new Dictionary<string, object?>
        {
            ["applicant"] = new Dictionary<string, object?>
            {
                ["firstName"] = "Jordan", ["lastName"] = "Rivera", ["email"] = "jordan.rivera@example.com",
            },
            ["vacancy"] = new Dictionary<string, object?> { ["title"] = "Senior Software Engineer" },
            ["application"] = application,
            ["tenant"] = SampleTenant(),
        };
    }

    // ── Sample-data for the applicant status-tracking magic link (US-REC-008 FR-7, DF-41). ──
    private static Dictionary<string, object?> ApplicantPortalLinkSample() => new()
    {
        ["applicant"] = new Dictionary<string, object?> { ["firstName"] = "Jordan" },
        ["portal"] = new Dictionary<string, object?>
        {
            ["url"] = "https://acme.yourhrm.com/portal?token=sample-token",
            ["expiresAt"] = "2026-08-18",
        },
        ["tenant"] = SampleTenant(),
    };

    // ── Sample-data for the recruitment interview events (US-NTF-006 Phase 5a). ──
    private static Dictionary<string, object?> InterviewSample() => new()
    {
        ["applicant"] = new Dictionary<string, object?> { ["email"] = "jordan.rivera@example.com" },
        ["vacancy"] = new Dictionary<string, object?> { ["title"] = "Senior Software Engineer" },
        ["interview"] = new Dictionary<string, object?>
        {
            ["date"] = "2026-07-20", ["time"] = "10:30", ["type"] = "Video",
            ["location"] = "https://meet.example.com/abc-defg-hij",
        },
        ["tenant"] = SampleTenant(),
    };

    // ── Sample-data for the recruitment offer events (US-NTF-006 Phase 5a). ──
    private static Dictionary<string, object?> OfferSample() => new()
    {
        ["applicant"] = new Dictionary<string, object?>
        {
            ["firstName"] = "Jordan", ["email"] = "jordan.rivera@example.com",
        },
        ["vacancy"] = new Dictionary<string, object?> { ["title"] = "Senior Software Engineer" },
        ["offer"] = new Dictionary<string, object?>
        {
            ["reference"] = "OFR-2026-000045", ["position"] = "Senior Software Engineer",
            ["startDate"] = "2026-08-01", ["expiryDate"] = "2026-07-25",
            ["portalUrl"] = "https://acme.yourhrm.com/portal?token=sample-token",
        },
        ["tenant"] = SampleTenant(),
    };

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
