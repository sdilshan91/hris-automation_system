using System.Text.Json;
using HRM.Api.Jobs;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Notifications;
using HRM.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace HRM.Api.Notifications;

/// <summary>
/// The real <see cref="INotificationDispatcher"/> (US-NTF-006 delivery infrastructure) that replaces the
/// log-only <see cref="LoggingNotificationDispatcher"/>. It wires the two legs of a notification:
/// <list type="bullet">
/// <item><b>In-app</b> — delegates to <see cref="INotificationService"/> (persist + SignalR push).</item>
/// <item><b>Email</b> — resolves the recipient's address, applies the per-user preference gate
/// (<see cref="INotificationPreferenceService.ShouldDeliverAsync"/>, with a BR-1 bypass for non-suppressible
/// security types), renders the tenant's email template (<see cref="IEmailTemplateService"/>), records a
/// <see cref="NotificationDelivery"/> row, and enqueues the Hangfire <see cref="SendEmailJob"/>.</item>
/// </list>
///
/// <para>Job-safe: the email leg opens its OWN DI scope and restores the tenant context from the passed tenant id
/// (the dispatcher can be called from the onboarding outbox worker, which runs outside a resolved
/// ITenantContext). Restoring the context is REQUIRED so <see cref="IEmailTemplateService"/> resolves the correct
/// tenant's template override through the EF global query filter (never another tenant's).</para>
///
/// <para><b>Safe with no SMTP.</b> The actual send goes through <see cref="IEmailSender"/>, which is the log-only
/// stub unless <c>Smtp:Host</c> is configured — so this dispatcher can be the default with no SMTP server. Phase 1
/// lands the infrastructure only; the 12 module <c>LogOnly*</c> seams are NOT rewired onto it yet (Phase 2+).</para>
/// </summary>
public sealed class RealNotificationDispatcher : INotificationDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationService _notificationService;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly ILogger<RealNotificationDispatcher> _logger;

    public RealNotificationDispatcher(
        IServiceScopeFactory scopeFactory,
        INotificationService notificationService,
        IBackgroundJobClient backgroundJobs,
        ILogger<RealNotificationDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _notificationService = notificationService;
        _backgroundJobs = backgroundJobs;
        _logger = logger;
    }

    public async Task SendInAppAsync(
        Guid tenantId, Guid recipientUserId, string notificationType, string payloadJson,
        CancellationToken cancellationToken = default)
    {
        var payload = ParsePayload(payloadJson);
        var title = GetString(payload, "title") ?? Humanize(notificationType);
        var message = GetString(payload, "message") ?? GetString(payload, "body") ?? string.Empty;
        var resourceType = GetString(payload, "resourceType");
        var resourceId = GetString(payload, "resourceId");

        // The in-app path (INotificationService) persists a durable row + best-effort SignalR push, and works
        // outside a resolved tenant context (it takes tenantId explicitly and opens its own scope).
        await _notificationService.CreateAndDispatchAsync(
            tenantId, recipientUserId, notificationType, title, message, resourceType, resourceId, cancellationToken);
    }

    public async Task SendEmailAsync(
        Guid tenantId, Guid recipientUserId, string notificationType, string payloadJson,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Restore tenant context so the template RESOLUTION query is scoped to the right tenant's override.
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
        {
            _logger.LogWarning(
                "RealNotificationDispatcher: tenant {TenantId} not found; dropping email for {Type}.",
                tenantId, notificationType);
            return;
        }
        tenantContext.SetTenant(tenantId, tenant.Subdomain, tenant.Status);

        var (eventKey, category, isMandatory) = MapType(notificationType);

        // Resolve the recipient's email (global User table — not tenant-filtered).
        var recipientEmail = await db.Users
            .Where(u => u.Id == recipientUserId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            await WriteDeliveryAsync(db, tenantId, recipientUserId, notificationType, eventKey, null, null,
                NotificationDeliveryStatus.Failed, "No email address resolved for recipient user.", cancellationToken);
            _logger.LogWarning(
                "RealNotificationDispatcher: no email for user {UserId} (tenant {TenantId}); email {Type} not sent.",
                recipientUserId, tenantId, notificationType);
            return;
        }

        // Preference gate (BR-1: non-suppressible security types always send, bypassing the gate).
        DateTime? deferUntilUtc = null;
        if (!isMandatory)
        {
            var decision = await scope.ServiceProvider.GetRequiredService<INotificationPreferenceService>()
                .ShouldDeliverAsync(tenantId, recipientUserId, category, NotificationChannel.Email, cancellationToken);

            switch (decision.Kind)
            {
                case DeliveryDecisionKind.Suppressed:
                    await WriteDeliveryAsync(db, tenantId, recipientUserId, notificationType, eventKey,
                        recipientEmail, null, NotificationDeliveryStatus.Suppressed, null, cancellationToken);
                    _logger.LogDebug(
                        "RealNotificationDispatcher: email {Type} suppressed by preferences for user {UserId}.",
                        notificationType, recipientUserId);
                    return;

                case DeliveryDecisionKind.DeferredUntilQuietHoursEnd:
                    deferUntilUtc = decision.DeferUntilUtc;
                    break;
            }
        }

        // Resolve + render the tenant's email template for this catalog event.
        var templateService = scope.ServiceProvider.GetRequiredService<IEmailTemplateService>();
        var resolved = await templateService.ResolveAsync(eventKey, language: null, cancellationToken);
        if (resolved.IsFailure || resolved.Value is null)
        {
            await WriteDeliveryAsync(db, tenantId, recipientUserId, notificationType, eventKey, recipientEmail, null,
                NotificationDeliveryStatus.Failed, $"No email template for event '{eventKey}'.", cancellationToken);
            _logger.LogWarning(
                "RealNotificationDispatcher: no template for event {EventKey} (type {Type}); email not sent.",
                eventKey, notificationType);
            return;
        }

        var template = resolved.Value;
        var data = ParsePayload(payloadJson);
        var rendered = templateService.Render(template.Subject, template.BodyHtml, template.BodyText, data);

        var status = deferUntilUtc.HasValue
            ? NotificationDeliveryStatus.Deferred
            : NotificationDeliveryStatus.Queued;

        var delivery = await WriteDeliveryAsync(db, tenantId, recipientUserId, notificationType, eventKey,
            recipientEmail, rendered.Subject, status, null, cancellationToken);

        var emailMessage = new EmailMessage(
            tenantId, recipientEmail, rendered.Subject, rendered.BodyHtml, rendered.BodyText);

        // Enqueue (or schedule past quiet-hours) the send job that drives the delivery row to a terminal state.
        if (deferUntilUtc.HasValue)
        {
            var delay = deferUntilUtc.Value - DateTime.UtcNow;
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
            _backgroundJobs.Schedule<SendEmailJob>(
                j => j.RunAsync(tenantId, delivery.Id, emailMessage, CancellationToken.None), delay);
        }
        else
        {
            _backgroundJobs.Enqueue<SendEmailJob>(
                j => j.RunAsync(tenantId, delivery.Id, emailMessage, CancellationToken.None));
        }
    }

    private static async Task<NotificationDelivery> WriteDeliveryAsync(
        AppDbContext db, Guid tenantId, Guid recipientUserId, string notificationType, string eventKey,
        string? recipientEmail, string? subject, NotificationDeliveryStatus status, string? lastError,
        CancellationToken cancellationToken)
    {
        var row = new NotificationDelivery
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,            // explicit — the dispatcher may run outside a resolved tenant context.
            Channel = NotificationDeliveryChannel.Email,
            Status = status,
            NotificationType = notificationType,
            EventKey = eventKey,
            RecipientUserId = recipientUserId,
            RecipientEmail = recipientEmail,
            Subject = subject,
            LastError = lastError,
            CreatedAt = DateTime.UtcNow,
        };
        db.NotificationDeliveries.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }

    /// <summary>
    /// Maps a raw dispatched notification type to (catalog event key, preference category, non-suppressible flag).
    /// This is the Phase-1 bridge between the free-form <c>notificationType</c> the dispatcher receives and the
    /// notification catalog/preference model; Phase 2 rewires the module seams to pass these explicitly. Security
    /// types (password reset, impersonation, tenant lifecycle, MFA, lockout) are mandatory (BR-1) and bypass the
    /// preference gate.
    /// </summary>
    private static (string EventKey, NotificationCategory Category, bool Mandatory) MapType(string notificationType)
    {
        var t = notificationType.ToLowerInvariant();

        if (t.Contains("password") || t.Contains("reset") || t.Contains("impersonat") || t.Contains("security")
            || t.Contains("mfa") || t.Contains("lockout") || t.Contains("tenant.lifecycle"))
            return (ResolveEventKey(notificationType, "password_reset"), NotificationCategory.SecurityAlerts, true);

        if (t.Contains("leave"))
            return (ResolveEventKey(notificationType, "leave_approved"), NotificationCategory.LeaveUpdates, false);

        if (t.Contains("payslip") || t.Contains("payroll"))
            return (ResolveEventKey(notificationType, "payslip_published"), NotificationCategory.PayrollNotifications, false);

        if (t.Contains("onboard") || t.Contains("offboard"))
            return (ResolveEventKey(notificationType, "onboarding_welcome"), NotificationCategory.OnboardingOffboarding, false);

        if (t.Contains("attendance") || t.Contains("regulariz") || t.Contains("punch") || t.Contains("shift"))
            return (ResolveEventKey(notificationType, notificationType), NotificationCategory.AttendanceAlerts, false);

        if (t.Contains("performance") || t.Contains("appraisal") || t.Contains("review") || t.Contains("goal") || t.Contains("pip"))
            return (ResolveEventKey(notificationType, notificationType), NotificationCategory.PerformanceReviews, false);

        if (t.Contains("recruit") || t.Contains("interview") || t.Contains("applicant") || t.Contains("offer") || t.Contains("vacancy"))
            return (ResolveEventKey(notificationType, notificationType), NotificationCategory.RecruitmentUpdates, false);

        return (ResolveEventKey(notificationType, notificationType), NotificationCategory.SystemAnnouncements, false);
    }

    /// <summary>Uses the type itself as the catalog event key when it is a known event, else the mapped fallback.</summary>
    private static string ResolveEventKey(string? notificationType, string fallback) =>
        !string.IsNullOrWhiteSpace(notificationType) && NotificationEventCatalog.IsKnownEvent(notificationType)
            ? notificationType!
            : fallback;

    /// <summary>Parses a JSON payload into a nested <c>Dictionary&lt;string, object?&gt;</c> the renderer walks.</summary>
    private static IReadOnlyDictionary<string, object?> ParsePayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new Dictionary<string, object?>();
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return ConvertElement(doc.RootElement) as IReadOnlyDictionary<string, object?>
                   ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>();
        }
    }

    private static object? ConvertElement(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => ConvertElement(p.Value)),
        JsonValueKind.Array => el.EnumerateArray().Select(ConvertElement).ToList(),
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    private static string? GetString(IReadOnlyDictionary<string, object?> data, string key) =>
        data.TryGetValue(key, out var v) && v is not null ? v.ToString() : null;

    private static string Humanize(string notificationType) =>
        string.IsNullOrWhiteSpace(notificationType)
            ? "Notification"
            : notificationType.Replace('.', ' ').Replace('_', ' ');
}
