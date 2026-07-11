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

    public async Task SendInAppAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        // The in-app leg needs a real user to push to; skip gracefully for email-only (raw-address) recipients.
        if (request.RecipientUserId is not { } recipientUserId)
        {
            _logger.LogDebug(
                "RealNotificationDispatcher: no recipient user for event {EventKey}; skipping the in-app leg.",
                request.EventKey);
            return;
        }

        var notificationType = request.NotificationType ?? request.EventKey;
        var payload = ParsePayload(request.PayloadJson);
        var title = GetString(payload, "title") ?? Humanize(notificationType);
        var message = GetString(payload, "message") ?? GetString(payload, "body") ?? string.Empty;
        var resourceType = GetString(payload, "resourceType");
        var resourceId = GetString(payload, "resourceId");

        // The in-app path (INotificationService) persists a durable row + best-effort SignalR push, and works
        // outside a resolved tenant context (it takes tenantId explicitly and opens its own scope).
        await _notificationService.CreateAndDispatchAsync(
            request.TenantId, recipientUserId, notificationType, title, message, resourceType, resourceId, cancellationToken);
    }

    public async Task SendEmailAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = request.TenantId;
        var eventKey = request.EventKey;
        var notificationType = request.NotificationType ?? eventKey;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();

        // Outcome captured OUT of the runner block so the Hangfire enqueue (not a tenant DB write) stays outside it.
        NotificationDelivery? delivery = null;
        EmailMessage? emailMessage = null;
        DateTime? deferUntilUtc = null;

        // ISSUE-268: all tenant AppDbContext reads/writes (template resolution + the NotificationDelivery INSERT)
        // run inside ONE RunForTenantAsync block so the fresh scope's connection carries the app.current_tenant GUC
        // under RLS-on (the strict WITH CHECK tenant_isolation policy would otherwise reject the INSERT — 42501).
        // The runner + AppDbContext come from the SAME scope so the GUC applies to the db we write. Under
        // Rls:Enabled=false / InMemory the runner no-ops (just sets context) — behaviour is unchanged. The Hangfire
        // enqueue is done AFTER the block (an enqueue is not a tenant DB write and must not sit in the tx).
        await runner.RunForTenantAsync(tenantId, $"tenant-{tenantId}", async ct =>
        {
            // Restore tenant context so the template RESOLUTION query is scoped to the right tenant's override.
            // (The runner already SetTenant; we still read the tenant to confirm existence + preserve status.)
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
            if (tenant is null)
            {
                _logger.LogWarning(
                    "RealNotificationDispatcher: tenant {TenantId} not found; dropping email for {EventKey}.",
                    tenantId, eventKey);
                return;
            }
            tenantContext.SetTenant(tenantId, tenant.Subdomain, tenant.Status);

            // Category + mandatory come from the catalog (single source of truth). Unknown events default to a
            // suppressible SystemAnnouncement — template resolution will then fail below and write a Failed row.
            var definition = NotificationEventCatalog.Get(eventKey);
            var category = definition?.Category ?? NotificationCategory.SystemAnnouncements;
            var isMandatory = definition?.IsMandatory ?? false;

            // Resolve the recipient's email: the raw override wins, else the User table (global — not tenant-filtered).
            var recipientEmail = request.RecipientEmail;
            if (string.IsNullOrWhiteSpace(recipientEmail) && request.RecipientUserId is { } userId)
            {
                recipientEmail = await db.Users
                    .Where(u => u.Id == userId)
                    .Select(u => u.Email)
                    .FirstOrDefaultAsync(ct);
            }

            if (string.IsNullOrWhiteSpace(recipientEmail))
            {
                await WriteDeliveryAsync(db, tenantId, request.RecipientUserId, notificationType, eventKey, null, null,
                    NotificationDeliveryStatus.Failed, "No email address resolved for recipient.", ct);
                _logger.LogWarning(
                    "RealNotificationDispatcher: no email resolved for event {EventKey} (tenant {TenantId}); not sent.",
                    eventKey, tenantId);
                return;
            }

            // Preference gate (BR-1: non-suppressible security types always send, bypassing the gate). The gate is
            // per-user, so it only applies when we have a recipient user id (raw-email recipients always send).
            if (!isMandatory && request.RecipientUserId is { } prefUserId)
            {
                var decision = await scope.ServiceProvider.GetRequiredService<INotificationPreferenceService>()
                    .ShouldDeliverAsync(tenantId, prefUserId, category, NotificationChannel.Email, ct);

                switch (decision.Kind)
                {
                    case DeliveryDecisionKind.Suppressed:
                        await WriteDeliveryAsync(db, tenantId, request.RecipientUserId, notificationType, eventKey,
                            recipientEmail, null, NotificationDeliveryStatus.Suppressed, null, ct);
                        _logger.LogDebug(
                            "RealNotificationDispatcher: email {EventKey} suppressed by preferences for user {UserId}.",
                            eventKey, prefUserId);
                        return;

                    case DeliveryDecisionKind.DeferredUntilQuietHoursEnd:
                        deferUntilUtc = decision.DeferUntilUtc;
                        break;
                }
            }

            // Resolve + render the tenant's email template for this catalog event.
            var templateService = scope.ServiceProvider.GetRequiredService<IEmailTemplateService>();
            var resolved = await templateService.ResolveAsync(eventKey, language: null, ct);
            if (resolved.IsFailure || resolved.Value is null)
            {
                await WriteDeliveryAsync(db, tenantId, request.RecipientUserId, notificationType, eventKey, recipientEmail, null,
                    NotificationDeliveryStatus.Failed, $"No email template for event '{eventKey}'.", ct);
                _logger.LogWarning(
                    "RealNotificationDispatcher: no template for event {EventKey}; email not sent.", eventKey);
                return;
            }

            var template = resolved.Value;
            var data = ParsePayload(request.PayloadJson);
            var rendered = templateService.Render(template.Subject, template.BodyHtml, template.BodyText, data);

            var status = deferUntilUtc.HasValue
                ? NotificationDeliveryStatus.Deferred
                : NotificationDeliveryStatus.Queued;

            delivery = await WriteDeliveryAsync(db, tenantId, request.RecipientUserId, notificationType, eventKey,
                recipientEmail, rendered.Subject, status, null, ct);

            emailMessage = new EmailMessage(
                tenantId, recipientEmail, rendered.Subject, rendered.BodyHtml, rendered.BodyText);
        }, cancellationToken);

        // Nothing to send — an early-return path (no tenant / no email / suppressed / no template) ran inside the
        // runner and left no queued delivery.
        if (delivery is null || emailMessage is null)
            return;

        // Enqueue (or schedule past quiet-hours) the send job that drives the delivery row to a terminal state.
        // OUTSIDE the runner block — the enqueue is not a tenant DB write.
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
        AppDbContext db, Guid tenantId, Guid? recipientUserId, string notificationType, string eventKey,
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
            RecipientUserId = recipientUserId ?? Guid.Empty,  // Guid.Empty for raw-email (non-provisioned) recipients.
            RecipientEmail = recipientEmail,
            Subject = subject,
            LastError = lastError,
            CreatedAt = DateTime.UtcNow,
        };
        db.NotificationDeliveries.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }

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
