namespace HRM.Domain.Notifications;

/// <summary>
/// One entry in the <see cref="NotificationEventCatalog"/> (US-NTF-002 AC-1/AC-2). Describes an email event
/// type: its machine key, human-readable name, the placeholder variables available to that event's templates
/// (FR-3 variable-reference panel), the sample-data dictionary used for the live preview (FR-4/AC-2), and the
/// seeded system-default subject/HTML/text bodies (BR-2 — every event has a default).
/// </summary>
public sealed record NotificationEventDefinition(
    string EventKey,
    string EventName,
    IReadOnlyList<string> Placeholders,
    IReadOnlyDictionary<string, object?> SampleData,
    string DefaultSubject,
    string DefaultBodyHtml,
    string DefaultBodyText);

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
                "has been approved by {{approver.name}}.\n\nRegards,\n{{tenant.companyName}}");

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
                "Your manager {{manager.name}} will be in touch.\n\nRegards,\n{{tenant.companyName}}");

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
                "employee portal.\n\nRegards,\n{{tenant.companyName}}");

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
                "If you didn't request this, you can safely ignore this email.\n\nRegards,\n{{tenant.companyName}}");
    }

    private static Dictionary<string, object?> SampleTenant() => new()
    {
        ["companyName"] = "Acme Corporation",
        ["logoUrl"] = "https://app.example.com/logo.png",
        ["supportEmail"] = "support@acme.example.com",
    };
}
