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
    // Comment length ceilings — must mirror the EF column caps (Feedback360Configuration / Feedback360ItemConfiguration)
    // so an over-length comment is rejected as a clean 422 before it ever hits Npgsql (ISSUE-256).
    private const int MaxOverallCommentLength = 5000;
    private const int MaxItemCommentLength = 2000;

    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IPerformanceNotificationService _notifications;
    private readonly ILogger<Feedback360Service> _logger;

    public Feedback360Service(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IPerformanceNotificationService notifications,
        ILogger<Feedback360Service> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _notifications = notifications;
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

        // Comment length ceilings (ISSUE-256): enforced here so BOTH submit entry points (the direct
        // SubmitFeedback360Command and the by-assignment SubmitFeedbackByAssignmentCommand) are covered —
        // otherwise an over-length comment reaches the DB column cap and Npgsql returns 22001 → HTTP 500.
        // Caps mirror Feedback360Configuration (OverallComment 5000) / Feedback360ItemConfiguration (Comment 2000).
        if (input.OverallComment is { Length: > MaxOverallCommentLength })
            return Result<Feedback360ResultEntryDto>.Failure(
                $"Overall comment cannot exceed {MaxOverallCommentLength} characters.", 422, "comment_too_long");

        if (input.Items.Any(i => i.Comment is { Length: > MaxItemCommentLength }))
            return Result<Feedback360ResultEntryDto>.Failure(
                $"Comment cannot exceed {MaxItemCommentLength} characters.", 422, "comment_too_long");

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

    // ── Feedback form for one assignment (BUG-244 #2, FR-4/AC-2) ─────

    public async Task<Result<FeedbackFormDto>> GetFeedbackFormAsync(
        Guid assignmentId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<FeedbackFormDto>.Failure("Tenant context is not resolved.", 400);

        var caller = await GetCurrentEmployeeAsync(cancellationToken);
        if (caller is null)
            return Result<FeedbackFormDto>.Failure(
                "The current user is not linked to an employee record.", 403, "no_employee_record");

        var assignment = await _dbContext.ReviewerAssignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);
        if (assignment is null)
            return Result<FeedbackFormDto>.Failure("Reviewer assignment not found.", 404, "assignment_not_found");

        // RLS (NFR-2/NFR-3): a reviewer may load ONLY their own assignment's form.
        if (assignment.ReviewerEmployeeId != caller.Id)
            return Result<FeedbackFormDto>.Failure(
                "You are not assigned to review this employee for this cycle.", 403, "not_assigned");

        var cycle = await _dbContext.AppraisalCycles.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == assignment.CycleId, cancellationToken);
        if (cycle is null)
            return Result<FeedbackFormDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        var reviewee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == assignment.RevieweeEmployeeId, cancellationToken);
        if (reviewee is null)
            return Result<FeedbackFormDto>.Failure("Employee not found.", 404, "employee_not_found");

        var revieweeJobTitle = await _dbContext.JobTitles.AsNoTracking()
            .Where(j => j.Id == reviewee.JobTitleId)
            .Select(j => j.TitleName)
            .FirstOrDefaultAsync(cancellationToken);

        // Questions = the reviewee's goals for this cycle (same source Feedback360Service uses for submit labels).
        var goals = await _dbContext.Goals.AsNoTracking()
            .Where(g => g.EmployeeId == assignment.RevieweeEmployeeId && g.CycleId == assignment.CycleId)
            .OrderBy(g => g.Title)
            .Select(g => new { g.Id, g.Title, g.Description })
            .ToListAsync(cancellationToken);

        // Hydrate the reviewer's existing answers when they have already submitted (BR-3 — one per reviewer).
        var feedback = await _dbContext.Feedback360s.AsNoTracking()
            .Include(f => f.Items)
            .FirstOrDefaultAsync(f => f.CycleId == assignment.CycleId
                && f.RevieweeEmployeeId == assignment.RevieweeEmployeeId
                && f.ReviewerEmployeeId == caller.Id, cancellationToken);

        var submitted = feedback is not null || assignment.Status == ReviewerAssignmentStatus.Completed;
        var itemsByGoal = feedback?.Items
            .Where(i => !i.IsDeleted && i.GoalId.HasValue)
            .ToDictionary(i => i.GoalId!.Value)
            ?? new Dictionary<Guid, Feedback360Item>();

        var questions = goals.Select(g =>
        {
            itemsByGoal.TryGetValue(g.Id, out var item);
            return new FeedbackFormQuestionDto
            {
                QuestionId = g.Id,
                Kind = "Goal",
                Title = g.Title,
                Description = g.Description,
                Rating = item?.Rating,
                Comment = item?.Comment ?? string.Empty,
            };
        }).ToList();

        return Result<FeedbackFormDto>.Success(new FeedbackFormDto
        {
            AssignmentId = assignment.Id,
            CycleId = cycle.Id,
            CycleName = cycle.Name,
            RevieweeId = reviewee.Id,
            RevieweeName = FullName(reviewee),
            RevieweeJobTitle = revieweeJobTitle,
            Category = assignment.Category,
            RatingScaleMax = cycle.RatingScaleMax,
            Submitted = submitted,
            SubmittedOn = feedback?.SubmittedAt ?? assignment.CompletedAt,
            Anonymous = cycle.IsAnonymousFeedback,
            Questions = questions,
        });
    }

    // ── Submit-by-assignment (BUG-244, reviewer form submit) ─────────

    public async Task<Result<FeedbackFormDto>> SubmitFeedbackByAssignmentAsync(
        Guid assignmentId, IReadOnlyList<FeedbackAnswerInput> answers, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<FeedbackFormDto>.Failure("Tenant context is not resolved.", 400);

        var caller = await GetCurrentEmployeeAsync(cancellationToken);
        if (caller is null)
            return Result<FeedbackFormDto>.Failure(
                "The current user is not linked to an employee record.", 403, "no_employee_record");

        var assignment = await _dbContext.ReviewerAssignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);
        if (assignment is null)
            return Result<FeedbackFormDto>.Failure("Reviewer assignment not found.", 404, "assignment_not_found");

        // RLS (NFR-2/NFR-3): a reviewer may submit ONLY their own assignment — same gate as the form load (#2).
        if (assignment.ReviewerEmployeeId != caller.Id)
            return Result<FeedbackFormDto>.Failure(
                "You are not assigned to review this employee for this cycle.", 403, "not_assigned");

        // Map each form answer back to a goal rating — the exact inverse of the #2 form projection
        // (questionId == goal id). Null ratings map to 0 so the reused validator rejects them (rating_out_of_range).
        var items = (answers ?? [])
            .Select(a => new Feedback360ItemInput
            {
                GoalId = a.QuestionId,
                CompetencyKey = null,
                Rating = a.Rating ?? 0,
                Comment = a.Comment,
            })
            .ToList();

        // Reuse the full submit path (Is360Enabled, Pending assignment, BR-3 duplicate, rating-range) keyed by
        // the assignment's cycle + reviewee; the caller is re-resolved to the same reviewer inside.
        var submit = await SubmitFeedbackAsync(
            new SubmitFeedback360Input(assignment.CycleId, assignment.RevieweeEmployeeId, null, items),
            cancellationToken);
        if (submit.IsFailure)
            return Result<FeedbackFormDto>.Failure(submit.Error!, submit.StatusCode ?? 400, submit.ErrorCode);

        // Return the now-locked form (submitted=true, ratings/comments hydrated) — the FE consumes IFeedbackForm.
        return await GetFeedbackFormAsync(assignmentId, cancellationToken);
    }

    // ── Aggregated results (AC-4/FR-6) ───────────────────────────────

    public Task<Result<Feedback360ResultsDto>> GetResultsAsync(
        Guid revieweeEmployeeId, Guid cycleId, CancellationToken cancellationToken = default)
        => BuildResultsAsync(revieweeEmployeeId, cycleId, forRevieweeSelf: false, cancellationToken);

    public Task<Result<Feedback360ResultsDto>> GetReportDataAsync(
        Guid revieweeEmployeeId, Guid cycleId, CancellationToken cancellationToken = default)
        => BuildResultsAsync(revieweeEmployeeId, cycleId, forRevieweeSelf: false, cancellationToken);

    // ── Release results to the reviewee (BR-4/FR-3) ──────────────────

    public async Task<Result<Feedback360ReleaseDto>> ReleaseResultsAsync(
        Guid cycleId, Guid revieweeEmployeeId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<Feedback360ReleaseDto>.Failure("Tenant context is not resolved.", 400);

        // HR (Review.All) unrestricted, or the reviewee's own reporting manager (Review.Team) when the tenant
        // allows it — the SAME shared four-outcome gate the reviewer-config surface uses (not a second copy).
        var authz = await Performance360Authorization.AuthorizeReviewerManagementAsync(
            _dbContext, _tenantContext, _currentUser, revieweeEmployeeId, cancellationToken);
        if (authz.IsFailure)
            return Result<Feedback360ReleaseDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        var cycle = await _dbContext.AppraisalCycles.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (cycle is null)
            return Result<Feedback360ReleaseDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        if (!cycle.Is360Enabled)
            return Result<Feedback360ReleaseDto>.Failure(
                "360-degree feedback is not enabled for this cycle.", 409, "not_360_enabled");

        var reviewee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == revieweeEmployeeId, cancellationToken);
        if (reviewee is null)
            return Result<Feedback360ReleaseDto>.Failure("Employee not found.", 404, "employee_not_found");

        // Idempotency (BR-4): a re-release resolves to the existing row — 200, no second row. The unique index
        // is the durable guard; a retried click must not error.
        var existing = await _dbContext.Feedback360Releases.AsNoTracking()
            .FirstOrDefaultAsync(r => r.CycleId == cycleId && r.RevieweeEmployeeId == revieweeEmployeeId, cancellationToken);
        if (existing is not null)
            return Result<Feedback360ReleaseDto>.Success(MapRelease(existing));

        // BR-4 HARD gate: the minimum number of PEER responses must be met before releasing to the reviewee.
        // Same peer-count computation as the pre-release warning path (CountPeerResponses) — one rule, two callers.
        var feedbacks = await _dbContext.Feedback360s.AsNoTracking()
            .Where(f => f.CycleId == cycleId && f.RevieweeEmployeeId == revieweeEmployeeId)
            .ToListAsync(cancellationToken);
        var peerResponses = CountPeerResponses(feedbacks);
        if (peerResponses < cycle.Min360PeerReviewers)
            return Result<Feedback360ReleaseDto>.Failure(
                $"At least {cycle.Min360PeerReviewers} peer reviewer(s) must submit feedback before results can be "
                + $"released. Currently {peerResponses}.", 422, "min_peer_threshold_not_met");

        var releasedBy = await GetCurrentEmployeeAsync(cancellationToken);
        var release = new Feedback360Release
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            CycleId = cycleId,
            RevieweeEmployeeId = revieweeEmployeeId,
            ReleasedAt = DateTime.UtcNow,
            ReleasedByEmployeeId = releasedBy?.Id ?? Guid.Empty,
            IsDeleted = false,
        };
        _dbContext.Feedback360Releases.Add(release);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // CONCURRENT double-release: the check above is read-then-insert, so two simultaneous callers both
            // see no row and both insert. The filtered unique index (TenantId, CycleId, RevieweeEmployeeId) is
            // the real guard and rejects the loser. Idempotency must hold for the CONCURRENT case too, not just
            // the sequential one — a double-clicked Release button is exactly how this happens — so resolve to
            // the winner's row and return 200 rather than surfacing a 500.
            _dbContext.Entry(release).State = EntityState.Detached;
            var winner = await _dbContext.Feedback360Releases.AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.CycleId == cycleId && r.RevieweeEmployeeId == revieweeEmployeeId, cancellationToken);
            if (winner is null)
                throw; // not the uniqueness collision — a genuine write failure, do not swallow it
            return Result<Feedback360ReleaseDto>.Success(MapRelease(winner));
        }

        _logger.LogInformation(
            "360 results released. CycleId={CycleId}, RevieweeId={RevieweeId}, ReleasedById={ReleasedById}, " +
            "PeerResponses={PeerResponses}, TenantId={TenantId}",
            cycleId, revieweeEmployeeId, release.ReleasedByEmployeeId, peerResponses, _tenantContext.TenantId);

        // Notify the reviewee (and their reporting manager) that results are available — reuse the existing
        // performance notification seam (fire-and-forget; the service never throws into this path).
        await _notifications.NotifyCycleEventAsync(
            "results-released", cycleId, revieweeEmployeeId,
            "Your 360-degree feedback results are now available.", cancellationToken);
        if (reviewee.ReportsToEmployeeId is { } managerId)
            await _notifications.NotifyCycleEventAsync(
                "results-released", cycleId, managerId,
                "360-degree feedback results for your direct report are now available.", cancellationToken);

        return Result<Feedback360ReleaseDto>.Success(MapRelease(release));
    }

    // ── Reviewee-facing read of own results (FR-3/FR-5) ──────────────

    public async Task<Result<Feedback360ResultsDto>> GetMyResultsAsync(
        Guid cycleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<Feedback360ResultsDto>.Failure("Tenant context is not resolved.", 400);

        var caller = await GetCurrentEmployeeAsync(cancellationToken);
        if (caller is null)
            return Result<Feedback360ResultsDto>.Failure(
                "The current user is not linked to an employee record.", 403, "no_employee_record");

        // FR-3: results are only visible to the reviewee once RELEASED. 404 (not 403) so we never disclose
        // whether unreleased results exist.
        var released = await _dbContext.Feedback360Releases.AsNoTracking()
            .AnyAsync(r => r.CycleId == cycleId && r.RevieweeEmployeeId == caller.Id, cancellationToken);
        if (!released)
            return Result<Feedback360ResultsDto>.Failure(
                "Your 360-degree feedback results have not been released yet.", 404, "not_released");

        // Reuse the SAME aggregation as HR (no parallel projection); forRevieweeSelf strips every reviewer
        // identity (FR-5, unconditional) and marks the PDF export unavailable.
        return await BuildResultsAsync(caller.Id, cycleId, forRevieweeSelf: true, cancellationToken);
    }

    public async Task<Result<PerformanceExportFile>> ExportReportAsync(
        Guid revieweeEmployeeId, Guid cycleId, string? format, CancellationToken cancellationToken = default)
    {
        var normalized = (format ?? "pdf").Trim().ToLowerInvariant();
        if (normalized != "pdf")
            return Result<PerformanceExportFile>.Failure("Export format must be pdf.", 400, "invalid_format");

        // Reuse the authorized, tenant-filtered read path (HR-only + anonymity enforced in the projection).
        var data = await BuildResultsAsync(revieweeEmployeeId, cycleId, forRevieweeSelf: false, cancellationToken);
        if (data.IsFailure)
            return Result<PerformanceExportFile>.Failure(data.Error!, data.StatusCode ?? 400, data.ErrorCode);

        var bytes = Performance.PerformancePdfRenderer.RenderFeedback360(data.Value!, _tenantContext.PrimaryColor);
        var fileName = $"360-report-{cycleId:N}-{revieweeEmployeeId:N}.pdf";
        return Result<PerformanceExportFile>.Success(new PerformanceExportFile(bytes, fileName, "application/pdf"));
    }

    /// <param name="forRevieweeSelf">
    /// When true the caller is the reviewee reading their OWN released results: the HR permission check is
    /// skipped (the caller self-scoped + release-gated before getting here), every reviewer identity is stripped
    /// unconditionally (FR-5), and the PDF export is marked unavailable. When false this is the HR/manager path
    /// (Review.All required, existing anonymity rules, export available).
    /// </param>
    private async Task<Result<Feedback360ResultsDto>> BuildResultsAsync(
        Guid revieweeEmployeeId, Guid cycleId, bool forRevieweeSelf, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return Result<Feedback360ResultsDto>.Failure("Tenant context is not resolved.", 400);

        // HR/manager only (Performance.Review.All for results, NFR-3/FR-5) — NOT enforced on the reviewee's own
        // self path, which is already release-gated and self-scoped by GetMyResultsAsync.
        if (!forRevieweeSelf && !_currentUser.Permissions.Contains(PermissionCatalog.Performance.ReviewAll))
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
        // would not be allowed to surface). On the reviewee's own path we surface NO reviewer identity at all
        // (FR-5, unconditional), so we do not even load the names.
        var reviewerNames = forRevieweeSelf
            ? new Dictionary<Guid, string>()
            : await LoadNameLookupAsync(
                feedbacks.Where(f => !f.IsAnonymous).Select(f => f.ReviewerEmployeeId).ToHashSet(), cancellationToken);

        // GAP-012 / ISSUE-373: the reviewee's job title, resolved with its OWN query.
        //
        // NOT Include(e => e.JobTitle), which is what I tried first and which is actively dangerous here:
        // Employee.JobTitleId is non-nullable so the navigation is REQUIRED, and JobTitle carries a global
        // query filter (!IsDeleted && tenant). EF emits an INNER JOIN for a required navigation, so an employee
        // whose job title is soft-deleted would VANISH from the query altogether — the caller would see "no
        // such reviewee" instead of a missing label. It broke 33 tests, and in production it would have been a
        // disappearing-employee bug rather than a blank field.
        //
        // A separate query degrades the right way: a filtered-out title yields an empty label and the reviewee
        // still loads. The tenant filter still applies, so this cannot read another tenant's title.
        var jobTitle = await _dbContext.JobTitles
            .AsNoTracking()
            .Where(j => j.Id == reviewee.JobTitleId)
            .Select(j => j.TitleName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        // Release state (BR-4/FR-3): both read paths report whether the reviewee's results have been released.
        var release = await _dbContext.Feedback360Releases.AsNoTracking()
            .FirstOrDefaultAsync(r => r.CycleId == cycleId && r.RevieweeEmployeeId == revieweeEmployeeId, cancellationToken);

        var dto = Aggregate(
            cycle, reviewee, jobTitle, feedbacks, assignments, labels, reviewerNames,
            released: release is not null, releasedAt: release?.ReleasedAt,
            exportAvailable: !forRevieweeSelf, suppressReviewerIdentity: forRevieweeSelf);
        return Result<Feedback360ResultsDto>.Success(dto);
    }

    // ── Aggregation (pure given the loaded rows) ─────────────────────

    private static Feedback360ResultsDto Aggregate(
        AppraisalCycle cycle, Employee reviewee, string jobTitle,
        IReadOnlyList<Feedback360> feedbacks, IReadOnlyList<ReviewerAssignment> assignments,
        IReadOnlyDictionary<string, string> labels, IReadOnlyDictionary<Guid, string> reviewerNames,
        bool released, DateTime? releasedAt, bool exportAvailable, bool suppressReviewerIdentity)
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

        // Completion tracker (AC-3/AC-4) — shared per-category tally (BUG-244: also feeds the standalone tracker).
        var completion = ThreeSixtyCompletion.ByCategory(assignments).Select(c => new CategoryCompletionDto
        {
            Category = c.Category,
            CategoryName = c.Category.ToString(),
            Assigned = c.Assigned,
            Completed = c.Completed,
        }).ToList();

        // Individual entries — reviewer identity OMITTED when the feedback is anonymous (NFR-3/FR-5) OR when this
        // is the reviewee's own view (suppressReviewerIdentity, FR-5 unconditional).
        var entries = feedbacks
            .OrderBy(f => f.Category).ThenBy(f => f.SubmittedAt)
            .Select(f =>
            {
                string? name = null;
                if (!suppressReviewerIdentity && !f.IsAnonymous && reviewerNames.TryGetValue(f.ReviewerEmployeeId, out var n))
                    name = n;
                return ProjectEntry(f, name, labels, suppressReviewerIdentity);
            })
            .ToList();

        // BR-4: minimum-peer release gate — same peer-count rule the release gate uses (CountPeerResponses).
        var peerResponses = CountPeerResponses(feedbacks);
        var thresholdMet = peerResponses >= cycle.Min360PeerReviewers;
        var warning = thresholdMet
            ? null
            : $"Only {peerResponses} of the required {cycle.Min360PeerReviewers} peer reviewer(s) have submitted "
              + "feedback. Releasing the results now may not be representative.";

        return new Feedback360ResultsDto
        {
            CycleId = cycle.Id,
            // GAP-012 / ISSUE-373: both read by the UI, neither previously sent.
            CycleName = cycle.Name,
            JobTitle = jobTitle,
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
            Released = released,
            ReleasedAt = releasedAt,
            ExportAvailable = exportAvailable,
        };
    }

    /// <summary>
    /// BR-4 peer-response count for a reviewee+cycle: the number of submitted PEER feedbacks. The ONE definition
    /// of the threshold input, shared by the pre-release warning (<see cref="Aggregate"/>) and the hard release
    /// gate (<see cref="ReleaseResultsAsync"/>) so the two can never drift.
    /// </summary>
    private static int CountPeerResponses(IReadOnlyList<Feedback360> feedbacks)
        => feedbacks.Count(f => f.Category == ReviewerCategory.Peer);

    // ── Projection (anonymity rule lives here, NFR-3/FR-5) ───────────

    private static Feedback360ResultEntryDto ProjectEntry(
        Feedback360 f, string? reviewerName, IReadOnlyDictionary<string, string> labels,
        bool suppressReviewerIdentity = false)
        => new()
        {
            FeedbackId = f.Id,
            Category = f.Category,
            CategoryName = f.Category.ToString(),
            IsAnonymous = f.IsAnonymous,
            // NFR-3/FR-5: when anonymous — or when this is the reviewee's own view — neither the id nor the name
            // is ever populated. suppressReviewerIdentity is the reviewee-facing hard override (FR-5 unconditional).
            ReviewerEmployeeId = (f.IsAnonymous || suppressReviewerIdentity) ? null : f.ReviewerEmployeeId,
            ReviewerName = (f.IsAnonymous || suppressReviewerIdentity) ? null : reviewerName,
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

    private static Feedback360ReleaseDto MapRelease(Feedback360Release r) => new()
    {
        CycleId = r.CycleId,
        RevieweeEmployeeId = r.RevieweeEmployeeId,
        ReleasedAt = r.ReleasedAt,
        ReleasedByEmployeeId = r.ReleasedByEmployeeId,
    };
}
