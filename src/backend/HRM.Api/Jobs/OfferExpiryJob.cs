using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
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

        // Restore tenant context so global query filters scope the reads/writes (tenant isolation).
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        if (tenantContext is Infrastructure.Services.TenantContext mutableContext)
        {
            mutableContext.SetTenant(tenantId, $"tenant-{tenantId}", TenantStatus.Active);
        }

        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<IRecruitmentNotificationService>();

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

        offer.Status = OfferStatus.Expired;
        offer.ReminderJobId = null;
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
    }
}
