using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire job that fires ~24h (configurable) before an interview to remind all participants
/// (US-REC-005 FR-4/AC-2/NFR-3/NFR-4). It is TENANT-AWARE: the tenant id is passed in the job args
/// (NFR-4), and the job restores the tenant context for its scope so the EF global query filters apply
/// (mirrors <c>AutoClockOutJob.ProcessTenantAsync</c>). Idempotent (NFR-4): if the interview is missing,
/// cancelled, or no longer Scheduled, it simply no-ops; otherwise it re-sends the reminder via the
/// log-only notification seam.
/// </summary>
public sealed class InterviewReminderJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public InterviewReminderJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <param name="tenantId">The tenant the interview belongs to (NFR-4 — restores tenant context).</param>
    /// <param name="interviewId">The interview to remind participants about.</param>
    public async Task RunAsync(Guid tenantId, Guid interviewId)
    {
        using var scope = _scopeFactory.CreateScope();

        // Restore tenant context so global query filters scope the reads (tenant isolation, NFR-4).
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        if (tenantContext is Infrastructure.Services.TenantContext mutableContext)
        {
            mutableContext.SetTenant(tenantId, $"tenant-{tenantId}", TenantStatus.Active);
        }

        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<IRecruitmentNotificationService>();

        var interview = await dbContext.Interviews
            .AsNoTracking()
            .Include(i => i.Interviewers)
            .FirstOrDefaultAsync(i => i.Id == interviewId);

        // Idempotent / defensive: only remind for a still-scheduled interview.
        if (interview is null || interview.Status != InterviewStatus.Scheduled)
        {
            Log.Information(
                "InterviewReminderJob: skipping interview {InterviewId} for tenant {TenantId} (missing or not scheduled)",
                interviewId, tenantId);
            return;
        }

        var applicantEmail = await dbContext.Applicants
            .AsNoTracking()
            .Where(a => a.Id == interview.ApplicantId)
            .Select(a => a.Email)
            .FirstOrDefaultAsync() ?? string.Empty;

        var interviewerIds = interview.Interviewers.Select(ii => ii.EmployeeId).ToList();
        var interviewerEmails = interviewerIds.Count == 0
            ? new List<string>()
            : await dbContext.Employees
                .AsNoTracking()
                .Where(e => interviewerIds.Contains(e.Id))
                .Select(e => e.Email)
                .ToListAsync();

        await notifications.NotifyInterviewReminderAsync(
            interview.Id, interview.ApplicantId, interview.VacancyId, applicantEmail, interviewerEmails);

        Log.Information(
            "InterviewReminderJob: sent reminder for interview {InterviewId} (tenant {TenantId}) to applicant + {Count} interviewer(s)",
            interview.Id, tenantId, interviewerEmails.Count);
    }
}
