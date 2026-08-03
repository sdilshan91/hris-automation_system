using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Recruitment;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire job that fires at an offer's expiry boundary to auto-expire it (US-REC-007 FR-7/AC-4). It is
/// TENANT-AWARE: the tenant id is passed in the job args, and the job restores the tenant context for its
/// scope so the EF global query filters apply (mirrors <c>InterviewReminderJob</c>). Idempotent: if the
/// offer is missing or no longer Draft/Sent (already responded/withdrawn/expired), it no-ops; otherwise it
/// sets the status to Expired and emits the expiry notification (recruiter + applicant) via the log-only
/// seam.
/// </summary>
public sealed class OfferExpiryJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public OfferExpiryJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <param name="tenantId">The tenant the offer belongs to (restores tenant context).</param>
    /// <param name="offerId">The offer to expire if still unanswered.</param>
    public async Task RunAsync(Guid tenantId, Guid offerId)
    {
        using var scope = _scopeFactory.CreateScope();

        var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<IRecruitmentNotificationService>();

        // RLS increment 2c: run the tenant body via the shared runner so it sets the tenant context (and, gated
        // on Rls:Enabled, the app.current_tenant GUC) — this offer-by-id job stays inside the RLS backstop.
        await runner.RunForTenantAsync(tenantId, $"tenant-{tenantId}", async _ =>
        {
        var offer = await dbContext.Offers.FirstOrDefaultAsync(o => o.Id == offerId);

        // Idempotent / defensive: only expire a still-active (Draft/Sent) offer.
        if (offer is null || !offer.IsActive)
        {
            Log.Information(
                "OfferExpiryJob: skipping offer {OfferId} for tenant {TenantId} (missing or no longer active)",
                offerId, tenantId);
            return;
        }

        // Defensive: don't expire early if the expiry date hasn't actually passed yet (e.g. a stale job).
        if (DateOnly.FromDateTime(DateTime.UtcNow) <= offer.ExpiryDate)
        {
            Log.Information(
                "OfferExpiryJob: offer {OfferId} not yet past expiry ({ExpiryDate}); skipping", offerId, offer.ExpiryDate);
            return;
        }

        var statusBeforeExpiry = offer.Status;
        offer.Status = OfferStatus.Expired;
        offer.ReminderJobId = null;

        // ISSUE-124: the auto-expire leg must leave the same audit trail as the operator-driven transitions,
        // otherwise the ONE status change nobody witnessed is also the one with no record. Added to the change
        // set before SaveChanges so the row commits atomically with the status flip. UserId is null on purpose:
        // this is the system actor, not a person.
        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            UserId = null,
            EventType = OfferAuditAction.Expired,
            Action = OfferAuditAction.Expired,
            ResourceType = OfferAuditAction.ResourceType,
            ResourceId = offer.Id.ToString(),
            Before = JsonSerializer.Serialize(new { Status = statusBeforeExpiry.ToString() }),
            After = JsonSerializer.Serialize(new { Status = offer.Status.ToString(), offer.ExpiryDate }),
            CreatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();

        var applicantEmail = await dbContext.Applicants
            .AsNoTracking()
            .Where(a => a.Id == offer.ApplicantId)
            .Select(a => a.Email)
            .FirstOrDefaultAsync() ?? string.Empty;

        await notifications.NotifyOfferAsync(
            "offer-expired", offer.Id, offer.ApplicantId, offer.VacancyId, applicantEmail);

        Log.Information(
            "OfferExpiryJob: expired offer {OfferId} (tenant {TenantId})", offer.Id, tenantId);
        });
    }
}
