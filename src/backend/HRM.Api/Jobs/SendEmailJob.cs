using HRM.Application.Common.Interfaces;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire worker that performs a single email send for US-NTF-006 delivery infrastructure. Enqueued by
/// <see cref="Notifications.RealNotificationDispatcher"/> after it writes a <see cref="NotificationDelivery"/>
/// row (Queued or Deferred). Wraps the generic <see cref="IEmailSender"/> seam:
/// <list type="bullet">
/// <item>success → marks the delivery row <see cref="NotificationDeliveryStatus.Sent"/> + stamps SentAt;</item>
/// <item>throw → increments <see cref="NotificationDelivery.Attempts"/>, records the error, and rethrows so
/// Hangfire retries with backoff (<see cref="AutomaticRetryAttribute"/>); once attempts are exhausted the row is
/// set terminal <see cref="NotificationDeliveryStatus.Failed"/>.</item>
/// </list>
///
/// <para>With no SMTP configured the injected <see cref="IEmailSender"/> is the log-only stub, which never
/// throws — so this job simply marks rows Sent, and the whole path is safe with no SMTP server.</para>
///
/// <para>Runs outside a request scope (no resolved ITenantContext): the delivery row is loaded by its id +
/// explicit tenant id. The global query filter is bypassed naturally because ITenantContext is unresolved in the
/// job scope (no <c>IgnoreQueryFilters</c> needed); the explicit tenant-id predicate is the isolation guard.</para>
/// </summary>
public sealed class SendEmailJob
{
    /// <summary>Total send attempts before the delivery row is marked terminally Failed (matches the retry cap).</summary>
    public const int MaxAttempts = 5;

    private readonly IServiceScopeFactory _scopeFactory;

    public SendEmailJob(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    [AutomaticRetry(Attempts = MaxAttempts, DelaysInSeconds = new[] { 60, 300, 900, 3600, 21600 })]
    public async Task RunAsync(
        Guid tenantId, Guid deliveryId, EmailMessage message, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var row = await db.NotificationDeliveries
            .FirstOrDefaultAsync(d => d.Id == deliveryId && d.TenantId == tenantId, cancellationToken);

        if (row is null)
        {
            Log.Warning(
                "SendEmailJob: delivery row {DeliveryId} for tenant {TenantId} not found; skipping.",
                deliveryId, tenantId);
            return;
        }

        if (row.Status == NotificationDeliveryStatus.Sent)
            return; // idempotent: a retry after a successful send that failed to persist should not resend twice.

        row.Attempts++;
        try
        {
            await sender.SendAsync(message, cancellationToken);

            row.Status = NotificationDeliveryStatus.Sent;
            row.SentAt = DateTime.UtcNow;
            row.LastError = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            row.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            if (row.Attempts >= MaxAttempts)
                row.Status = NotificationDeliveryStatus.Failed;

            await db.SaveChangesAsync(CancellationToken.None);

            Log.Error(ex,
                "SendEmailJob: send FAILED for delivery {DeliveryId} (tenant {TenantId}), attempt {Attempt}/{Max}.",
                deliveryId, tenantId, row.Attempts, MaxAttempts);

            throw; // surface to Hangfire so it retries with backoff (or gives up after MaxAttempts).
        }
    }
}
