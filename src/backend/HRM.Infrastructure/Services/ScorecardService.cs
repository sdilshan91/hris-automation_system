using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Recruitment;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Structured interview scorecard submission + reads (US-REC-006). All queries are tenant-scoped via
/// ITenantContext + the EF global query filter (AC-4). Submission enforces: the submitter is the assigned
/// interviewer (BR-1), one scorecard per interviewer per interview with edits until the lock period
/// (FR-2/BR-4), a mandatory recommendation (BR-3), and a computed average (FR-3). On submit it marks the
/// interview Completed when all assigned interviewers have submitted (FR-4), notifies the recruiter via the
/// log-only seam (FR-5), and writes an audit-log row (FR-7). Reads apply the anti-bias rule (FR-6/BR-5).
/// </summary>
public sealed class ScorecardService : IScorecardService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IRecruitmentNotificationService _notifications;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ScorecardService> _logger;

    /// <summary>BR-4: default lock period (hours after the interview) when not configured.</summary>
    private const int DefaultLockPeriodHours = 48;

    /// <summary>The permission a recruiter holds (sees ALL scorecards, exempt from the anti-bias rule, BR-5).</summary>
    private const string RecruitmentViewPermission = "Recruitment.View";

    public ScorecardService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IRecruitmentNotificationService notifications,
        IConfiguration configuration,
        ILogger<ScorecardService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _notifications = notifications;
        _configuration = configuration;
        _logger = logger;
    }

    // ── Submit / edit (AC-1/FR-1/FR-2/FR-3/BR-1/BR-3) ─────────────────

    public async Task<Result<ScorecardDto>> SubmitAsync(
        SubmitScorecardInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ScorecardDto>.Failure("Tenant context is not resolved.", 400);

        // BR-3: the overall recommendation is mandatory (defensive — the validator is the primary gate).
        if (!Enum.IsDefined(input.OverallRecommendation))
            return Result<ScorecardDto>.Failure("A valid overall recommendation is required.", 400, "recommendation_required");

        var ratings = input.Ratings ?? [];
        if (ratings.Count == 0)
            return Result<ScorecardDto>.Failure("At least one criterion rating is required.", 400, "ratings_required");

        // FR-1/BR-2: every rating must reference a known criterion key, rated within the 1-5 scale, with no
        // duplicate keys.
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in ratings)
        {
            if (!ScorecardCriteria.IsValidKey(r.CriterionKey))
                return Result<ScorecardDto>.Failure($"Unknown evaluation criterion '{r.CriterionKey}'.", 400, "unknown_criterion");
            if (r.Score < ScorecardCriteria.MinScore || r.Score > ScorecardCriteria.MaxScore)
                return Result<ScorecardDto>.Failure(
                    $"Each rating must be between {ScorecardCriteria.MinScore} and {ScorecardCriteria.MaxScore}.", 400, "score_out_of_range");
            if (!seenKeys.Add(r.CriterionKey))
                return Result<ScorecardDto>.Failure($"Duplicate rating for criterion '{r.CriterionKey}'.", 400, "duplicate_criterion");
        }

        // The interview must exist in this tenant (global filter ⇒ found ⇒ in-tenant). 404 otherwise so a
        // guessed cross-tenant id never resolves (AC-4).
        var interview = await _dbContext.Interviews
            .Include(i => i.Interviewers)
            .FirstOrDefaultAsync(i => i.Id == input.InterviewId, cancellationToken);
        if (interview is null)
            return Result<ScorecardDto>.Failure("Interview not found.", 404, "interview_not_found");

        if (interview.Status == InterviewStatus.Cancelled)
            return Result<ScorecardDto>.Failure("A scorecard cannot be submitted for a cancelled interview.", 409, "interview_cancelled");

        // BR-1: resolve the current user's employee record and confirm they are an assigned interviewer.
        var employeeId = await ResolveCurrentEmployeeIdAsync(cancellationToken);
        if (employeeId is null || !interview.Interviewers.Any(ii => ii.EmployeeId == employeeId.Value))
            return Result<ScorecardDto>.Failure(
                "Only an assigned interviewer can submit a scorecard for this interview.", 403, "not_assigned_interviewer");

        var average = ComputeAverage(ratings.Select(r => r.Score));

        // FR-2: one scorecard per interviewer per interview — find an existing one to edit, else create.
        var existing = await _dbContext.InterviewScorecards
            .Include(s => s.Ratings)
            .FirstOrDefaultAsync(
                s => s.InterviewId == interview.Id && s.InterviewerEmployeeId == employeeId.Value,
                cancellationToken);

        bool isNew = existing is null;
        InterviewScorecard scorecard;

        if (existing is not null)
        {
            // BR-4: edits are allowed only until the lock period has elapsed (immutable afterwards).
            if (DateTime.UtcNow >= existing.LockedAt)
                return Result<ScorecardDto>.Failure(
                    "The scorecard is locked and can no longer be edited.", 409, "scorecard_locked");

            existing.OverallRecommendation = input.OverallRecommendation;
            existing.AverageScore = average;
            existing.GeneralNotes = Trim(input.GeneralNotes);

            // Replace the rating set wholesale (simplest, version history is deferred — see note).
            var oldRatings = await _dbContext.ScorecardCriterionRatings
                .Where(r => r.ScorecardId == existing.Id)
                .ToListAsync(cancellationToken);
            _dbContext.ScorecardCriterionRatings.RemoveRange(oldRatings);

            foreach (var r in ratings)
                _dbContext.ScorecardCriterionRatings.Add(NewRating(existing.Id, r));

            scorecard = existing;
        }
        else
        {
            var scorecardId = BaseEntity.NewUuidV7();
            var lockedAt = interview.StartsAtUtc.AddHours(ResolveLockPeriodHours());

            scorecard = new InterviewScorecard
            {
                Id = scorecardId,
                TenantId = _tenantContext.TenantId,
                InterviewId = interview.Id,
                InterviewerEmployeeId = employeeId.Value,
                OverallRecommendation = input.OverallRecommendation,
                AverageScore = average,
                GeneralNotes = Trim(input.GeneralNotes),
                SubmittedAt = DateTime.UtcNow,
                LockedAt = lockedAt,
                IsDeleted = false,
                Ratings = ratings.Select(r => NewRating(scorecardId, r)).ToList(),
            };

            _dbContext.InterviewScorecards.Add(scorecard);
        }

        // FR-7: audit the submission/edit.
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = isNew ? "recruitment.scorecard.submitted" : "recruitment.scorecard.edited",
            Detail =
                $"Scorecard {scorecard.Id} {(isNew ? "submitted" : "edited")} for interview {interview.Id} " +
                $"by interviewer {employeeId.Value}. Recommendation={input.OverallRecommendation}, Average={average:0.##}",
            CreatedAt = DateTime.UtcNow,
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        // FR-4: mark the interview Completed once every assigned interviewer has a scorecard.
        await MaybeCompleteInterviewAsync(interview, cancellationToken);

        _logger.LogInformation(
            "Scorecard {Action}. ScorecardId={ScorecardId}, InterviewId={InterviewId}, InterviewerEmployeeId={EmployeeId}, " +
            "Recommendation={Recommendation}, Average={Average}, TenantId={TenantId}",
            isNew ? "submitted" : "edited", scorecard.Id, interview.Id, employeeId.Value,
            input.OverallRecommendation, average, _tenantContext.TenantId);

        // FR-5: notify the recruiter (log-only seam) — never let a notification failure fail the write.
        await NotifyRecruiterSafeAsync(scorecard, interview, cancellationToken);

        // Re-read with ratings (no-tracking) so the returned shape matches the read paths.
        var saved = await _dbContext.InterviewScorecards
            .AsNoTracking()
            .Include(s => s.Ratings)
            .FirstAsync(s => s.Id == scorecard.Id, cancellationToken);

        return Result<ScorecardDto>.Success(await ToDtoAsync(saved, cancellationToken));
    }

    // ── Reads (AC-2/AC-3/FR-6/BR-5) ───────────────────────────────────

    public async Task<Result<ScorecardSummaryDto>> GetForInterviewAsync(
        Guid interviewId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ScorecardSummaryDto>.Failure("Tenant context is not resolved.", 400);

        var interview = await _dbContext.Interviews
            .AsNoTracking()
            .Include(i => i.Interviewers)
            .FirstOrDefaultAsync(i => i.Id == interviewId, cancellationToken);
        if (interview is null)
            return Result<ScorecardSummaryDto>.Failure("Interview not found.", 404, "interview_not_found");

        var scorecards = await _dbContext.InterviewScorecards
            .AsNoTracking()
            .Include(s => s.Ratings)
            .Where(s => s.InterviewId == interviewId)
            .ToListAsync(cancellationToken);

        var summary = await BuildSummaryAsync(scorecards, new[] { interview }, cancellationToken);
        return Result<ScorecardSummaryDto>.Success(summary);
    }

    public async Task<Result<ScorecardSummaryDto>> GetForApplicantAsync(
        Guid applicantId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ScorecardSummaryDto>.Failure("Tenant context is not resolved.", 400);

        // The applicant must exist in this tenant (AC-4).
        var applicantExists = await _dbContext.Applicants
            .AsNoTracking()
            .AnyAsync(a => a.Id == applicantId, cancellationToken);
        if (!applicantExists)
            return Result<ScorecardSummaryDto>.Failure("Applicant not found.", 404, "applicant_not_found");

        var interviews = await _dbContext.Interviews
            .AsNoTracking()
            .Include(i => i.Interviewers)
            .Where(i => i.ApplicantId == applicantId)
            .ToListAsync(cancellationToken);

        var interviewIds = interviews.Select(i => i.Id).ToList();

        var scorecards = interviewIds.Count == 0
            ? []
            : await _dbContext.InterviewScorecards
                .AsNoTracking()
                .Include(s => s.Ratings)
                .Where(s => interviewIds.Contains(s.InterviewId))
                .ToListAsync(cancellationToken);

        var summary = await BuildSummaryAsync(scorecards, interviews, cancellationToken);
        return Result<ScorecardSummaryDto>.Success(summary);
    }

    public Result<IReadOnlyList<ScorecardCriterionDto>> GetCriteria()
    {
        IReadOnlyList<ScorecardCriterionDto> criteria = ScorecardCriteria.Default
            .Select(c => new ScorecardCriterionDto { Key = c.Key, Label = c.Label })
            .ToList();
        return Result<IReadOnlyList<ScorecardCriterionDto>>.Success(criteria);
    }

    // ── Anti-bias filtering + aggregate (FR-6/BR-5/FR-3) ──────────────

    /// <summary>
    /// Builds the consolidated summary applying the anti-bias rule (FR-6/BR-5). The rule keys off whether the
    /// caller is an ASSIGNED INTERVIEWER on the supplied interviews — NOT merely on the Recruitment.View
    /// permission (an interviewer also holds View to reach the endpoint). A caller who is not an assigned
    /// interviewer on any of these interviews is treated as a recruiter and sees every scorecard. An assigned
    /// interviewer only sees peers' scorecards on an interview once they have submitted their OWN for that
    /// interview; otherwise peers' cards are hidden (and counted in <c>HiddenCount</c>). They always see their
    /// own card. (A caller who is neither — no employee record, no View — would have been blocked at the API
    /// layer; defensively such a caller sees nothing.)
    /// </summary>
    private async Task<ScorecardSummaryDto> BuildSummaryAsync(
        IReadOnlyList<InterviewScorecard> scorecards,
        IReadOnlyList<Interview> interviews,
        CancellationToken cancellationToken)
    {
        var employeeId = await ResolveCurrentEmployeeIdAsync(cancellationToken);

        // The interviews (of those supplied) the caller is an assigned interviewer on.
        var assignedInterviewIds = employeeId is null
            ? new HashSet<Guid>()
            : interviews.Where(i => i.Interviewers.Any(ii => ii.EmployeeId == employeeId.Value))
                        .Select(i => i.Id)
                        .ToHashSet();

        var isAssignedInterviewer = assignedInterviewIds.Count > 0;
        var hasView = _currentUser.Permissions.Contains(RecruitmentViewPermission);

        List<InterviewScorecard> visible;
        int hidden = 0;
        bool antiBiasApplies = false;

        if (!isAssignedInterviewer && hasView)
        {
            // Pure recruiter — exempt from anti-bias (BR-5), sees all.
            visible = scorecards.ToList();
        }
        else if (!isAssignedInterviewer)
        {
            // Neither an assigned interviewer nor a recruiter — defensively show nothing.
            visible = new List<InterviewScorecard>();
            hidden = scorecards.Count;
        }
        else
        {
            // The caller's own submitted scorecards unlock peers' cards on THAT interview only.
            var ownInterviewIds = scorecards
                .Where(s => s.InterviewerEmployeeId == employeeId!.Value)
                .Select(s => s.InterviewId)
                .ToHashSet();

            visible = new List<InterviewScorecard>(scorecards.Count);
            foreach (var s in scorecards)
            {
                bool isOwn = s.InterviewerEmployeeId == employeeId!.Value;
                bool unlocked = ownInterviewIds.Contains(s.InterviewId);
                if (isOwn || unlocked)
                {
                    visible.Add(s);
                }
                else
                {
                    hidden++;
                    antiBiasApplies = true;
                }
            }
        }

        var dtos = new List<ScorecardDto>(visible.Count);
        foreach (var s in visible.OrderBy(s => s.SubmittedAt))
            dtos.Add(await ToDtoAsync(s, cancellationToken));

        decimal? aggregate = dtos.Count == 0
            ? null
            : Math.Round(dtos.Average(d => d.AverageScore), 2, MidpointRounding.AwayFromZero);

        return new ScorecardSummaryDto
        {
            Scorecards = dtos,
            AggregateAverageScore = aggregate,
            HiddenCount = hidden,
            AntiBiasApplies = antiBiasApplies,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────

    /// <summary>FR-4: mark the interview Completed once every assigned interviewer has a scorecard.</summary>
    private async Task MaybeCompleteInterviewAsync(Interview interview, CancellationToken cancellationToken)
    {
        if (interview.Status == InterviewStatus.Completed || interview.Status == InterviewStatus.Cancelled)
            return;

        var assignedIds = interview.Interviewers.Select(ii => ii.EmployeeId).Distinct().ToList();
        if (assignedIds.Count == 0)
            return;

        var submittedIds = await _dbContext.InterviewScorecards
            .Where(s => s.InterviewId == interview.Id)
            .Select(s => s.InterviewerEmployeeId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (assignedIds.All(id => submittedIds.Contains(id)))
        {
            // Re-load tracked to flip the status (the interview was loaded tracked in SubmitAsync).
            interview.Status = InterviewStatus.Completed;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Interview marked Completed — all {Count} assigned interviewer(s) submitted scorecards. " +
                "InterviewId={InterviewId}, TenantId={TenantId}",
                assignedIds.Count, interview.Id, _tenantContext.TenantId);
        }
    }

    private async Task NotifyRecruiterSafeAsync(
        InterviewScorecard scorecard, Interview interview, CancellationToken cancellationToken)
    {
        try
        {
            await _notifications.NotifyScorecardSubmittedAsync(
                scorecard.Id, interview.Id, interview.ApplicantId, interview.VacancyId,
                scorecard.InterviewerEmployeeId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Scorecard notification failed (non-fatal). ScorecardId={ScorecardId}, InterviewId={InterviewId}, TenantId={TenantId}",
                scorecard.Id, interview.Id, _tenantContext.TenantId);
        }
    }

    /// <summary>Maps the current user (JWT) to their tenant employee record id, or null when none.</summary>
    private async Task<Guid?> ResolveCurrentEmployeeIdAsync(CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            return null;

        var userId = _currentUser.UserId;
        return await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private int ResolveLockPeriodHours()
    {
        var hours = _configuration.GetValue<int?>("Recruitment:ScorecardLockPeriodHours") ?? DefaultLockPeriodHours;
        return hours < 0 ? DefaultLockPeriodHours : hours;
    }

    private static decimal ComputeAverage(IEnumerable<int> scores)
    {
        var list = scores.ToList();
        if (list.Count == 0)
            return 0m;
        return Math.Round(list.Average(s => (decimal)s), 2, MidpointRounding.AwayFromZero);
    }

    private ScorecardCriterionRating NewRating(Guid scorecardId, CriterionRatingInput r) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = _tenantContext.TenantId,
        ScorecardId = scorecardId,
        CriterionKey = r.CriterionKey,
        Score = r.Score,
        Comment = Trim(r.Comment),
        IsDeleted = false,
    };

    private async Task<ScorecardDto> ToDtoAsync(InterviewScorecard scorecard, CancellationToken cancellationToken)
    {
        var interviewerName = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.Id == scorecard.InterviewerEmployeeId)
            .Select(e => $"{e.FirstName} {e.LastName}")
            .FirstOrDefaultAsync(cancellationToken);

        var labelByKey = ScorecardCriteria.Default.ToDictionary(c => c.Key, c => c.Label, StringComparer.Ordinal);

        var ratings = scorecard.Ratings
            .Select(r => new CriterionRatingDto
            {
                CriterionKey = r.CriterionKey,
                CriterionLabel = labelByKey.TryGetValue(r.CriterionKey, out var label) ? label : r.CriterionKey,
                Score = r.Score,
                Comment = r.Comment,
            })
            // Keep the canonical default order for a stable display.
            .OrderBy(r => IndexOfKey(r.CriterionKey))
            .ToList();

        return new ScorecardDto
        {
            Id = scorecard.Id,
            InterviewId = scorecard.InterviewId,
            InterviewerEmployeeId = scorecard.InterviewerEmployeeId,
            InterviewerName = interviewerName?.Trim() ?? string.Empty,
            OverallRecommendation = scorecard.OverallRecommendation,
            OverallRecommendationName = scorecard.OverallRecommendation.ToString(),
            AverageScore = scorecard.AverageScore,
            GeneralNotes = scorecard.GeneralNotes,
            Ratings = ratings,
            SubmittedAt = scorecard.SubmittedAt,
            LockedAt = scorecard.LockedAt,
            IsLocked = DateTime.UtcNow >= scorecard.LockedAt,
        };
    }

    private static int IndexOfKey(string key)
    {
        for (int i = 0; i < ScorecardCriteria.Default.Count; i++)
            if (ScorecardCriteria.Default[i].Key == key)
                return i;
        return int.MaxValue;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
