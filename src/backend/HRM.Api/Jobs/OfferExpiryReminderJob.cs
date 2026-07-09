using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire job that fires N days BEFORE an offer's expiry to nudge the candidate (+ recruiter pool) that the
/// offer is about to lapse (US-REC-007 FR-7/AC-4). Sibling of <see cref="OfferExpiryJob"/> (which auto-expires
/// AT the boundary). TENANT-AWARE: the tenant id is passed in the job args and the job restores the tenant
/// context for its scope so the EF global query filters apply. Idempotent: if the offer is missing or no
/// longer Draft/Sent (already responded/withdrawn/expired), it no-ops — this is what prevents a reminder
/// firing after the candidate already accepted/withdrew. Otherwise it emits the reminder notification
/// (candidate + recruiter pool) via <c>NotifyOfferAsync("offer-expiry-reminder", …)</c> and clears the
/// reminder job id. Unlike the expiry job it does NOT change the offer status.
/// </summary>
public sealed class OfferExpiryReminderJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public OfferExpiryReminderJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <param name="tenantId">The tenant the offer belongs to (restores tenant context).</param>
    /// <param name="offerId">The offer to remind about if still active.</param>
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

        // Idempotent / defensive: only remind about a still-active (Draft/Sent) offer. If it has already been
        // accepted/declined/withdrawn/expired, do NOT send a "your offer is expiring soon" nudge.
        if (offer is null || !offer.IsActive)
        {
            Log.Information(
                "OfferExpiryReminderJob: skipping offer {OfferId} for tenant {TenantId} (missing or no longer active)",
                offerId, tenantId);
            return;
        }

        offer.ExpiryReminderJobId = null;
        await dbContext.SaveChangesAsync();

        var applicantEmail = await dbContext.Applicants
            .AsNoTracking()
            .Where(a => a.Id == offer.ApplicantId)
            .Select(a => a.Email)
            .FirstOrDefaultAsync() ?? string.Empty;

        await notifications.NotifyOfferAsync(
            "offer-expiry-reminder", offer.Id, offer.ApplicantId, offer.VacancyId, applicantEmail);

        Log.Information(
            "OfferExpiryReminderJob: reminder sent for offer {OfferId} (tenant {TenantId})", offer.Id, tenantId);
    }
}
