using System.Text.Json;
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
/// Manager performance-review service (US-PRF-003). Every read/write is tenant-scoped via ITenantContext +
/// the EF global query filter (NFR-2). The direct-report scope (BR-2 — mirrors GoalService) and the
/// HR-override / reopen (BR-3/AC-5) are enforced here; so is the manager-review-window gate (BR-1/AC-5), the
/// all-goals-rated + comment-length submit rules (AC-3/FR-3), the weighted-manager-score calc and the FINAL
/// combined-score blend (BR-4). Audit of submissions (FR-7) rides the AuditInterceptor (manager user id +
/// timestamp stamped on SaveChanges) plus structured Serilog entries. Optimistic concurrency (NFR-3) rides
/// the xmin token.
/// </summary>
public sealed class ManagerReviewService : IManagerReviewService
{
    private const int MinCommentLength = 20; // FR-3

    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IPerformanceNotificationService _notifications;
    private readonly ILogger<ManagerReviewService> _logger;

    public ManagerReviewService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IPerformanceNotificationService notifications,
        ILogger<ManagerReviewService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _notifications = notifications;
        _logger = logger;
    }

    // ── Workspace (AC-1) ─────────────────────────────────────────────

    public async Task<Result<ManagerReviewDto>> GetReviewWorkspaceAsync(
        Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ManagerReviewDto>.Failure("Tenant context is not resolved.", 400);

        var authz = await AuthorizeForEmployeeAsync(employeeId, cancellationToken);
        if (authz.IsFailure)
            return Result<ManagerReviewDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        var cycle = await _dbContext.AppraisalCycles
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (cycle is null)
            return Result<ManagerReviewDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        var employee = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (employee is null)
            return Result<ManagerReviewDto>.Failure("Employee not found.", 404, "employee_not_found");

        var goals = await LoadGoalsAsync(employeeId, cycleId, cancellationToken);
        var selfAssessment = await LoadSelfAssessmentAsync(employeeId, cycleId, cancellationToken);
        var review = await LoadReviewWithItemsAsync(employeeId, cycleId, tracking: false, cancellationToken);

        return Result<ManagerReviewDto>.Success(
            BuildDto(cycle, employee, await ResolveJobTitleAsync(employee.JobTitleId, cancellationToken),
                review, goals, selfAssessment));
    }

    // ── Save / Submit (AC-2/AC-3) ────────────────────────────────────

    public Task<Result<ManagerReviewDto>> SaveDraftAsync(
        SaveManagerReviewInput input, CancellationToken cancellationToken = default)
        => UpsertAsync(input, submit: false, cancellationToken);

    public Task<Result<ManagerReviewDto>> SubmitAsync(
        SaveManagerReviewInput input, CancellationToken cancellationToken = default)
        => UpsertAsync(input, submit: true, cancellationToken);

    private async Task<Result<ManagerReviewDto>> UpsertAsync(
        SaveManagerReviewInput input, bool submit, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return Result<ManagerReviewDto>.Failure("Tenant context is not resolved.", 400);

        var authz = await AuthorizeForEmployeeAsync(input.EmployeeId, cancellationToken);
        if (authz.IsFailure)
            return Result<ManagerReviewDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        var employee = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == input.EmployeeId, cancellationToken);
        if (employee is null)
            return Result<ManagerReviewDto>.Failure("Employee not found.", 404, "employee_not_found");

        var cycle = await _dbContext.AppraisalCycles
            .FirstOrDefaultAsync(c => c.Id == input.CycleId, cancellationToken);
        if (cycle is null)
            return Result<ManagerReviewDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        // BR-1/AC-5: edits/submits only while the manager-review window is open.
        if (!cycle.IsManagerReviewOpen(DateTime.UtcNow))
            return Result<ManagerReviewDto>.Failure(
                "The manager-review window for this cycle is not open.", 409, "manager_review_closed");

        var goals = await LoadGoalsAsync(input.EmployeeId, input.CycleId, cancellationToken);
        if (goals.Count == 0)
            return Result<ManagerReviewDto>.Failure(
                "No goals have been assigned to this employee for this cycle.", 409, "no_goals_assigned");

        var goalIds = goals.Select(g => g.Id).ToHashSet();

        // Validate the submitted item ranges against the cycle's scale + only-known-goals.
        foreach (var item in input.Items)
        {
            if (!goalIds.Contains(item.GoalId))
                return Result<ManagerReviewDto>.Failure(
                    "A submitted rating references a goal that is not assigned to this employee for this cycle.",
                    400, "unknown_goal");

            if (item.ManagerRating is { } rating && (rating < 1 || rating > cycle.RatingScaleMax))
                return Result<ManagerReviewDto>.Failure(
                    $"Manager rating must be between 1 and {cycle.RatingScaleMax}.", 422, "rating_out_of_range");
        }

        var review = await LoadReviewWithItemsAsync(input.EmployeeId, input.CycleId, tracking: true, cancellationToken);

        // ISSUE-111 (US-PRF-005 FR-7 audit): snapshot the pre-mutation state (null for a first-time draft) so
        // the audit row can capture before/after. Captured BEFORE the merge loop mutates items/scores.
        var beforeSnapshot = review is null ? null : SnapshotReview(review);

        // AC-5/BR-1: a submitted review is locked. HR can reopen it explicitly via ReopenAsync first.
        if (review is not null && review.Status == ManagerReviewStatus.Submitted)
            return Result<ManagerReviewDto>.Failure(
                "This review has already been submitted and is locked. Reopen it before editing.",
                409, "already_submitted");

        var reviewer = await GetCurrentEmployeeAsync(cancellationToken);

        if (review is null)
        {
            review = new ManagerReview
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                CycleId = input.CycleId,
                EmployeeId = input.EmployeeId,
                ReviewerEmployeeId = reviewer?.Id,
                Status = ManagerReviewStatus.Draft,
                Flag = ReviewFlag.None,
                IsDeleted = false,
            };
            _dbContext.ManagerReviews.Add(review);
        }
        else if (review.ReviewerEmployeeId is null && reviewer is not null)
        {
            review.ReviewerEmployeeId = reviewer.Id;
        }

        review.SummaryComment = string.IsNullOrWhiteSpace(input.SummaryComment) ? null : input.SummaryComment.Trim();
        review.Flag = input.Flag;

        // Merge each input item into a persisted item (one row per goal).
        var inputByGoal = input.Items.ToDictionary(i => i.GoalId);
        foreach (var goal in goals)
        {
            if (!inputByGoal.TryGetValue(goal.Id, out var src))
                continue; // goal not touched in this save — leave any existing draft row as-is

            var existing = review.Items.FirstOrDefault(i => i.GoalId == goal.Id);
            if (existing is null)
            {
                existing = new ManagerReviewItem
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = _tenantContext.TenantId,
                    ManagerReviewId = review.Id,
                    GoalId = goal.Id,
                    IsDeleted = false,
                };
                review.Items.Add(existing);
                _dbContext.ManagerReviewItems.Add(existing);
            }

            existing.ManagerRating = src.ManagerRating;
            existing.ManagerComment = string.IsNullOrWhiteSpace(src.ManagerComment) ? null : src.ManagerComment.Trim();
        }

        decimal? selfScore = null;
        if (submit)
        {
            // AC-3/FR-3: every assigned goal must be rated with a comment ≥20 chars. Collect the unrated
            // goals so the caller can show the exact list (AC-3).
            var unrated = new List<Guid>();
            var shortComment = new List<Guid>();
            foreach (var goal in goals)
            {
                var item = review.Items.FirstOrDefault(i => i.GoalId == goal.Id);
                if (item is null || item.ManagerRating is null)
                {
                    unrated.Add(goal.Id);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(item.ManagerComment) || item.ManagerComment.Trim().Length < MinCommentLength)
                    shortComment.Add(goal.Id);
            }

            if (unrated.Count > 0)
                return Result<ManagerReviewDto>.Failure(
                    "All goals must be rated before submitting. Unrated goals: " + string.Join(", ", unrated),
                    422, "incomplete_ratings");

            if (shortComment.Count > 0)
                return Result<ManagerReviewDto>.Failure(
                    $"Each goal comment must be at least {MinCommentLength} characters. Goals needing a comment: "
                    + string.Join(", ", shortComment),
                    422, "comment_too_short");

            var managerScore = ComputeWeightedScore(goals, review.Items);

            // BR-4: the self-score comes from the employee's SUBMITTED self-assessment (0 if none submitted).
            var selfAssessment = await LoadSelfAssessmentAsync(input.EmployeeId, input.CycleId, cancellationToken);
            selfScore = selfAssessment is { Status: SelfAssessmentStatus.Submitted }
                ? selfAssessment.WeightedSelfScore ?? 0m
                : 0m;

            review.Status = ManagerReviewStatus.Submitted;
            review.SubmittedAt = DateTime.UtcNow;
            review.WeightedManagerScore = managerScore;
            review.SelfScoreAtSubmit = selfScore;

            if (cycle.Is360Enabled)
            {
                // BR-6 (US-PRF-005): incorporate the 360 composite alongside self + manager. The composite
                // weights self/manager/peers/reports by the cycle's 360 config and normalizes by the
                // categories that actually have feedback, so when only self+manager exist it degrades to a
                // self+manager blend by the 360 weights. The peer/report averages come from submitted
                // Feedback360 rows.
                var (peerAvg, reportAvg) = await LoadPeerReportAveragesAsync(
                    input.EmployeeId, input.CycleId, cancellationToken);
                review.FinalScore = ComputeFinalScoreWith360(
                    selfScore.Value, managerScore, peerAvg, reportAvg, cycle);
            }
            else
            {
                // Existing US-PRF-003 behavior — UNCHANGED when 360 is disabled (keeps existing tests green).
                review.FinalScore = ComputeFinalScore(
                    selfScore.Value, managerScore, cycle.SelfWeightPercent, cycle.ManagerWeightPercent);
            }
        }

        // ISSUE-111 (US-PRF-005 FR-7 audit): write a queryable tenant audit row for the rating submission.
        // FR-7 requires the manager's user id + timestamp — the standard actor (UserId) + CreatedAt fields
        // carry that. Distinguish save vs submit by the resulting status; a submit is the audit-critical event.
        var action = submit ? "ManagerReview.Submitted" : "ManagerReview.Saved";
        AddManagerReviewAudit(action, review.Id, before: beforeSnapshot, after: SnapshotReview(review),
            $"Manager review {(submit ? "submitted" : "saved")} for employee {input.EmployeeId}, cycle {input.CycleId}.");

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // NFR-3: HR and the manager edited the same row concurrently (xmin mismatch).
            return Result<ManagerReviewDto>.Failure(
                "This review was modified by another session. Reload and try again.", 409, "concurrency_conflict");
        }

        _logger.LogInformation(
            "Manager review {Action}. Id={Id}, EmployeeId={EmployeeId}, CycleId={CycleId}, Status={Status}, " +
            "FinalScore={FinalScore}, TenantId={TenantId}, By={User}",
            submit ? "submitted" : "saved (draft)", review.Id, input.EmployeeId, input.CycleId,
            review.Status, review.FinalScore, _tenantContext.TenantId, _currentUser.Email);

        if (submit)
            await _notifications.NotifyManagerReviewSubmittedAsync(
                review.Id, input.EmployeeId, input.CycleId, cancellationToken);

        return await BuildDtoResultAsync(cycle, employee, review.Id, cancellationToken);
    }

    // ── Reopen (AC-5/BR-3) ───────────────────────────────────────────

    public async Task<Result<ManagerReviewDto>> ReopenAsync(
        Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ManagerReviewDto>.Failure("Tenant context is not resolved.", 400);

        // BR-3: only HR (Review.All) may reopen a submitted review.
        if (!_currentUser.Permissions.Contains(PermissionCatalog.Performance.ReviewAll))
            return Result<ManagerReviewDto>.Failure(
                "Only HR can reopen a submitted review.", 403, "reopen_forbidden");

        var cycle = await _dbContext.AppraisalCycles
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (cycle is null)
            return Result<ManagerReviewDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        var employee = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (employee is null)
            return Result<ManagerReviewDto>.Failure("Employee not found.", 404, "employee_not_found");

        if (!cycle.IsManagerReviewOpen(DateTime.UtcNow))
            return Result<ManagerReviewDto>.Failure(
                "The manager-review window for this cycle is not open.", 409, "manager_review_closed");

        var review = await LoadReviewWithItemsAsync(employeeId, cycleId, tracking: true, cancellationToken);
        if (review is null)
            return Result<ManagerReviewDto>.Failure("Manager review not found.", 404, "review_not_found");

        if (review.Status != ManagerReviewStatus.Submitted)
            return Result<ManagerReviewDto>.Failure(
                "Only a submitted review can be reopened.", 409, "not_submitted");

        review.Status = ManagerReviewStatus.Draft;
        review.SubmittedAt = null;
        // Keep the computed scores visible; they are recomputed on the next submit.

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ManagerReviewDto>.Failure(
                "This review was modified by another session. Reload and try again.", 409, "concurrency_conflict");
        }

        _logger.LogInformation(
            "Manager review reopened. Id={Id}, EmployeeId={EmployeeId}, CycleId={CycleId}, TenantId={TenantId}, By={User}",
            review.Id, employeeId, cycleId, _tenantContext.TenantId, _currentUser.Email);

        return await BuildDtoResultAsync(cycle, employee, review.Id, cancellationToken);
    }

    // ── Team dashboard (AC-4) ────────────────────────────────────────

    public async Task<Result<TeamReviewsDashboardDto>> GetTeamReviewsAsync(
        Guid cycleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<TeamReviewsDashboardDto>.Failure("Tenant context is not resolved.", 400);

        var manager = await GetCurrentEmployeeAsync(cancellationToken);
        if (manager is null)
            return Result<TeamReviewsDashboardDto>.Failure(
                "The current user is not linked to an employee record.", 403, "no_employee_record");

        // AC-4: direct reports of the calling manager (BR-2). (HR with Review.All still review any employee
        // via the per-employee workspace; the team dashboard is intentionally the manager's own reports.)
        var reports = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.ReportsToEmployeeId == manager.Id)
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.EmployeeNo })
            .ToListAsync(cancellationToken);

        var reportIds = reports.Select(r => r.Id).ToList();

        var goals = reportIds.Count == 0
            ? new List<Goal>()
            : await _dbContext.Goals.AsNoTracking()
                .Where(g => g.CycleId == cycleId && reportIds.Contains(g.EmployeeId))
                .ToListAsync(cancellationToken);

        var selfAssessments = reportIds.Count == 0
            ? new List<SelfAssessment>()
            : await _dbContext.SelfAssessments.AsNoTracking()
                .Where(s => s.CycleId == cycleId && reportIds.Contains(s.EmployeeId))
                .ToListAsync(cancellationToken);

        var reviews = reportIds.Count == 0
            ? new List<ManagerReview>()
            : await _dbContext.ManagerReviews.AsNoTracking()
                .Where(r => r.CycleId == cycleId && reportIds.Contains(r.EmployeeId))
                .ToListAsync(cancellationToken);

        var goalsByEmp = goals.GroupBy(g => g.EmployeeId).ToDictionary(grp => grp.Key, grp => grp.Count());
        var selfByEmp = selfAssessments.ToDictionary(s => s.EmployeeId);
        var reviewByEmp = reviews.ToDictionary(r => r.EmployeeId);

        var members = reports.Select(r =>
        {
            goalsByEmp.TryGetValue(r.Id, out var goalCount);
            selfByEmp.TryGetValue(r.Id, out var self);
            reviewByEmp.TryGetValue(r.Id, out var review);

            return new TeamReviewStatusDto
            {
                EmployeeId = r.Id,
                EmployeeName = $"{r.FirstName} {r.LastName}".Trim(),
                EmployeeNo = r.EmployeeNo,
                GoalCount = goalCount,
                Status = AggregateStatus(self, review),
                WeightedSelfScore = self?.WeightedSelfScore,
                WeightedManagerScore = review?.WeightedManagerScore,
                FinalScore = review?.FinalScore,
            };
        }).ToList();

        return Result<TeamReviewsDashboardDto>.Success(new TeamReviewsDashboardDto
        {
            CycleId = cycleId,
            Members = members,
        });
    }

    // ── Final-score computation (BR-4) ───────────────────────────────
    //
    // EXTENSION POINT for US-PRF-005 (360 feedback, BR-5): today the final score is a simple two-way blend
    // of the self-score and the manager-score by the tenant-configured ratio. When 360 lands, extend this
    // single method to also weight peer/report scores (their weights subtracted from manager/self per the
    // tenant 360 config) — the call sites and stored FinalScore semantics stay the same.
    internal static decimal ComputeFinalScore(
        decimal selfScore, decimal managerScore, int selfWeightPercent, int managerWeightPercent)
    {
        var self = selfScore * selfWeightPercent / 100m;
        var manager = managerScore * managerWeightPercent / 100m;
        return Math.Round(self + manager, 2, MidpointRounding.AwayFromZero);
    }

    // ── 360 final-score incorporation (US-PRF-005 BR-6) ──────────────
    //
    // When the cycle has 360 enabled, the final score blends self + manager + peers + direct-reports by the
    // cycle's 360 per-category weights (FR-6), normalizing by the categories that actually have data. The
    // self perspective uses the submitted self-score; the manager perspective uses the weighted manager
    // score just computed; peers/reports use the average of their submitted Feedback360 ratings (null when
    // none). This delegates to the pure domain ThreeSixtyScoreCalculator so the math is one source of truth.
    internal static decimal ComputeFinalScoreWith360(
        decimal selfScore, decimal managerScore, decimal? peerAvg, decimal? reportAvg, AppraisalCycle cycle)
        => ThreeSixtyScoreCalculator.ComputeComposite(
            new ThreeSixtyScoreCalculator.CategoryAverages(selfScore, managerScore, peerAvg, reportAvg),
            new ThreeSixtyScoreCalculator.CategoryWeights(
                cycle.ThreeSixtySelfWeightPercent,
                cycle.ThreeSixtyManagerWeightPercent,
                cycle.ThreeSixtyPeerWeightPercent,
                cycle.ThreeSixtyReportWeightPercent));

    // Average rating across submitted Feedback360 rows for the Peer / DirectReport categories (null when
    // a category has no feedback). Used by the BR-6 final-score blend.
    private async Task<(decimal? PeerAvg, decimal? ReportAvg)> LoadPeerReportAveragesAsync(
        Guid employeeId, Guid cycleId, CancellationToken cancellationToken)
    {
        var ratings = await _dbContext.Feedback360s
            .AsNoTracking()
            .Where(f => f.CycleId == cycleId && f.RevieweeEmployeeId == employeeId
                && (f.Category == ReviewerCategory.Peer || f.Category == ReviewerCategory.DirectReport))
            .SelectMany(f => f.Items.Where(i => !i.IsDeleted).Select(i => new { f.Category, i.Rating }))
            .ToListAsync(cancellationToken);

        decimal? Avg(ReviewerCategory cat)
        {
            var vals = ratings.Where(r => r.Category == cat).Select(r => r.Rating).ToList();
            return vals.Count == 0 ? null
                : Math.Round((decimal)vals.Average(), 2, MidpointRounding.AwayFromZero);
        }

        return (Avg(ReviewerCategory.Peer), Avg(ReviewerCategory.DirectReport));
    }

    // ── Weighted manager score ───────────────────────────────────────
    // Weighted average of each goal's managerRating by its goal weight, on the rating scale:
    // Σ(rating*weight)/Σ(weight). Mirrors SelfAssessmentService.ComputeWeightedScore.
    internal static decimal ComputeWeightedScore(IReadOnlyList<Goal> goals, IReadOnlyList<ManagerReviewItem> items)
    {
        decimal weightedSum = 0m;
        decimal totalWeight = 0m;
        var byGoal = items.ToDictionary(i => i.GoalId);
        foreach (var goal in goals)
        {
            if (!byGoal.TryGetValue(goal.Id, out var item) || item.ManagerRating is null)
                continue;
            weightedSum += item.ManagerRating.Value * goal.Weight;
            totalWeight += goal.Weight;
        }
        return totalWeight == 0m ? 0m : Math.Round(weightedSum / totalWeight, 2, MidpointRounding.AwayFromZero);
    }

    // ── Audit (FR-7) ─────────────────────────────────────────────────

    private static readonly JsonSerializerOptions AuditJsonOptions = new() { WriteIndented = false };

    /// <summary>
    /// ISSUE-111 (US-PRF-005 FR-7): appends a queryable tenant audit row (with before/after JSON snapshots)
    /// to the shared audit_log table. Tenant + the reviewing manager's user id are stamped from context, and
    /// CreatedAt supplies the submission timestamp FR-7 requires. Mirrors
    /// <c>LeaveTypeService.AddLeaveTypeAudit</c>; persisted by the caller's SaveChanges.
    /// </summary>
    private void AddManagerReviewAudit(string action, Guid resourceId, string? before, string? after, string detail)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = action,
            Action = action,
            ResourceType = "ManagerReview",
            ResourceId = resourceId.ToString(),
            Before = before,
            After = after,
            Detail = detail,
            CreatedAt = DateTime.UtcNow,
        });
    }

    /// <summary>Serializes the audit-relevant fields of a manager review (incl. per-goal ratings) to JSON.</summary>
    private static string SnapshotReview(ManagerReview r) => JsonSerializer.Serialize(new
    {
        r.CycleId,
        r.EmployeeId,
        r.ReviewerEmployeeId,
        Status = r.Status.ToString(),
        Flag = r.Flag.ToString(),
        r.SummaryComment,
        r.WeightedManagerScore,
        r.SelfScoreAtSubmit,
        r.FinalScore,
        r.SubmittedAt,
        Items = r.Items
            .Where(i => !i.IsDeleted)
            .OrderBy(i => i.GoalId)
            .Select(i => new { i.GoalId, i.ManagerRating, i.ManagerComment })
            .ToList(),
    }, AuditJsonOptions);

    // ── Authorization (BR-2/BR-3) ────────────────────────────────────

    /// <summary>
    /// BR-2/BR-3: the caller may review <paramref name="employeeId"/> if they hold Performance.Review.All
    /// (HR override) OR they hold Performance.Review.Team AND are the employee's direct reporting manager.
    /// Mirrors GoalService.AuthorizeForEmployeeAsync. The target must exist in-tenant (global filter).
    /// </summary>
    private async Task<Result> AuthorizeForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var target = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (target is null)
            return Result.Failure("Employee not found.", 404, "employee_not_found");

        var permissions = _currentUser.Permissions;

        // HR override (BR-3).
        if (permissions.Contains(PermissionCatalog.Performance.ReviewAll))
            return Result.Success();

        if (!permissions.Contains(PermissionCatalog.Performance.ReviewTeam))
            return Result.Failure("You do not have permission to review performance.", 403, "forbidden");

        var manager = await GetCurrentEmployeeAsync(cancellationToken);
        if (manager is null)
            return Result.Failure(
                "The current user is not linked to an employee record.", 403, "no_employee_record");

        // BR-2: a manager may only review their direct reports.
        if (target.ReportsToEmployeeId != manager.Id)
            return Result.Failure(
                "You can only review your direct reports.", 403, "not_direct_report");

        return Result.Success();
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

    private Task<SelfAssessment?> LoadSelfAssessmentAsync(Guid employeeId, Guid cycleId, CancellationToken cancellationToken)
        => _dbContext.SelfAssessments
            .AsNoTracking()
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.EmployeeId == employeeId && s.CycleId == cycleId, cancellationToken);

    private async Task<ManagerReview?> LoadReviewWithItemsAsync(
        Guid employeeId, Guid cycleId, bool tracking, CancellationToken cancellationToken)
    {
        IQueryable<ManagerReview> query = _dbContext.ManagerReviews.Include(r => r.Items);
        if (!tracking)
            query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(
            r => r.EmployeeId == employeeId && r.CycleId == cycleId, cancellationToken);
    }


    /// <summary>
    /// GAP-012 / ISSUE-373: resolves an employee's job-title label with its OWN query.
    ///
    /// <para><b>Deliberately not <c>Include(e =&gt; e.JobTitle)</c>.</b> <c>Employee.JobTitleId</c> is
    /// non-nullable, so the navigation is REQUIRED, and <c>JobTitle</c> carries a global query filter
    /// (<c>!IsDeleted &amp;&amp; tenant</c>). EF emits an INNER JOIN for a required navigation, so an employee
    /// whose job title is soft-deleted would VANISH from the query entirely — callers would see "no such
    /// employee" rather than a missing label. That is a disappearing-employee bug, strictly worse than the
    /// blank field it was meant to fix; it surfaced as 33 failing tests.</para>
    ///
    /// <para>A separate query degrades correctly: a filtered-out title yields an empty label and the employee
    /// still loads. The tenant filter still applies, so this cannot read another tenant's title.</para>
    /// </summary>
    private async Task<string> ResolveJobTitleAsync(Guid jobTitleId, CancellationToken ct)
        => await _dbContext.JobTitles
            .AsNoTracking()
            .Where(j => j.Id == jobTitleId)
            .Select(j => j.TitleName)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

    private async Task<Result<ManagerReviewDto>> BuildDtoResultAsync(
        AppraisalCycle cycle, Employee employee, Guid reviewId, CancellationToken cancellationToken)
    {
        var goals = await LoadGoalsAsync(employee.Id, cycle.Id, cancellationToken);
        var selfAssessment = await LoadSelfAssessmentAsync(employee.Id, cycle.Id, cancellationToken);
        var review = await _dbContext.ManagerReviews
            .AsNoTracking()
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);
        var jobTitle = await ResolveJobTitleAsync(employee.JobTitleId, cancellationToken);
        return Result<ManagerReviewDto>.Success(BuildDto(cycle, employee, jobTitle, review, goals, selfAssessment));
    }

    private static ManagerReviewDto BuildDto(
        AppraisalCycle cycle, Employee employee, string jobTitle, ManagerReview? review,
        IReadOnlyList<Goal> goals, SelfAssessment? selfAssessment)
    {
        var managerByGoal = review?.Items.Where(i => !i.IsDeleted).ToDictionary(i => i.GoalId)
            ?? new Dictionary<Guid, ManagerReviewItem>();
        var selfByGoal = selfAssessment?.Items.Where(i => !i.IsDeleted).ToDictionary(i => i.GoalId)
            ?? new Dictionary<Guid, SelfAssessmentItem>();

        var locked = review?.Status == ManagerReviewStatus.Submitted;
        var open = cycle.IsManagerReviewOpen(DateTime.UtcNow);
        var selfSubmitted = selfAssessment is { Status: SelfAssessmentStatus.Submitted };

        var itemDtos = goals.Select(g =>
        {
            managerByGoal.TryGetValue(g.Id, out var mgr);
            selfByGoal.TryGetValue(g.Id, out var self);
            return new ManagerReviewItemDto
            {
                GoalId = g.Id,
                GoalTitle = g.Title,
                GoalDescription = g.Description,
                GoalWeight = g.Weight,
                GoalTargetValue = g.TargetValue,
                GoalMeasurementUnit = g.MeasurementUnit,
                GoalDueDate = g.DueDate,
                SelfRating = self?.SelfRating,
                SelfAchievementPercentage = self?.AchievementPercentage,
                SelfComment = self?.Comment,
                ManagerRating = mgr?.ManagerRating,
                ManagerComment = mgr?.ManagerComment,
            };
        }).ToList();

        var flag = review?.Flag ?? ReviewFlag.None;

        return new ManagerReviewDto
        {
            Id = review?.Id ?? Guid.Empty,
            CycleId = cycle.Id,
            // GAP-012 / ISSUE-373: both read by the UI, neither previously sent.
            CycleName = cycle.Name,
            JobTitle = jobTitle,
            EmployeeId = employee.Id,
            EmployeeName = $"{employee.FirstName} {employee.LastName}".Trim(),
            EmployeeNo = employee.EmployeeNo,
            Status = review?.Status ?? ManagerReviewStatus.Draft,
            StatusName = (review?.Status ?? ManagerReviewStatus.Draft).ToString(),
            WeightedManagerScore = review?.WeightedManagerScore,
            WeightedSelfScore = selfSubmitted ? selfAssessment!.WeightedSelfScore : null,
            FinalScore = review?.FinalScore,
            SummaryComment = review?.SummaryComment,
            Flag = flag,
            FlagName = flag.ToString(),
            SubmittedAt = review?.SubmittedAt,
            RatingScaleMax = cycle.RatingScaleMax,
            SelfWeightPercent = cycle.SelfWeightPercent,
            ManagerWeightPercent = cycle.ManagerWeightPercent,
            IsReviewWindowOpen = open,
            IsLocked = locked,
            SelfAssessmentSubmitted = selfSubmitted,
            Items = itemDtos,
        };
    }

    // Team-dashboard status (AC-4): derive from the self-assessment + manager-review state.
    private static string AggregateStatus(SelfAssessment? self, ManagerReview? review)
    {
        if (review is { Status: ManagerReviewStatus.Submitted }) return "Completed";
        if (self is not { Status: SelfAssessmentStatus.Submitted }) return "PendingSelfAssessment";
        if (review is { Status: ManagerReviewStatus.Draft }) return "ManagerReviewPending";
        return "SelfAssessmentSubmitted";
    }
}
