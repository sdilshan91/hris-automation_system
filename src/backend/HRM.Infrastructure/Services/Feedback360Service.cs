using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// 360-degree feedback submission + aggregation service (US-PRF-005 AC-3/AC-4/FR-4/FR-6). A reviewer submits
/// their feedback (one per reviewee per cycle, BR-3) which marks their assignment Completed; HR/manager views
/// the aggregated results (per-competency + per-category averages, composite score FR-6, anonymized entries).
/// Anonymity is CAPTURED AT SUBMIT TIME (BR-5) and ENFORCED IN THE PROJECTION (NFR-3/FR-5) — when on, the
/// reviewer identifiers are never written into the result DTO, not merely hidden in the UI. Every read/write
/// is tenant-scoped via the EF global query filter (NFR-2).
/// </summary>
public sealed class Feedback360Service : IFeedback360Service
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<Feedback360Service> _logger;

    public Feedback360Service(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<Feedback360Service> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ── Submit feedback (AC-3) ───────────────────────────────────────

    public async Task<Result<Feedback360ResultEntryDto>> SubmitFeedbackAsync(
        SubmitFeedback360Input input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<Feedback360ResultEntryDto>.Failure("Tenant context is not resolved.", 400);

        var reviewer = await GetCurrentEmployeeAsync(cancellationToken);
        if (reviewer is null)
            return Result<Feedback360ResultEntryDto>.Failure(
                "The current user is not linked to an employee record.", 403, "no_employee_record");

        var cycle = await _dbContext.AppraisalCycles.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == input.CycleId, cancellationToken);
        if (cycle is null)
            return Result<Feedback360ResultEntryDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        if (!cycle.Is360Enabled)
            return Result<Feedback360ResultEntryDto>.Failure(
                "360-degree feedback is not enabled for this cycle.", 409, "not_360_enabled");

        // The reviewer must have a Pending assignment for this reviewee (gates who may submit, AC-3).
        var assignment = await _dbContext.ReviewerAssignments
            .FirstOrDefaultAsync(a => a.CycleId == input.CycleId
                && a.RevieweeEmployeeId == input.RevieweeEmployeeId
                && a.ReviewerEmployeeId == reviewer.Id, cancellationToken);
        if (assignment is null)
            return Result<Feedback360ResultEntryDto>.Failure(
                "You are not assigned to review this employee for this cycle.", 403, "not_assigned");

        // BR-3: one feedback per reviewer per reviewee per cycle.
        var duplicate = await _dbContext.Feedback360s
            .AnyAsync(f => f.CycleId == input.CycleId
                && f.RevieweeEmployeeId == input.RevieweeEmployeeId
                && f.ReviewerEmployeeId == reviewer.Id, cancellationToken);
        if (duplicate || assignment.Status == ReviewerAssignmentStatus.Completed)
            return Result<Feedback360ResultEntryDto>.Failure(
                "You have already submitted feedback for this employee in this cycle.", 409, "already_submitted");

        if (input.Items is null || input.Items.Count == 0)
            return Result<Feedback360ResultEntryDto>.Failure(
                "At least one competency rating is required.", 422, "no_ratings");

        // Validate each rating against the cycle scale (FR-4).
        foreach (var item in input.Items)
        {
            if (item.Rating < 1 || item.Rating > cycle.RatingScaleMax)
                return Result<Feedback360ResultEntryDto>.Failure(
                    $"Rating must be between 1 and {cycle.RatingScaleMax}.", 422, "rating_out_of_range");
        }

        var feedback = new Feedback360
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            CycleId = input.CycleId,
            RevieweeEmployeeId = input.RevieweeEmployeeId,
            ReviewerEmployeeId = reviewer.Id,
            Category = assignment.Category,
            // BR-5: capture anonymity AT SUBMIT TIME so it can never be retroactively changed for this record.
            IsAnonymous = cycle.IsAnonymousFeedback,
            OverallComment = string.IsNullOrWhiteSpace(input.OverallComment) ? null : input.OverallComment.Trim(),
            SubmittedAt = DateTime.UtcNow,
            IsDeleted = false,
        };

        foreach (var item in input.Items)
        {
            feedback.Items.Add(new Feedback360Item
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                Feedback360Id = feedback.Id,
                GoalId = item.GoalId == Guid.Empty ? null : item.GoalId,
                CompetencyKey = string.IsNullOrWhiteSpace(item.CompetencyKey) ? null : item.CompetencyKey.Trim(),
                Rating = item.Rating,
                Comment = string.IsNullOrWhiteSpace(item.Comment) ? null : item.Comment.Trim(),
                IsDeleted = false,
            });
        }

        _dbContext.Feedback360s.Add(feedback);

        // AC-3: mark the reviewer Completed + update the completion tracker.
        assignment.Status = ReviewerAssignmentStatus.Completed;
        assignment.CompletedAt = feedback.SubmittedAt;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "360 feedback submitted. FeedbackId={Id}, RevieweeId={RevieweeId}, ReviewerId={ReviewerId}, " +
            "Category={Category}, Anonymous={Anonymous}, CycleId={CycleId}, TenantId={TenantId}",
            feedback.Id, input.RevieweeEmployeeId, reviewer.Id, feedback.Category, feedback.IsAnonymous,
            input.CycleId, _tenantContext.TenantId);

        // Return the just-submitted entry — projected through the anonymity rule (the submitter is the
        // reviewer themselves, but we keep the projection consistent so identity never leaks via this path).
        var labels = await BuildLabelMapAsync(input.RevieweeEmployeeId, input.CycleId, cancellationToken);
        var reviewerName = feedback.IsAnonymous ? null : FullName(reviewer);
        return Result<Feedback360ResultEntryDto>.Success(ProjectEntry(feedback, reviewerName, labels));
    }

    // ── Aggregated results (AC-4/FR-6) ───────────────────────────────

    public Task<Result<Feedback360ResultsDto>> GetResultsAsync(
        Guid revieweeEmployeeId, Guid cycleId, CancellationToken cancellationToken = default)
        => BuildResultsAsync(revieweeEmployeeId, cycleId, cancellationToken);

    public Task<Result<Feedback360ResultsDto>> GetReportDataAsync(
        Guid revieweeEmployeeId, Guid cycleId, CancellationToken cancellationToken = default)
        => BuildResultsAsync(revieweeEmployeeId, cycleId, cancellationToken);

    private async Task<Result<Feedback360ResultsDto>> BuildResultsAsync(
        Guid revieweeEmployeeId, Guid cycleId, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return Result<Feedback360ResultsDto>.Failure("Tenant context is not resolved.", 400);

        // HR/manager only (Performance.Review.All for results, NFR-3/FR-5).
        if (!_currentUser.Permissions.Contains(PermissionCatalog.Performance.ReviewAll))
            return Result<Feedback360ResultsDto>.Failure(
                "You do not have permission to view 360 results.", 403, "forbidden");

        var cycle = await _dbContext.AppraisalCycles.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (cycle is null)
            return Result<Feedback360ResultsDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        var reviewee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == revieweeEmployeeId, cancellationToken);
        if (reviewee is null)
            return Result<Feedback360ResultsDto>.Failure("Employee not found.", 404, "employee_not_found");

        var feedbacks = await _dbContext.Feedback360s
            .AsNoTracking()
            .Include(f => f.Items)
            .Where(f => f.CycleId == cycleId && f.RevieweeEmployeeId == revieweeEmployeeId)
            .ToListAsync(cancellationToken);

        var assignments = await _dbContext.ReviewerAssignments
            .AsNoTracking()
            .Where(a => a.CycleId == cycleId && a.RevieweeEmployeeId == revieweeEmployeeId)
            .ToListAsync(cancellationToken);

        var labels = await BuildLabelMapAsync(revieweeEmployeeId, cycleId, cancellationToken);

        // Reviewer-name lookup ONLY for the non-anonymous feedbacks (NFR-3 — we never even load names we
        // would not be allowed to surface).
        var nonAnonReviewerIds = feedbacks.Where(f => !f.IsAnonymous).Select(f => f.ReviewerEmployeeId).ToHashSet();
        var reviewerNames = await LoadNameLookupAsync(nonAnonReviewerIds, cancellationToken);

        var dto = Aggregate(cycle, reviewee, feedbacks, assignments, labels, reviewerNames);
        return Result<Feedback360ResultsDto>.Success(dto);
    }

    // ── Aggregation (pure given the loaded rows) ─────────────────────

    private static Feedback360ResultsDto Aggregate(
        AppraisalCycle cycle, Employee reviewee,
        IReadOnlyList<Feedback360> feedbacks, IReadOnlyList<ReviewerAssignment> assignments,
        IReadOnlyDictionary<string, string> labels, IReadOnlyDictionary<Guid, string> reviewerNames)
    {
        var allItems = feedbacks.SelectMany(f => f.Items.Where(i => !i.IsDeleted)).ToList();

        // Per-competency averages (radar/bar chart, AC-4).
        var competencyAverages = allItems
            .GroupBy(i => ItemKey(i))
            .Select(g => new CompetencyAverageDto
            {
                GoalId = g.First().GoalId,
                CompetencyKey = g.First().CompetencyKey,
                Label = ResolveLabel(g.First(), labels),
                AverageRating = Math.Round((decimal)g.Average(i => i.Rating), 2, MidpointRounding.AwayFromZero),
                ResponseCount = g.Count(),
            })
            .OrderBy(c => c.Label)
            .ToList();

        // Per-category averages (the radar comparing self/manager/peer/report, AC-4).
        decimal? CategoryAvg(ReviewerCategory cat)
        {
            var items = feedbacks.Where(f => f.Category == cat)
                .SelectMany(f => f.Items.Where(i => !i.IsDeleted)).ToList();
            return items.Count == 0 ? null
                : Math.Round((decimal)items.Average(i => i.Rating), 2, MidpointRounding.AwayFromZero);
        }
        int CategoryResponses(ReviewerCategory cat) => feedbacks.Count(f => f.Category == cat);

        var weights = new[]
        {
            (ReviewerCategory.Self, cycle.ThreeSixtySelfWeightPercent),
            (ReviewerCategory.Manager, cycle.ThreeSixtyManagerWeightPercent),
            (ReviewerCategory.Peer, cycle.ThreeSixtyPeerWeightPercent),
            (ReviewerCategory.DirectReport, cycle.ThreeSixtyReportWeightPercent),
        };

        var categoryAverages = weights.Select(w => new CategoryAverageDto
        {
            Category = w.Item1,
            CategoryName = w.Item1.ToString(),
            AverageRating = CategoryAvg(w.Item1),
            ResponseCount = CategoryResponses(w.Item1),
            Weight = w.Item2,
        }).ToList();

        // Composite score (FR-6) — pure domain calc.
        var composite = ThreeSixtyScoreCalculator.ComputeComposite(
            new ThreeSixtyScoreCalculator.CategoryAverages(
                CategoryAvg(ReviewerCategory.Self),
                CategoryAvg(ReviewerCategory.Manager),
                CategoryAvg(ReviewerCategory.Peer),
                CategoryAvg(ReviewerCategory.DirectReport)),
            new ThreeSixtyScoreCalculator.CategoryWeights(
                cycle.ThreeSixtySelfWeightPercent,
                cycle.ThreeSixtyManagerWeightPercent,
                cycle.ThreeSixtyPeerWeightPercent,
                cycle.ThreeSixtyReportWeightPercent));

        // Completion tracker (AC-3/AC-4).
        var completion = Enum.GetValues<ReviewerCategory>().Select(cat => new CategoryCompletionDto
        {
            Category = cat,
            CategoryName = cat.ToString(),
            Assigned = assignments.Count(a => a.Category == cat),
            Completed = assignments.Count(a => a.Category == cat && a.Status == ReviewerAssignmentStatus.Completed),
        }).ToList();

        // Individual entries — reviewer identity OMITTED when the feedback is anonymous (NFR-3/FR-5).
        var entries = feedbacks
            .OrderBy(f => f.Category).ThenBy(f => f.SubmittedAt)
            .Select(f =>
            {
                string? name = null;
                if (!f.IsAnonymous && reviewerNames.TryGetValue(f.ReviewerEmployeeId, out var n))
                    name = n;
                return ProjectEntry(f, name, labels);
            })
            .ToList();

        // BR-4: minimum-peer release gate.
        var peerResponses = CategoryResponses(ReviewerCategory.Peer);
        var thresholdMet = peerResponses >= cycle.Min360PeerReviewers;
        var warning = thresholdMet
            ? null
            : $"Only {peerResponses} of the required {cycle.Min360PeerReviewers} peer reviewer(s) have submitted "
              + "feedback. Releasing the results now may not be representative.";

        return new Feedback360ResultsDto
        {
            CycleId = cycle.Id,
            RevieweeEmployeeId = reviewee.Id,
            RevieweeName = FullName(reviewee),
            IsAnonymousFeedback = cycle.IsAnonymousFeedback,
            RatingScaleMax = cycle.RatingScaleMax,
            CompositeScore = composite,
            CompetencyAverages = competencyAverages,
            CategoryAverages = categoryAverages,
            Completion = completion,
            Entries = entries,
            MinPeerReviewers = cycle.Min360PeerReviewers,
            PeerResponseCount = peerResponses,
            MinPeerThresholdMet = thresholdMet,
            ReleaseWarning = warning,
        };
    }

    // ── Projection (anonymity rule lives here, NFR-3/FR-5) ───────────

    private static Feedback360ResultEntryDto ProjectEntry(
        Feedback360 f, string? reviewerName, IReadOnlyDictionary<string, string> labels)
        => new()
        {
            FeedbackId = f.Id,
            Category = f.Category,
            CategoryName = f.Category.ToString(),
            IsAnonymous = f.IsAnonymous,
            // NFR-3/FR-5: when anonymous, neither the id nor the name is ever populated.
            ReviewerEmployeeId = f.IsAnonymous ? null : f.ReviewerEmployeeId,
            ReviewerName = f.IsAnonymous ? null : reviewerName,
            OverallComment = f.OverallComment,
            SubmittedAt = f.SubmittedAt,
            Items = f.Items.Where(i => !i.IsDeleted).Select(i => new Feedback360ResultItemDto
            {
                GoalId = i.GoalId,
                CompetencyKey = i.CompetencyKey,
                Label = ResolveLabel(i, labels),
                Rating = i.Rating,
                Comment = i.Comment,
            }).ToList(),
        };

    // ── Helpers ──────────────────────────────────────────────────────

    private Task<Employee?> GetCurrentEmployeeAsync(CancellationToken cancellationToken)
        => _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);

    /// <summary>Maps each goal id to its title so goal-keyed items get a human label (competency keys are self-labeling).</summary>
    private async Task<Dictionary<string, string>> BuildLabelMapAsync(
        Guid revieweeEmployeeId, Guid cycleId, CancellationToken cancellationToken)
    {
        var goals = await _dbContext.Goals.AsNoTracking()
            .Where(g => g.EmployeeId == revieweeEmployeeId && g.CycleId == cycleId)
            .Select(g => new { g.Id, g.Title })
            .ToListAsync(cancellationToken);
        return goals.ToDictionary(g => $"goal:{g.Id}", g => g.Title);
    }

    private async Task<Dictionary<Guid, string>> LoadNameLookupAsync(HashSet<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();
        var rows = await _dbContext.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.FirstName, e.LastName })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Id, r => $"{r.FirstName} {r.LastName}".Trim());
    }

    private static string ItemKey(Feedback360Item i)
        => i.GoalId is { } gid ? $"goal:{gid}" : $"comp:{i.CompetencyKey}";

    private static string ResolveLabel(Feedback360Item i, IReadOnlyDictionary<string, string> labels)
    {
        if (i.GoalId is { } gid && labels.TryGetValue($"goal:{gid}", out var title))
            return title;
        return i.CompetencyKey ?? (i.GoalId is { } g ? g.ToString() : "Competency");
    }

    private static string FullName(Employee e) => $"{e.FirstName} {e.LastName}".Trim();
}
