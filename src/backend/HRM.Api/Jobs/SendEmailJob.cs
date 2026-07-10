using System.Runtime.ExceptionServices;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
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
/// <para><b>RLS increment 3a</b> — the delivery row lives in the policy-bearing <c>notification_deliveries</c>
/// table, so this job establishes the tenant via <see cref="ITenantJobRunner.RunForTenantAsync"/> (which sets the
/// <c>app.current_tenant</c> GUC on the <c>hrm_app</c> connection under RLS) rather than relying on an unresolved
/// see-all context. The work is split into THREE units so it works under RLS AND still persists retry state on
/// failure — a single commit-on-success transaction would otherwise roll back the <c>Attempts++</c>/<c>LastError</c>
/// bookkeeping when the send throws:</para>
/// <list type="number">
/// <item><b>read</b> the row (tenant-scoped, GUC set);</item>
/// <item><b>send</b> via the SMTP seam OUTSIDE any GUC transaction (a network call must not hold a DB tx open);</item>
/// <item><b>persist</b> the outcome (Sent, or Attempts++/LastError/Failed) in its OWN short GUC-scoped unit that
/// COMMITS even when the send failed — so the retry counter/error is durable and visible under RLS;</item>
/// </list>
/// <para>then, only after that commit, the send error is rethrown so Hangfire retries with backoff (or gives up
/// after <see cref="MaxAttempts"/>). An already-Sent row is a no-op (idempotent).</para>
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
        var tenantRunner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();

        // Subdomain is log/diagnostic-only for the runner; the tenant id is what scopes the GUC + query filter.
        var subdomain = tenantId.ToString();

        // 1. READ — load the delivery row inside the tenant scope (under RLS this sets the GUC on hrm_app so the
        //    row is visible; the explicit tenant-id predicate stays as a belt-and-suspenders guard).
        NotificationDelivery? row = null;
        await tenantRunner.RunForTenantAsync(tenantId, subdomain, async innerCt =>
        {
            row = await db.NotificationDeliveries
                .FirstOrDefaultAsync(d => d.Id == deliveryId && d.TenantId == tenantId, innerCt);
        }, cancellationToken);

        if (row is null)
        {
            Log.Warning(
                "SendEmailJob: delivery row {DeliveryId} for tenant {TenantId} not found; skipping.",
                deliveryId, tenantId);
            return;
        }

        if (row.Status == NotificationDeliveryStatus.Sent)
            return; // idempotent: a retry after a successful send that failed to persist should not resend twice.

        // 2. SEND — OUTSIDE any GUC transaction. A network/SMTP call must never hold a DB transaction open.
        Exception? sendError = null;
        try
        {
            await sender.SendAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            sendError = ex;
        }

        // Compute the target attempt count once, OUTSIDE the (retry-safe) persistence unit, so a transient-retry of
        // that unit re-applies the SAME value instead of double-incrementing the tracked entity.
        var attempts = row.Attempts + 1;

        // 3. PERSIST — the outcome in its OWN short tenant-scoped unit that COMMITS even on send failure. This is
        //    the key correctness property: the retry counter/error is durable regardless of the send result.
        await tenantRunner.RunForTenantAsync(tenantId, subdomain, async innerCt =>
        {
            row.Attempts = attempts;
            if (sendError is null)
            {
                row.Status = NotificationDeliveryStatus.Sent;
                row.SentAt = DateTime.UtcNow;
                row.LastError = null;
            }
            else
            {
                row.LastError = sendError.Message.Length > 2000 ? sendError.Message[..2000] : sendError.Message;
                if (attempts >= MaxAttempts)
                    row.Status = NotificationDeliveryStatus.Failed;
            }

            await db.SaveChangesAsync(innerCt);
        }, cancellationToken);

        // 4. RETHROW — only AFTER the outcome is committed, so Hangfire retries with backoff (or gives up after
        //    MaxAttempts) while the persisted Attempts/LastError survive. ExceptionDispatchInfo preserves the
        //    original stack trace.
        if (sendError is not null)
        {
            Log.Error(sendError,
                "SendEmailJob: send FAILED for delivery {DeliveryId} (tenant {TenantId}), attempt {Attempt}/{Max}.",
                deliveryId, tenantId, attempts, MaxAttempts);

            ExceptionDispatchInfo.Throw(sendError);
        }
    }
}
