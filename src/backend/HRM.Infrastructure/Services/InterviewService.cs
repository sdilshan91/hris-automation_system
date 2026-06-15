using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Interview scheduling / rescheduling / cancellation + calendar reads (US-REC-005). All queries are
/// tenant-scoped via ITenantContext + the EF global query filter (AC-5). Scheduling enforces ≥1
/// interviewer (FR-1), a future date (BR-3/NFR-6), interviewers that are active employees in the tenant
/// (BR-2), and location/video-link by interview type (FR-1). Scheduling conflicts (FR-7) are returned as
/// soft warnings (never blocking — override allowed). On create/update/cancel, participants are notified
/// via the log-only seam (FR-3/BR-7). A tenant-aware Hangfire reminder job is scheduled ~lead-time before
/// the interview (FR-4/BR-6) when the scheduler seam is available; on reschedule it is swapped, on cancel
/// deleted.
/// </summary>
public sealed class InterviewService : IInterviewService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IRecruitmentNotificationService _notifications;
    private readonly IInterviewReminderScheduler? _reminderScheduler;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InterviewService> _logger;

    /// <summary>BR-5/FR-4: default reminder lead time (hours) when not configured.</summary>
    private const int DefaultReminderLeadHours = 24;

    public InterviewService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IRecruitmentNotificationService notifications,
        IConfiguration configuration,
        ILogger<InterviewService> logger,
        IInterviewReminderScheduler? reminderScheduler = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _notifications = notifications;
        _configuration = configuration;
        _logger = logger;
        _reminderScheduler = reminderScheduler;
    }

    // ── Schedule (AC-1/FR-1/FR-2) ─────────────────────────────────────

    public async Task<Result<InterviewDto>> ScheduleAsync(
        ScheduleInterviewInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<InterviewDto>.Failure("Tenant context is not resolved.", 400);

        var interviewerIds = (input.InterviewerEmployeeIds ?? []).Distinct().ToList();

        // FR-1: ≥1 interviewer (defensive — the validator is the primary gate).
        if (interviewerIds.Count == 0)
            return Result<InterviewDto>.Failure("At least one interviewer is required.", 400, "interviewers_required");

        // BR-3/NFR-6: future date (defensive re-check).
        var startsAtUtc = DateTime.SpecifyKind(input.ScheduledDate.ToDateTime(input.StartTime), DateTimeKind.Utc);
        if (startsAtUtc <= DateTime.UtcNow)
            return Result<InterviewDto>.Failure("The interview must be scheduled for a future date and time.", 400, "interview_in_past");

        var typeCheck = ValidateTypeFields(input.InterviewType, input.Location, input.VideoLink);
        if (typeCheck.IsFailure)
            return Result<InterviewDto>.Failure(typeCheck.Error!, typeCheck.StatusCode ?? 400, typeCheck.ErrorCode);

        // Applicant must exist in this tenant (global filter ⇒ found ⇒ in-tenant). 404 otherwise so a
        // guessed cross-tenant id never schedules against another tenant's applicant (AC-5).
        var applicant = await _dbContext.Applicants
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == input.ApplicantId, cancellationToken);
        if (applicant is null)
            return Result<InterviewDto>.Failure("Applicant not found.", 404, "applicant_not_found");

        // BR-2: every interviewer must be an active employee in this tenant.
        var employeeCheck = await ValidateInterviewersAsync(interviewerIds, cancellationToken);
        if (employeeCheck.IsFailure)
            return Result<InterviewDto>.Failure(employeeCheck.Error!, employeeCheck.StatusCode ?? 400, employeeCheck.ErrorCode);

        // FR-2: auto-increment the round number per applicant (max existing + 1).
        var maxRound = await _dbContext.Interviews
            .Where(i => i.ApplicantId == input.ApplicantId)
            .Select(i => (int?)i.RoundNumber)
            .MaxAsync(cancellationToken) ?? 0;

        var interviewId = BaseEntity.NewUuidV7();
        var endsAtUtc = startsAtUtc.AddMinutes(input.DurationMinutes);

        // FR-7: soft conflict detection across the selected interviewers.
        var warnings = await DetectConflictsAsync(interviewerIds, startsAtUtc, endsAtUtc, excludeInterviewId: null, cancellationToken);

        var interview = new Interview
        {
            Id = interviewId,
            TenantId = _tenantContext.TenantId,
            ApplicantId = input.ApplicantId,
            VacancyId = applicant.VacancyId,
            RoundNumber = maxRound + 1,
            InterviewType = input.InterviewType,
            ScheduledDate = input.ScheduledDate,
            StartTime = input.StartTime,
            DurationMinutes = input.DurationMinutes,
            Location = Trim(input.Location),
            VideoLink = Trim(input.VideoLink),
            Notes = Trim(input.Notes),
            Status = InterviewStatus.Scheduled,
            IsDeleted = false,
            Interviewers = interviewerIds.Select(empId => new InterviewInterviewer
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                InterviewId = interviewId,
                EmployeeId = empId,
                IsDeleted = false,
            }).ToList(),
        };

        // FR-4/BR-6: schedule the tenant-aware reminder job (no-op if the scheduler seam is absent, e.g.
        // tests, or if the fire-time is already in the past).
        interview.ReminderJobId = ScheduleReminder(interview);

        _dbContext.Interviews.Add(interview);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Interview scheduled. InterviewId={InterviewId}, ApplicantId={ApplicantId}, VacancyId={VacancyId}, " +
            "Round={Round}, Type={Type}, StartsAtUtc={StartsAtUtc}, Interviewers={Count}, ReminderJobId={ReminderJobId}, TenantId={TenantId}",
            interview.Id, interview.ApplicantId, interview.VacancyId, interview.RoundNumber, interview.InterviewType,
            startsAtUtc, interviewerIds.Count, interview.ReminderJobId, _tenantContext.TenantId);

        await NotifyParticipantsSafeAsync("interview-scheduled", interview, cancellationToken);

        return await BuildDtoResultAsync(interview.Id, warnings, cancellationToken);
    }

    // ── Reschedule / update (AC-3/BR-6) ──────────────────────────────

    public async Task<Result<InterviewDto>> UpdateAsync(
        Guid interviewId, UpdateInterviewInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<InterviewDto>.Failure("Tenant context is not resolved.", 400);

        var interviewerIds = (input.InterviewerEmployeeIds ?? []).Distinct().ToList();
        if (interviewerIds.Count == 0)
            return Result<InterviewDto>.Failure("At least one interviewer is required.", 400, "interviewers_required");

        var startsAtUtc = DateTime.SpecifyKind(input.ScheduledDate.ToDateTime(input.StartTime), DateTimeKind.Utc);
        if (startsAtUtc <= DateTime.UtcNow)
            return Result<InterviewDto>.Failure("The interview must be rescheduled to a future date and time.", 400, "interview_in_past");

        var typeCheck = ValidateTypeFields(input.InterviewType, input.Location, input.VideoLink);
        if (typeCheck.IsFailure)
            return Result<InterviewDto>.Failure(typeCheck.Error!, typeCheck.StatusCode ?? 400, typeCheck.ErrorCode);

        var interview = await _dbContext.Interviews
            .FirstOrDefaultAsync(i => i.Id == interviewId, cancellationToken);
        if (interview is null)
            return Result<InterviewDto>.Failure("Interview not found.", 404, "interview_not_found");

        if (interview.Status == InterviewStatus.Cancelled)
            return Result<InterviewDto>.Failure("A cancelled interview cannot be rescheduled.", 409, "interview_cancelled");

        var employeeCheck = await ValidateInterviewersAsync(interviewerIds, cancellationToken);
        if (employeeCheck.IsFailure)
            return Result<InterviewDto>.Failure(employeeCheck.Error!, employeeCheck.StatusCode ?? 400, employeeCheck.ErrorCode);

        var endsAtUtc = startsAtUtc.AddMinutes(input.DurationMinutes);

        // FR-7: conflict detection excludes THIS interview (don't conflict with itself).
        var warnings = await DetectConflictsAsync(interviewerIds, startsAtUtc, endsAtUtc, excludeInterviewId: interview.Id, cancellationToken);

        interview.InterviewType = input.InterviewType;
        interview.ScheduledDate = input.ScheduledDate;
        interview.StartTime = input.StartTime;
        interview.DurationMinutes = input.DurationMinutes;
        interview.Location = Trim(input.Location);
        interview.VideoLink = Trim(input.VideoLink);
        interview.Notes = Trim(input.Notes);

        // Reconcile the interviewer set by employee id directly on the junction DbSet (the interview is
        // loaded WITHOUT Include, so there is no tracked nav-collection to fight the add/remove — which
        // is what tripped the InMemory change tracker). Remove links no longer selected, add new ones,
        // leave unchanged links in place.
        var desired = interviewerIds.ToHashSet();
        var existingLinks = await _dbContext.InterviewInterviewers
            .Where(ii => ii.InterviewId == interview.Id)
            .ToListAsync(cancellationToken);
        var existingEmpIds = existingLinks.Select(l => l.EmployeeId).ToHashSet();

        var toRemove = existingLinks.Where(l => !desired.Contains(l.EmployeeId)).ToList();
        if (toRemove.Count > 0)
            _dbContext.InterviewInterviewers.RemoveRange(toRemove);

        foreach (var empId in interviewerIds.Where(e => !existingEmpIds.Contains(e)))
        {
            _dbContext.InterviewInterviewers.Add(new InterviewInterviewer
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                InterviewId = interview.Id,
                EmployeeId = empId,
                IsDeleted = false,
            });
        }

        // BR-6: swap the reminder job (delete the old, create a new one for the updated time).
        _reminderScheduler?.Cancel(interview.ReminderJobId);
        interview.ReminderJobId = ScheduleReminder(interview);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Interview rescheduled. InterviewId={InterviewId}, StartsAtUtc={StartsAtUtc}, Interviewers={Count}, " +
            "ReminderJobId={ReminderJobId}, TenantId={TenantId}",
            interview.Id, startsAtUtc, interviewerIds.Count, interview.ReminderJobId, _tenantContext.TenantId);

        await NotifyParticipantsSafeAsync("interview-updated", interview, cancellationToken);

        return await BuildDtoResultAsync(interview.Id, warnings, cancellationToken);
    }

    // ── Cancel (AC-3) ─────────────────────────────────────────────────

    public async Task<Result<InterviewDto>> CancelAsync(Guid interviewId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<InterviewDto>.Failure("Tenant context is not resolved.", 400);

        var interview = await _dbContext.Interviews
            .Include(i => i.Interviewers)
            .FirstOrDefaultAsync(i => i.Id == interviewId, cancellationToken);
        if (interview is null)
            return Result<InterviewDto>.Failure("Interview not found.", 404, "interview_not_found");

        if (interview.Status == InterviewStatus.Cancelled)
            return Result<InterviewDto>.Failure("The interview is already cancelled.", 409, "interview_already_cancelled");

        interview.Status = InterviewStatus.Cancelled;

        // BR-6: remove the reminder job (no participant should be reminded about a cancelled interview).
        _reminderScheduler?.Cancel(interview.ReminderJobId);
        interview.ReminderJobId = null;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Interview cancelled. InterviewId={InterviewId}, ApplicantId={ApplicantId}, TenantId={TenantId}",
            interview.Id, interview.ApplicantId, _tenantContext.TenantId);

        // BR-4: cancelling does NOT change the applicant's pipeline stage.
        await NotifyParticipantsSafeAsync("interview-cancelled", interview, cancellationToken);

        return await BuildDtoResultAsync(interview.Id, [], cancellationToken);
    }

    // ── Reads ─────────────────────────────────────────────────────────

    public async Task<Result<InterviewDto>> GetByIdAsync(Guid interviewId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<InterviewDto>.Failure("Tenant context is not resolved.", 400);

        var interview = await _dbContext.Interviews
            .AsNoTracking()
            .Include(i => i.Interviewers)
            .FirstOrDefaultAsync(i => i.Id == interviewId, cancellationToken);
        if (interview is null)
            return Result<InterviewDto>.Failure("Interview not found.", 404, "interview_not_found");

        return Result<InterviewDto>.Success(await ToDtoAsync(interview, [], cancellationToken));
    }

    public async Task<Result<IReadOnlyList<InterviewDto>>> ListAsync(
        InterviewCalendarFilter filter, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<InterviewDto>>.Failure("Tenant context is not resolved.", 400);

        var query = _dbContext.Interviews.AsNoTracking().Include(i => i.Interviewers).AsQueryable();

        if (filter.VacancyId is { } vacancyId)
            query = query.Where(i => i.VacancyId == vacancyId);

        if (filter.Status is { } status)
            query = query.Where(i => i.Status == status);

        if (filter.From is { } from)
            query = query.Where(i => i.ScheduledDate >= from);

        if (filter.To is { } to)
            query = query.Where(i => i.ScheduledDate <= to);

        if (filter.InterviewerEmployeeId is { } interviewerId)
            query = query.Where(i => i.Interviewers.Any(ii => ii.EmployeeId == interviewerId));

        var interviews = await query
            .OrderBy(i => i.ScheduledDate).ThenBy(i => i.StartTime)
            .ToListAsync(cancellationToken);

        var dtos = await ToDtosAsync(interviews, cancellationToken);
        return Result<IReadOnlyList<InterviewDto>>.Success(dtos);
    }

    public async Task<Result<IReadOnlyList<InterviewDto>>> ListByApplicantAsync(
        Guid applicantId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<InterviewDto>>.Failure("Tenant context is not resolved.", 400);

        var interviews = await _dbContext.Interviews
            .AsNoTracking()
            .Include(i => i.Interviewers)
            .Where(i => i.ApplicantId == applicantId)
            .OrderByDescending(i => i.RoundNumber)
            .ToListAsync(cancellationToken);

        var dtos = await ToDtosAsync(interviews, cancellationToken);
        return Result<IReadOnlyList<InterviewDto>>.Success(dtos);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static Result ValidateTypeFields(InterviewType type, string? location, string? videoLink)
    {
        if (type == InterviewType.InPerson && string.IsNullOrWhiteSpace(location))
            return Result.Failure("A location is required for an in-person interview.", 400, "location_required");
        if (type == InterviewType.Video && string.IsNullOrWhiteSpace(videoLink))
            return Result.Failure("A video meeting link is required for a video interview.", 400, "video_link_required");
        return Result.Success();
    }

    /// <summary>BR-2: every interviewer must be an active employee in this tenant (tenant-scoped lookup).</summary>
    private async Task<Result> ValidateInterviewersAsync(IReadOnlyList<Guid> interviewerIds, CancellationToken cancellationToken)
    {
        var activeIds = await _dbContext.Employees
            .Where(e => interviewerIds.Contains(e.Id) && e.Status == EmployeeStatus.Active)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        var missing = interviewerIds.Except(activeIds).ToList();
        if (missing.Count > 0)
            return Result.Failure(
                "One or more selected interviewers are not active employees in this organization.",
                400, "interviewer_not_active");

        return Result.Success();
    }

    /// <summary>
    /// FR-7: returns a soft warning per interviewer that already has a Scheduled interview overlapping the
    /// [start, end) window. Never blocks the move — the recruiter may override. Tenant-scoped.
    /// </summary>
    private async Task<IReadOnlyList<string>> DetectConflictsAsync(
        IReadOnlyList<Guid> interviewerIds, DateTime startsAtUtc, DateTime endsAtUtc,
        Guid? excludeInterviewId, CancellationToken cancellationToken)
    {
        // Candidate Scheduled interviews on the same calendar date for any of these interviewers. We
        // overlap-check in memory using the StartsAtUtc/EndsAtUtc computed properties (TimeOnly arithmetic
        // is awkward in the InMemory/SQL provider; the per-date prefilter keeps the set tiny).
        var startDate = DateOnly.FromDateTime(startsAtUtc);
        var endDate = DateOnly.FromDateTime(endsAtUtc);

        var candidates = await _dbContext.Interviews
            .AsNoTracking()
            .Include(i => i.Interviewers)
            .Where(i => i.Status == InterviewStatus.Scheduled
                        && (excludeInterviewId == null || i.Id != excludeInterviewId)
                        && i.ScheduledDate >= startDate && i.ScheduledDate <= endDate
                        && i.Interviewers.Any(ii => interviewerIds.Contains(ii.EmployeeId)))
            .ToListAsync(cancellationToken);

        var conflictingInterviewerIds = new HashSet<Guid>();
        foreach (var candidate in candidates)
        {
            // [start, end) overlap test.
            var overlaps = candidate.StartsAtUtc < endsAtUtc && startsAtUtc < candidate.EndsAtUtc;
            if (!overlaps)
                continue;

            foreach (var ii in candidate.Interviewers)
                if (interviewerIds.Contains(ii.EmployeeId))
                    conflictingInterviewerIds.Add(ii.EmployeeId);
        }

        if (conflictingInterviewerIds.Count == 0)
            return [];

        // Resolve names for a readable warning.
        var names = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => conflictingInterviewerIds.Contains(e.Id))
            .Select(e => new { e.Id, e.FirstName, e.LastName })
            .ToListAsync(cancellationToken);

        return names
            .Select(n => $"{($"{n.FirstName} {n.LastName}").Trim()} already has another interview that overlaps this time slot.")
            .ToList();
    }

    /// <summary>
    /// FR-4/BR-6: schedule the tenant-aware reminder via the seam. Lead time is read from config
    /// (Recruitment:InterviewReminderLeadHours), defaulting to 24h. Returns the job id, or null when the
    /// scheduler is unavailable (tests) or the fire-time is already past.
    /// </summary>
    private string? ScheduleReminder(Interview interview)
    {
        if (_reminderScheduler is null)
            return null;

        var leadHours = _configuration.GetValue<int?>("Recruitment:InterviewReminderLeadHours") ?? DefaultReminderLeadHours;
        if (leadHours < 0) leadHours = DefaultReminderLeadHours;

        var fireAtUtc = interview.StartsAtUtc.AddHours(-leadHours);
        return _reminderScheduler.Schedule(_tenantContext.TenantId, interview.Id, fireAtUtc);
    }

    /// <summary>
    /// FR-3/BR-7: notify all interviewers (work email) + the applicant (application email) via the
    /// log-only seam. Never let a notification failure fail a committed interview write.
    /// </summary>
    private async Task NotifyParticipantsSafeAsync(string eventType, Interview interview, CancellationToken cancellationToken)
    {
        try
        {
            var applicantEmail = await _dbContext.Applicants
                .AsNoTracking()
                .Where(a => a.Id == interview.ApplicantId)
                .Select(a => a.Email)
                .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

            var interviewerEmails = await GetInterviewerEmailsAsync(interview, cancellationToken);

            await _notifications.NotifyInterviewAsync(
                eventType, interview.Id, interview.ApplicantId, interview.VacancyId,
                applicantEmail, interviewerEmails, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Interview notification failed (non-fatal). EventType={EventType}, InterviewId={InterviewId}, TenantId={TenantId}",
                eventType, interview.Id, _tenantContext.TenantId);
        }
    }

    private async Task<IReadOnlyList<string>> GetInterviewerEmailsAsync(Interview interview, CancellationToken cancellationToken)
    {
        var ids = interview.Interviewers.Select(ii => ii.EmployeeId).ToList();
        if (ids.Count == 0)
            return [];

        return await _dbContext.Employees
            .AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .Select(e => e.Email)
            .ToListAsync(cancellationToken);
    }

    private async Task<Result<InterviewDto>> BuildDtoResultAsync(
        Guid interviewId, IReadOnlyList<string> warnings, CancellationToken cancellationToken)
    {
        // Re-read with no-tracking + interviewers so the returned shape is consistent with the read paths.
        var interview = await _dbContext.Interviews
            .AsNoTracking()
            .Include(i => i.Interviewers)
            .FirstAsync(i => i.Id == interviewId, cancellationToken);

        return Result<InterviewDto>.Success(await ToDtoAsync(interview, warnings, cancellationToken));
    }

    private async Task<IReadOnlyList<InterviewDto>> ToDtosAsync(
        IReadOnlyList<Interview> interviews, CancellationToken cancellationToken)
    {
        var dtos = new List<InterviewDto>(interviews.Count);
        foreach (var interview in interviews)
            dtos.Add(await ToDtoAsync(interview, [], cancellationToken));
        return dtos;
    }

    private async Task<InterviewDto> ToDtoAsync(
        Interview interview, IReadOnlyList<string> warnings, CancellationToken cancellationToken)
    {
        var interviewerIds = interview.Interviewers.Select(ii => ii.EmployeeId).ToList();

        var employees = interviewerIds.Count == 0
            ? []
            : await _dbContext.Employees
                .AsNoTracking()
                .Where(e => interviewerIds.Contains(e.Id))
                .Select(e => new { e.Id, e.FirstName, e.LastName, e.Email })
                .ToListAsync(cancellationToken);

        var interviewers = interviewerIds
            .Select(id => employees.FirstOrDefault(e => e.Id == id))
            .Where(e => e is not null)
            .Select(e => new InterviewerDto
            {
                EmployeeId = e!.Id,
                FullName = $"{e.FirstName} {e.LastName}".Trim(),
                Email = e.Email,
            })
            .ToList();

        var vacancyTitle = await _dbContext.Vacancies
            .AsNoTracking()
            .Where(v => v.Id == interview.VacancyId)
            .Select(v => v.Title)
            .FirstOrDefaultAsync(cancellationToken);

        var applicantName = await _dbContext.Applicants
            .AsNoTracking()
            .Where(a => a.Id == interview.ApplicantId)
            .Select(a => $"{a.FirstName} {a.LastName}")
            .FirstOrDefaultAsync(cancellationToken);

        return new InterviewDto
        {
            Id = interview.Id,
            ApplicantId = interview.ApplicantId,
            VacancyId = interview.VacancyId,
            VacancyTitle = vacancyTitle,
            ApplicantName = applicantName?.Trim(),
            RoundNumber = interview.RoundNumber,
            InterviewType = interview.InterviewType,
            InterviewTypeName = interview.InterviewType.ToString(),
            ScheduledDate = interview.ScheduledDate,
            StartTime = interview.StartTime,
            DurationMinutes = interview.DurationMinutes,
            Location = interview.Location,
            VideoLink = interview.VideoLink,
            Notes = interview.Notes,
            Status = interview.Status,
            StatusName = interview.Status.ToString(),
            ReminderJobId = interview.ReminderJobId,
            Interviewers = interviewers,
            CreatedAt = interview.CreatedAt,
            Warnings = warnings,
        };
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
