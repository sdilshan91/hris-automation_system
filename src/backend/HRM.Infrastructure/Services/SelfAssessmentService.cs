using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Employee self-assessment service (US-PRF-002). Every read/write is scoped to the CALLING employee's own
/// record (resolved from ICurrentUser) AND their tenant (global query filter), so an employee can never see
/// another employee's data (NFR-2). The window gate (BR-1/AC-4), all-goals-rated + comment-length submit
/// rules (BR-2/FR-3), the weighted-score calc (FR-4) and the submitted-lock (BR-3) are enforced here.
/// </summary>
public sealed class SelfAssessmentService : ISelfAssessmentService
{
    private const int MinCommentLength = 20; // FR-3
    private const int MaxAchievement = 100;  // FR-1

    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IPerformanceNotificationService _notifications;
    private readonly ILogger<SelfAssessmentService> _logger;

    public SelfAssessmentService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IPerformanceNotificationService notifications,
        ILogger<SelfAssessmentService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<Result<SelfAssessmentDto>> GetMyAssessmentAsync(
        Guid cycleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<SelfAssessmentDto>.Failure("Tenant context is not resolved.", 400);

        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
            return Result<SelfAssessmentDto>.Failure(
                "The current user is not linked to an employee record.", 403, "no_employee_record");

        var cycle = await _dbContext.AppraisalCycles
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (cycle is null)
            return Result<SelfAssessmentDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        var goals = await LoadGoalsAsync(employee.Id, cycleId, cancellationToken);

        var assessment = await LoadAssessmentWithItemsAsync(employee.Id, cycleId, tracking: false, cancellationToken);

        return Result<SelfAssessmentDto>.Success(BuildDto(cycle, employee.Id, assessment, goals));
    }

    public Task<Result<SelfAssessmentDto>> SaveDraftAsync(
        SaveSelfAssessmentInput input, CancellationToken cancellationToken = default)
        => UpsertAsync(input, submit: false, cancellationToken);

    public Task<Result<SelfAssessmentDto>> SubmitAsync(
        SaveSelfAssessmentInput input, CancellationToken cancellationToken = default)
        => UpsertAsync(input, submit: true, cancellationToken);

    private async Task<Result<SelfAssessmentDto>> UpsertAsync(
        SaveSelfAssessmentInput input, bool submit, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return Result<SelfAssessmentDto>.Failure("Tenant context is not resolved.", 400);

        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
            return Result<SelfAssessmentDto>.Failure(
                "The current user is not linked to an employee record.", 403, "no_employee_record");

        var cycle = await _dbContext.AppraisalCycles
            .FirstOrDefaultAsync(c => c.Id == input.CycleId, cancellationToken);
        if (cycle is null)
            return Result<SelfAssessmentDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        // BR-1/AC-4: edits/submits only while the self-assessment window is open.
        if (!cycle.IsSelfAssessmentOpen(DateTime.UtcNow))
            return Result<SelfAssessmentDto>.Failure(
                "The self-assessment period for this cycle has ended.", 409, "self_assessment_closed");

        var goals = await LoadGoalsAsync(employee.Id, input.CycleId, cancellationToken);
        if (goals.Count == 0)
            return Result<SelfAssessmentDto>.Failure(
                "No goals have been assigned for this cycle.", 409, "no_goals_assigned");

        var goalIds = goals.Select(g => g.Id).ToHashSet();

        // Validate the submitted item ranges against the cycle's scale + only-known-goals.
        foreach (var item in input.Items)
        {
            if (!goalIds.Contains(item.GoalId))
                return Result<SelfAssessmentDto>.Failure(
                    "A submitted rating references a goal that is not assigned to you for this cycle.",
                    400, "unknown_goal");

            if (item.SelfRating is { } rating && (rating < 1 || rating > cycle.RatingScaleMax))
                return Result<SelfAssessmentDto>.Failure(
                    $"Self-rating must be between 1 and {cycle.RatingScaleMax}.", 422, "rating_out_of_range");

            if (item.AchievementPercentage is { } pct && (pct < 0 || pct > MaxAchievement))
                return Result<SelfAssessmentDto>.Failure(
                    "Achievement percentage must be between 0 and 100.", 422, "achievement_out_of_range");
        }

        var assessment = await LoadAssessmentWithItemsAsync(employee.Id, input.CycleId, tracking: true, cancellationToken);

        // BR-3: a submitted assessment is locked.
        if (assessment is not null && assessment.Status == SelfAssessmentStatus.Submitted)
            return Result<SelfAssessmentDto>.Failure(
                "This self-assessment has already been submitted and is locked.", 409, "already_submitted");

        if (assessment is null)
        {
            assessment = new SelfAssessment
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                CycleId = input.CycleId,
                EmployeeId = employee.Id,
                Status = SelfAssessmentStatus.Draft,
                IsDeleted = false,
            };
            _dbContext.SelfAssessments.Add(assessment);
        }

        // Merge each input item into a persisted item (one row per goal).
        var inputByGoal = input.Items.ToDictionary(i => i.GoalId);
        foreach (var goal in goals)
        {
            if (!inputByGoal.TryGetValue(goal.Id, out var src))
                continue; // goal not touched in this save — leave any existing draft row as-is

            var existing = assessment.Items.FirstOrDefault(i => i.GoalId == goal.Id);
            if (existing is null)
            {
                existing = new SelfAssessmentItem
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = _tenantContext.TenantId,
                    SelfAssessmentId = assessment.Id,
                    GoalId = goal.Id,
                    IsDeleted = false,
                };
                assessment.Items.Add(existing);
                _dbContext.SelfAssessmentItems.Add(existing);
            }

            existing.SelfRating = src.SelfRating;
            existing.AchievementPercentage = src.AchievementPercentage;
            existing.Comment = string.IsNullOrWhiteSpace(src.Comment) ? null : src.Comment.Trim();
        }

        if (submit)
        {
            // BR-2/FR-3: every assigned goal must be rated, with achievement set + a comment ≥20 chars.
            foreach (var goal in goals)
            {
                var item = assessment.Items.FirstOrDefault(i => i.GoalId == goal.Id);
                if (item is null || item.SelfRating is null)
                    return Result<SelfAssessmentDto>.Failure(
                        "All goals must be rated before submitting.", 422, "incomplete_ratings");

                if (item.AchievementPercentage is null)
                    return Result<SelfAssessmentDto>.Failure(
                        "Each goal must have an achievement percentage before submitting.", 422, "incomplete_ratings");

                if (string.IsNullOrWhiteSpace(item.Comment) || item.Comment.Trim().Length < MinCommentLength)
                    return Result<SelfAssessmentDto>.Failure(
                        $"Each goal comment must be at least {MinCommentLength} characters.", 422, "comment_too_short");
            }

            assessment.Status = SelfAssessmentStatus.Submitted;
            assessment.SubmittedAt = DateTime.UtcNow;
            assessment.WeightedSelfScore = ComputeWeightedScore(goals, assessment.Items);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<SelfAssessmentDto>.Failure(
                "This self-assessment was modified by another session. Reload and try again.", 409, "concurrency_conflict");
        }

        _logger.LogInformation(
            "Self-assessment {Action}. Id={Id}, EmployeeId={EmployeeId}, CycleId={CycleId}, Status={Status}, TenantId={TenantId}",
            submit ? "submitted" : "saved (draft)", assessment.Id, employee.Id, input.CycleId, assessment.Status, _tenantContext.TenantId);

        if (submit)
            await _notifications.NotifySelfAssessmentSubmittedAsync(
                assessment.Id, employee.Id, input.CycleId, cancellationToken);

        return Result<SelfAssessmentDto>.Success(BuildDto(cycle, employee.Id, assessment, goals));
    }

    // ── Weighted self-score (FR-4) ───────────────────────────────────
    // Weighted average of each goal's selfRating by its goal weight, expressed on the rating scale.
    // score = Σ(rating_i * weight_i) / Σ(weight_i). With weights summing to 100 this is a clean 0..max.
    internal static decimal ComputeWeightedScore(IReadOnlyList<Goal> goals, IReadOnlyList<SelfAssessmentItem> items)
    {
        decimal weightedSum = 0m;
        decimal totalWeight = 0m;
        var byGoal = items.ToDictionary(i => i.GoalId);
        foreach (var goal in goals)
        {
            if (!byGoal.TryGetValue(goal.Id, out var item) || item.SelfRating is null)
                continue;
            weightedSum += item.SelfRating.Value * goal.Weight;
            totalWeight += goal.Weight;
        }
        if (totalWeight == 0m)
            return 0m;
        return Math.Round(weightedSum / totalWeight, 2, MidpointRounding.AwayFromZero);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private Task<Employee?> GetCurrentEmployeeAsync(CancellationToken cancellationToken)
        => _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);

    private Task<List<Goal>> LoadGoalsAsync(Guid employeeId, Guid cycleId, CancellationToken cancellationToken)
        => _dbContext.Goals
            .AsNoTracking()
            .Where(g => g.EmployeeId == employeeId && g.CycleId == cycleId)
            .OrderBy(g => g.CreatedAt)
            .ToListAsync(cancellationToken);

    private async Task<SelfAssessment?> LoadAssessmentWithItemsAsync(
        Guid employeeId, Guid cycleId, bool tracking, CancellationToken cancellationToken)
    {
        IQueryable<SelfAssessment> query = _dbContext.SelfAssessments
            .Include(s => s.Items).ThenInclude(i => i.Attachments);
        if (!tracking)
            query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(
            s => s.EmployeeId == employeeId && s.CycleId == cycleId, cancellationToken);
    }

    private static SelfAssessmentDto BuildDto(
        AppraisalCycle cycle, Guid employeeId, SelfAssessment? assessment, IReadOnlyList<Goal> goals)
    {
        var itemsByGoal = assessment?.Items.ToDictionary(i => i.GoalId)
            ?? new Dictionary<Guid, SelfAssessmentItem>();

        var locked = assessment?.Status == SelfAssessmentStatus.Submitted;
        var open = cycle.IsSelfAssessmentOpen(DateTime.UtcNow);

        var itemDtos = goals.Select(g =>
        {
            itemsByGoal.TryGetValue(g.Id, out var item);
            return new SelfAssessmentItemDto
            {
                GoalId = g.Id,
                GoalTitle = g.Title,
                GoalDescription = g.Description,
                GoalWeight = g.Weight,
                GoalTargetValue = g.TargetValue,
                GoalMeasurementUnit = g.MeasurementUnit,
                GoalDueDate = g.DueDate,
                SelfRating = item?.SelfRating,
                AchievementPercentage = item?.AchievementPercentage,
                Comment = item?.Comment,
                Attachments = (item?.Attachments ?? [])
                    .Where(a => !a.IsDeleted)
                    .Select(a => new SelfAssessmentAttachmentDto
                    {
                        Id = a.Id,
                        GoalId = g.Id,
                        FileName = a.FileName,
                        ContentType = a.ContentType,
                        SizeBytes = a.SizeBytes,
                    }).ToList(),
            };
        }).ToList();

        return new SelfAssessmentDto
        {
            Id = assessment?.Id ?? Guid.Empty,
            CycleId = cycle.Id,
            EmployeeId = employeeId,
            Status = assessment?.Status ?? SelfAssessmentStatus.Draft,
            StatusName = (assessment?.Status ?? SelfAssessmentStatus.Draft).ToString(),
            WeightedSelfScore = assessment?.WeightedSelfScore,
            SubmittedAt = assessment?.SubmittedAt,
            RatingScaleMax = cycle.RatingScaleMax,
            SelfWeightPercent = cycle.SelfWeightPercent,
            IsSelfAssessmentOpen = open,
            IsLocked = locked,
            Items = itemDtos,
        };
    }
}
