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
/// Performance-review meeting-notes &amp; digital sign-off service (US-PRF-006). Every read/write is
/// tenant-scoped via ITenantContext + the EF global query filter (NFR-2). Enforces: notes only after the
/// manager review is submitted (BR-1); HTML sanitization of rich-text on write (FR-1, §10); the manager →
/// employee sign-off order (FR-3); IMMUTABLE, append-only sign-off records (NFR-3/BR-5 — there is NO update or
/// delete path for a recorded ReviewSignoff, and a SignedOff review is locked against edits); mandatory
/// dispute comments + HR resolution (FR-4/FR-5/BR-4); and the FR-7 audit (client IP + timestamp on each
/// sign-off, plus the AuditInterceptor stamping CreatedBy/CreatedAt). Authorization mirrors
/// <see cref="ManagerReviewService"/> (Review.Team direct-manager / Review.All HR; the reviewed employee for
/// the employee-side actions).
/// </summary>
public sealed class ReviewSignoffService : IReviewSignoffService
{
    /// <summary>
    /// Built-in default meeting-notes template (FR-1/FR-2) when the cycle has no tenant override. Sanitized
    /// HTML — kept minimal (a default + per-cycle override field, NOT a template-editor subsystem).
    /// </summary>
    internal const string DefaultTemplate =
        "<h3>Key strengths</h3><p></p><h3>Areas for improvement</h3><p></p>" +
        "<h3>Agreed development actions</h3><p></p><h3>Overall discussion summary</h3><p></p>";

    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IHtmlSanitizer _sanitizer;
    private readonly IPerformanceNotificationService _notifications;
    private readonly ILogger<ReviewSignoffService> _logger;

    public ReviewSignoffService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IHtmlSanitizer sanitizer,
        IPerformanceNotificationService notifications,
        ILogger<ReviewSignoffService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _sanitizer = sanitizer;
        _notifications = notifications;
        _logger = logger;
    }

    // ── Get notes (AC-1) ──────────────────────────────────────────────

    public async Task<Result<ReviewMeetingNotesDto>> GetNotesAsync(
        Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ReviewMeetingNotesDto>.Failure("Tenant context is not resolved.", 400);

        var authz = await AuthorizeManagerOrHrAsync(employeeId, cancellationToken);
        if (authz.IsFailure)
            return Result<ReviewMeetingNotesDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        var (loadResult, ctx) = await LoadContextAsync(employeeId, cycleId, tracking: false, cancellationToken);
        if (loadResult is not null)
            return loadResult;

        return Result<ReviewMeetingNotesDto>.Success(BuildNotesDto(ctx!));
    }

    // ── Save notes (AC-1) ─────────────────────────────────────────────

    public Task<Result<ReviewMeetingNotesDto>> SaveNotesAsync(
        SaveMeetingNotesInput input, CancellationToken cancellationToken = default)
        => UpsertNotesAsync(input, requestSignOff: false, clientIp: null, cancellationToken);

    public Task<Result<ReviewMeetingNotesDto>> RequestSignOffAsync(
        SaveMeetingNotesInput input, string? clientIpAddress, CancellationToken cancellationToken = default)
        => UpsertNotesAsync(input, requestSignOff: true, clientIp: clientIpAddress, cancellationToken);

    private async Task<Result<ReviewMeetingNotesDto>> UpsertNotesAsync(
        SaveMeetingNotesInput input, bool requestSignOff, string? clientIp, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return Result<ReviewMeetingNotesDto>.Failure("Tenant context is not resolved.", 400);

        var authz = await AuthorizeManagerOrHrAsync(input.EmployeeId, cancellationToken);
        if (authz.IsFailure)
            return Result<ReviewMeetingNotesDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        var (loadResult, ctx) = await LoadContextAsync(input.EmployeeId, input.CycleId, tracking: true, cancellationToken);
        if (loadResult is not null)
            return loadResult;

        var review = ctx!.Review!;

        // BR-1: notes can only be added after the manager review is submitted.
        if (review.Status != ManagerReviewStatus.Submitted)
            return Result<ReviewMeetingNotesDto>.Failure(
                "Meeting notes can only be added after the manager review has been submitted.",
                409, "review_not_submitted");

        // BR-5: a locked (signed-off) review cannot be edited.
        if (review.IsLocked || review.SignoffStatus == ReviewSignoffStatus.SignedOff)
            return Result<ReviewMeetingNotesDto>.Failure(
                "This review is locked after sign-off and can no longer be edited.", 409, "review_locked");

        // BR-4: while disputed, only HR's resolve flow touches it — not a plain notes save.
        if (review.SignoffStatus == ReviewSignoffStatus.Disputed)
            return Result<ReviewMeetingNotesDto>.Failure(
                "This review is disputed; HR must resolve the dispute before notes can be changed.",
                409, "review_disputed");

        var notes = review.MeetingNotes;
        if (notes is null)
        {
            notes = new ReviewMeetingNotes
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                ManagerReviewId = review.Id,
                IsDeleted = false,
            };
            review.MeetingNotes = notes;
            _dbContext.ReviewMeetingNotes.Add(notes);
        }

        // FR-1, §10: sanitize ALL rich-text on write to prevent stored XSS.
        notes.Body = _sanitizer.Sanitize(input.Body);
        notes.Strengths = _sanitizer.Sanitize(input.Strengths);
        notes.DevelopmentAreas = _sanitizer.Sanitize(input.DevelopmentAreas);
        notes.Summary = _sanitizer.Sanitize(input.Summary);

        // Replace agreed actions (full-replace of the small child list).
        foreach (var existing in notes.Actions.ToList())
        {
            notes.Actions.Remove(existing);
            _dbContext.ReviewMeetingNotesActions.Remove(existing);
        }
        var order = 0;
        foreach (var a in input.Actions)
        {
            if (string.IsNullOrWhiteSpace(a.Description))
                continue;
            var action = new ReviewMeetingNotesAction
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                ReviewMeetingNotesId = notes.Id,
                Description = a.Description.Trim(),
                Deadline = a.Deadline,
                SortOrder = order++,
                IsDeleted = false,
            };
            notes.Actions.Add(action);
            _dbContext.ReviewMeetingNotesActions.Add(action);
        }

        if (requestSignOff)
        {
            // AC-2: request employee sign-off.
            review.SignoffStatus = ReviewSignoffStatus.PendingEmployeeSignOff;
            review.SignoffRequestedAt = DateTime.UtcNow;

            var actor = await GetCurrentEmployeeAsync(cancellationToken);
            AppendSignoff(review, SignoffParty.Manager, SignoffAction.RequestedSignOff,
                SignerDisplayName(actor), actor?.Id, clientIp, comments: null);
        }
        else if (review.SignoffStatus == ReviewSignoffStatus.NotStarted)
        {
            review.SignoffStatus = ReviewSignoffStatus.NotesAdded;
        }

        var saveFailure = await SaveAsync(cancellationToken);
        if (saveFailure is not null)
            return saveFailure;

        _logger.LogInformation(
            "Review meeting notes {Action}. ReviewId={ReviewId}, EmployeeId={EmployeeId}, CycleId={CycleId}, " +
            "SignoffStatus={Status}, TenantId={TenantId}, By={User}",
            requestSignOff ? "saved + sign-off requested" : "saved", review.Id, input.EmployeeId, input.CycleId,
            review.SignoffStatus, _tenantContext.TenantId, _currentUser.Email);

        if (requestSignOff)
            await _notifications.NotifyReviewSignOffRequestedAsync(
                review.Id, input.EmployeeId, input.CycleId, cancellationToken);

        return await ReloadNotesDtoAsync(input.EmployeeId, input.CycleId, cancellationToken);
    }

    // ── Employee acknowledge & sign (AC-3) ────────────────────────────

    public async Task<Result<ReviewMeetingNotesDto>> AcknowledgeAsync(
        SignoffActionInput input, CancellationToken cancellationToken = default)
        => await EmployeeActionAsync(input, dispute: false, cancellationToken);

    public async Task<Result<ReviewMeetingNotesDto>> DisputeAsync(
        SignoffActionInput input, CancellationToken cancellationToken = default)
        => await EmployeeActionAsync(input, dispute: true, cancellationToken);

    private async Task<Result<ReviewMeetingNotesDto>> EmployeeActionAsync(
        SignoffActionInput input, bool dispute, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return Result<ReviewMeetingNotesDto>.Failure("Tenant context is not resolved.", 400);

        // The reviewed employee themselves must perform this (FR-3/FR-4).
        var actor = await GetCurrentEmployeeAsync(cancellationToken);
        if (actor is null)
            return Result<ReviewMeetingNotesDto>.Failure(
                "The current user is not linked to an employee record.", 403, "no_employee_record");
        if (actor.Id != input.EmployeeId)
            return Result<ReviewMeetingNotesDto>.Failure(
                "Only the reviewed employee can sign off or dispute this review.", 403, "not_reviewed_employee");

        if (dispute && string.IsNullOrWhiteSpace(input.Comments))
            return Result<ReviewMeetingNotesDto>.Failure(
                "Dispute comments are required.", 422, "dispute_comments_required");

        var (loadResult, ctx) = await LoadContextAsync(input.EmployeeId, input.CycleId, tracking: true, cancellationToken);
        if (loadResult is not null)
            return loadResult;

        var review = ctx!.Review!;

        if (review.SignoffStatus != ReviewSignoffStatus.PendingEmployeeSignOff)
            return Result<ReviewMeetingNotesDto>.Failure(
                "This review is not awaiting your sign-off.", 409, "not_pending_signoff");

        // BR-2 read-before-sign: the employee must have opened the meeting notes before Acknowledge & Sign.
        // (Disputing is exempt — a dispute is itself a considered rejection and has its own required comments.)
        if (!dispute && review.NotesOpenedAt is null)
            return Result<ReviewMeetingNotesDto>.Failure(
                "You must open and read the review notes before signing off.", 409, "notes_not_read");

        var signerName = SignerDisplayName(actor);

        if (dispute)
        {
            review.SignoffStatus = ReviewSignoffStatus.Disputed;
            AppendSignoff(review, SignoffParty.Employee, SignoffAction.Disputed,
                signerName, actor.Id, input.ClientIpAddress, input.Comments!.Trim());
        }
        else
        {
            review.SignoffStatus = ReviewSignoffStatus.SignedOff;
            review.SignoffCompletedAt = DateTime.UtcNow;
            review.IsLocked = true; // BR-5: lock the review.
            AppendSignoff(review, SignoffParty.Employee, SignoffAction.Acknowledged,
                signerName, actor.Id, input.ClientIpAddress, comments: null);
        }

        var saveFailure = await SaveAsync(cancellationToken);
        if (saveFailure is not null)
            return saveFailure;

        _logger.LogInformation(
            "Review {Action} by employee. ReviewId={ReviewId}, EmployeeId={EmployeeId}, CycleId={CycleId}, " +
            "Status={Status}, IP={Ip}, TenantId={TenantId}",
            dispute ? "disputed" : "acknowledged & signed", review.Id, input.EmployeeId, input.CycleId,
            review.SignoffStatus, input.ClientIpAddress, _tenantContext.TenantId);

        if (dispute)
            await _notifications.NotifyReviewDisputedAsync(review.Id, input.EmployeeId, input.CycleId, cancellationToken);

        return await ReloadNotesDtoAsync(input.EmployeeId, input.CycleId, cancellationToken);
    }

    // ── HR resolve dispute (BR-4) ─────────────────────────────────────

    public async Task<Result<ReviewMeetingNotesDto>> ResolveDisputeAsync(
        ResolveDisputeInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ReviewMeetingNotesDto>.Failure("Tenant context is not resolved.", 400);

        // BR-4: only HR (Review.All) resolves disputes.
        if (!_currentUser.Permissions.Contains(PermissionCatalog.Performance.ReviewAll))
            return Result<ReviewMeetingNotesDto>.Failure(
                "Only HR can resolve a disputed review.", 403, "resolve_forbidden");

        var (loadResult, ctx) = await LoadContextAsync(input.EmployeeId, input.CycleId, tracking: true, cancellationToken);
        if (loadResult is not null)
            return loadResult;

        var review = ctx!.Review!;

        if (review.SignoffStatus != ReviewSignoffStatus.Disputed)
            return Result<ReviewMeetingNotesDto>.Failure(
                "Only a disputed review can be resolved.", 409, "not_disputed");

        var actor = await GetCurrentEmployeeAsync(cancellationToken);
        var resolverName = SignerDisplayName(actor) ?? _currentUser.Email;
        var comments = string.IsNullOrWhiteSpace(input.Comments) ? null : input.Comments.Trim();

        if (input.Amend)
        {
            // Amend: return to NotesAdded so the manager (or HR) can edit & re-request sign-off.
            review.SignoffStatus = ReviewSignoffStatus.NotesAdded;
            review.SignoffRequestedAt = null;
            AppendSignoff(review, SignoffParty.Hr, SignoffAction.DisputeAmended,
                resolverName, actor?.Id, input.ClientIpAddress, comments);
        }
        else
        {
            // Confirm as-is: the review is signed off / locked (BR-5).
            review.SignoffStatus = ReviewSignoffStatus.SignedOff;
            review.SignoffCompletedAt = DateTime.UtcNow;
            review.IsLocked = true;
            AppendSignoff(review, SignoffParty.Hr, SignoffAction.DisputeConfirmed,
                resolverName, actor?.Id, input.ClientIpAddress, comments);
        }

        var saveFailure = await SaveAsync(cancellationToken);
        if (saveFailure is not null)
            return saveFailure;

        _logger.LogInformation(
            "Disputed review resolved by HR ({Resolution}). ReviewId={ReviewId}, EmployeeId={EmployeeId}, " +
            "CycleId={CycleId}, Status={Status}, TenantId={TenantId}, By={User}",
            input.Amend ? "amended" : "confirmed", review.Id, input.EmployeeId, input.CycleId,
            review.SignoffStatus, _tenantContext.TenantId, _currentUser.Email);

        return await ReloadNotesDtoAsync(input.EmployeeId, input.CycleId, cancellationToken);
    }

    // ── Export record (AC-4/FR-6) ─────────────────────────────────────

    public async Task<Result<ReviewExportDto>> GetExportRecordAsync(
        Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ReviewExportDto>.Failure("Tenant context is not resolved.", 400);

        var authz = await AuthorizeManagerOrHrAsync(employeeId, cancellationToken);
        if (authz.IsFailure)
            return Result<ReviewExportDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        var cycle = await _dbContext.AppraisalCycles.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (cycle is null)
            return Result<ReviewExportDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (employee is null)
            return Result<ReviewExportDto>.Failure("Employee not found.", 404, "employee_not_found");

        var review = await _dbContext.ManagerReviews.AsNoTracking()
            .Include(r => r.Items)
            .Include(r => r.MeetingNotes!).ThenInclude(n => n.Actions)
            .Include(r => r.Signoffs)
            .FirstOrDefaultAsync(r => r.EmployeeId == employeeId && r.CycleId == cycleId, cancellationToken);
        if (review is null)
            return Result<ReviewExportDto>.Failure("Manager review not found.", 404, "review_not_found");

        var goals = await _dbContext.Goals.AsNoTracking()
            .Where(g => g.EmployeeId == employeeId && g.CycleId == cycleId)
            .OrderBy(g => g.CreatedAt)
            .ToListAsync(cancellationToken);

        var self = await _dbContext.SelfAssessments.AsNoTracking()
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.EmployeeId == employeeId && s.CycleId == cycleId, cancellationToken);

        var reviewer = review.ReviewerEmployeeId is { } rid
            ? await _dbContext.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == rid, cancellationToken)
            : null;

        var mgrByGoal = review.Items.Where(i => !i.IsDeleted).ToDictionary(i => i.GoalId);
        var selfByGoal = self?.Items.Where(i => !i.IsDeleted).ToDictionary(i => i.GoalId)
            ?? new Dictionary<Guid, SelfAssessmentItem>();

        var goalDtos = goals.Select(g =>
        {
            mgrByGoal.TryGetValue(g.Id, out var mgr);
            selfByGoal.TryGetValue(g.Id, out var s);
            return new ReviewExportGoalDto
            {
                GoalId = g.Id,
                GoalTitle = g.Title,
                GoalWeight = g.Weight,
                SelfRating = s?.SelfRating,
                SelfComment = s?.Comment,
                ManagerRating = mgr?.ManagerRating,
                ManagerComment = mgr?.ManagerComment,
            };
        }).ToList();

        var dto = new ReviewExportDto
        {
            ManagerReviewId = review.Id,
            CycleId = cycle.Id,
            CycleName = cycle.Name,
            EmployeeId = employee.Id,
            EmployeeName = $"{employee.FirstName} {employee.LastName}".Trim(),
            EmployeeNo = employee.EmployeeNo,
            ReviewerName = reviewer is null ? null : $"{reviewer.FirstName} {reviewer.LastName}".Trim(),
            ReviewStatus = review.Status,
            ReviewStatusName = review.Status.ToString(),
            SignoffStatus = review.SignoffStatus,
            SignoffStatusName = review.SignoffStatus.ToString(),
            RatingScaleMax = cycle.RatingScaleMax,
            WeightedSelfScore = self is { Status: SelfAssessmentStatus.Submitted } ? self.WeightedSelfScore : null,
            WeightedManagerScore = review.WeightedManagerScore,
            FinalScore = review.FinalScore,
            SummaryComment = review.SummaryComment,
            SubmittedAt = review.SubmittedAt,
            SignoffCompletedAt = review.SignoffCompletedAt,
            NotesOpenedAt = review.NotesOpenedAt,
            IsLocked = review.IsLocked,
            Goals = goalDtos,
            MeetingNotes = review.MeetingNotes is null ? null : BuildNotesDtoFrom(cycle, employee, review),
            Signoffs = review.Signoffs.OrderBy(s => s.SignedAt).Select(MapSignoff).ToList(),
        };

        // FR-6 PDF SEAM: this returns the complete review DATA. PDF rendering (with tenant logo/branding) is a
        // deliberate seam — the codebase has no PDF library (same decision as US-PRF-005). TODO(US-PRF PDF).
        return Result<ReviewExportDto>.Success(dto);
    }

    // ── Caller-scoped self-service (ISSUE-288) ────────────────────────

    public async Task<Result<ReviewMeetingNotesDto>> GetMyMeetingNotesAsync(CancellationToken cancellationToken = default)
    {
        var ctxResult = await ResolveMyContextAsync(cancellationToken);
        if (ctxResult.IsFailure)
            return Result<ReviewMeetingNotesDto>.Failure(ctxResult.Error!, ctxResult.StatusCode ?? 400, ctxResult.ErrorCode);

        var (cycleId, employeeId) = ctxResult.Value;

        // The caller reads their OWN review — employeeId was resolved FROM the caller, so the manager/HR authz
        // gate (which guards the manager-side notes surface) does not apply here. The "caller IS the reviewed
        // employee" invariant is satisfied by construction. Tracked load (BR-2): the first time the employee
        // opens their notes while sign-off is pending, stamp NotesOpenedAt so Acknowledge & Sign can enforce
        // read-before-sign.
        var (loadResult, ctx) = await LoadContextAsync(employeeId, cycleId, tracking: true, cancellationToken);
        if (loadResult is not null)
            return loadResult;

        var review = ctx!.Review!;
        if (review.SignoffStatus == ReviewSignoffStatus.PendingEmployeeSignOff && review.NotesOpenedAt is null)
        {
            review.NotesOpenedAt = DateTime.UtcNow;
            var saveFailure = await SaveAsync(cancellationToken);
            if (saveFailure is not null)
                return saveFailure;
        }

        return Result<ReviewMeetingNotesDto>.Success(BuildNotesDto(ctx));
    }

    public async Task<Result<ReviewMeetingNotesDto>> AcknowledgeMyReviewAsync(
        string? clientIpAddress, CancellationToken cancellationToken = default)
    {
        var ctxResult = await ResolveMyContextAsync(cancellationToken);
        if (ctxResult.IsFailure)
            return Result<ReviewMeetingNotesDto>.Failure(ctxResult.Error!, ctxResult.StatusCode ?? 400, ctxResult.ErrorCode);

        var (cycleId, employeeId) = ctxResult.Value;
        // Reuse the existing acknowledge logic verbatim (immutable signature + lock + notify). Its "caller IS the
        // reviewed employee" guard is trivially satisfied since employeeId came from the caller.
        return await AcknowledgeAsync(
            new SignoffActionInput(cycleId, employeeId, Comments: null, clientIpAddress), cancellationToken);
    }

    public async Task<Result<ReviewMeetingNotesDto>> DisputeMyReviewAsync(
        string? comments, string? clientIpAddress, CancellationToken cancellationToken = default)
    {
        var ctxResult = await ResolveMyContextAsync(cancellationToken);
        if (ctxResult.IsFailure)
            return Result<ReviewMeetingNotesDto>.Failure(ctxResult.Error!, ctxResult.StatusCode ?? 400, ctxResult.ErrorCode);

        var (cycleId, employeeId) = ctxResult.Value;
        // Reuse the existing dispute logic verbatim (mandatory-comments 422 + Disputed + notify manager/HR).
        return await DisputeAsync(
            new SignoffActionInput(cycleId, employeeId, comments, clientIpAddress), cancellationToken);
    }

    /// <summary>
    /// Resolves the caller's self-service context (ISSUE-288): employeeId from the caller's Employee row and
    /// cycleId from the active appraisal cycle. Mirrors <see cref="AppraisalCycleService.GetActiveAsync"/>'s
    /// active-cycle selection inline (most recently started Active cycle) rather than injecting the cycle
    /// service, because the sign-off service is hard-constructed by its unit tests. 403 when the caller is not
    /// linked to an employee; 404 when no cycle is active.
    /// </summary>
    private async Task<Result<(Guid CycleId, Guid EmployeeId)>> ResolveMyContextAsync(CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return Result<(Guid CycleId, Guid EmployeeId)>.Failure("Tenant context is not resolved.", 400);

        var actor = await GetCurrentEmployeeAsync(cancellationToken);
        if (actor is null)
            return Result<(Guid CycleId, Guid EmployeeId)>.Failure(
                "The current user is not linked to an employee record.", 403, "no_employee_record");

        var cycle = await _dbContext.AppraisalCycles.AsNoTracking()
            .Where(c => c.Status == AppraisalCycleStatus.Active)
            .OrderByDescending(c => c.StartDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (cycle is null)
            return Result<(Guid CycleId, Guid EmployeeId)>.Failure(
                "No active performance cycle.", 404, "no_active_cycle");

        return Result<(Guid CycleId, Guid EmployeeId)>.Success((cycle.Id, actor.Id));
    }

    // ── Shared helpers ────────────────────────────────────────────────

    /// <summary>Loaded review + cycle + employee for an operation.</summary>
    private sealed class ReviewContext
    {
        public required AppraisalCycle Cycle { get; init; }
        public required Employee Employee { get; init; }
        public required ManagerReview Review { get; set; }
    }

    private async Task<(Result<ReviewMeetingNotesDto>? Failure, ReviewContext? Ctx)> LoadContextAsync(
        Guid employeeId, Guid cycleId, bool tracking, CancellationToken cancellationToken)
    {
        var cycle = await _dbContext.AppraisalCycles.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (cycle is null)
            return (Result<ReviewMeetingNotesDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found"), null);

        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (employee is null)
            return (Result<ReviewMeetingNotesDto>.Failure("Employee not found.", 404, "employee_not_found"), null);

        IQueryable<ManagerReview> query = _dbContext.ManagerReviews
            .Include(r => r.MeetingNotes!).ThenInclude(n => n.Actions)
            .Include(r => r.Signoffs);
        if (!tracking)
            query = query.AsNoTracking();

        var review = await query.FirstOrDefaultAsync(
            r => r.EmployeeId == employeeId && r.CycleId == cycleId, cancellationToken);
        if (review is null)
            return (Result<ReviewMeetingNotesDto>.Failure(
                "A manager review must be submitted before meeting notes can be added.", 404, "review_not_found"), null);

        return (null, new ReviewContext { Cycle = cycle, Employee = employee, Review = review });
    }

    private async Task<Result<ReviewMeetingNotesDto>> ReloadNotesDtoAsync(
        Guid employeeId, Guid cycleId, CancellationToken cancellationToken)
    {
        var (failure, ctx) = await LoadContextAsync(employeeId, cycleId, tracking: false, cancellationToken);
        if (failure is not null)
            return failure;
        return Result<ReviewMeetingNotesDto>.Success(BuildNotesDto(ctx!));
    }

    private void AppendSignoff(
        ManagerReview review, SignoffParty party, SignoffAction action,
        string? signerName, Guid? signerEmployeeId, string? clientIp, string? comments)
    {
        var entry = new ReviewSignoff
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            ManagerReviewId = review.Id,
            Party = party,
            Action = action,
            SignerName = signerName ?? string.Empty,
            SignerUserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            SignerEmployeeId = signerEmployeeId,
            SignedAt = DateTime.UtcNow,
            ClientIpAddress = clientIp,
            Comments = comments,
            IsDeleted = false,
        };
        review.Signoffs.Add(entry);
        _dbContext.ReviewSignoffs.Add(entry);
    }

    /// <summary>
    /// Persists pending changes. Returns null on success, or a 409 failure on an optimistic-concurrency clash
    /// (NFR-3 — two sessions edited the same review's notes at once). Callers short-circuit on a non-null result.
    /// </summary>
    private async Task<Result<ReviewMeetingNotesDto>?> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ReviewMeetingNotesDto>.Failure(
                "This review was modified by another session. Reload and try again.", 409, "concurrency_conflict");
        }
    }

    private Task<Employee?> GetCurrentEmployeeAsync(CancellationToken cancellationToken)
        => _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);

    private static string? SignerDisplayName(Employee? e)
        => e is null ? null : $"{e.FirstName} {e.LastName}".Trim();

    /// <summary>
    /// Authorization mirroring ManagerReviewService: HR (Review.All) reviews anyone; a manager (Review.Team)
    /// only their direct reports. Used for the manager-side notes operations + export.
    /// </summary>
    private async Task<Result> AuthorizeManagerOrHrAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var target = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (target is null)
            return Result.Failure("Employee not found.", 404, "employee_not_found");

        var permissions = _currentUser.Permissions;

        if (permissions.Contains(PermissionCatalog.Performance.ReviewAll))
            return Result.Success();

        if (!permissions.Contains(PermissionCatalog.Performance.ReviewTeam))
            return Result.Failure("You do not have permission to review performance.", 403, "forbidden");

        var manager = await GetCurrentEmployeeAsync(cancellationToken);
        if (manager is null)
            return Result.Failure("The current user is not linked to an employee record.", 403, "no_employee_record");

        if (target.ReportsToEmployeeId != manager.Id)
            return Result.Failure("You can only manage reviews for your direct reports.", 403, "not_direct_report");

        return Result.Success();
    }

    // ── DTO builders ──────────────────────────────────────────────────

    private static ReviewMeetingNotesDto BuildNotesDto(ReviewContext ctx)
        => BuildNotesDtoFrom(ctx.Cycle, ctx.Employee, ctx.Review);

    private static ReviewMeetingNotesDto BuildNotesDtoFrom(AppraisalCycle cycle, Employee employee, ManagerReview review)
    {
        var notes = review.MeetingNotes;
        var isTemplate = notes is null;
        var actions = notes?.Actions.Where(a => !a.IsDeleted).OrderBy(a => a.SortOrder)
            .Select(a => new MeetingNotesActionDto(a.Id, a.Description, a.Deadline, a.SortOrder))
            .ToList() ?? new List<MeetingNotesActionDto>();

        return new ReviewMeetingNotesDto
        {
            ManagerReviewId = review.Id,
            MeetingNotesId = notes?.Id,
            EmployeeId = employee.Id,
            EmployeeName = $"{employee.FirstName} {employee.LastName}".Trim(),
            EmployeeNo = employee.EmployeeNo,
            CycleId = cycle.Id,
            // When no notes exist yet, return the template default (FR-1) so the editor opens populated.
            Body = notes?.Body ?? (cycle.MeetingNotesTemplate ?? DefaultTemplate),
            Strengths = notes?.Strengths,
            DevelopmentAreas = notes?.DevelopmentAreas,
            Summary = notes?.Summary,
            Actions = actions,
            SignoffStatus = review.SignoffStatus,
            SignoffStatusName = review.SignoffStatus.ToString(),
            SignoffRequestedAt = review.SignoffRequestedAt,
            SignoffCompletedAt = review.SignoffCompletedAt,
            NotesOpenedAt = review.NotesOpenedAt,
            IsLocked = review.IsLocked,
            IsTemplate = isTemplate,
            Signoffs = review.Signoffs.Where(s => !s.IsDeleted).OrderBy(s => s.SignedAt).Select(MapSignoff).ToList(),
        };
    }

    private static ReviewSignoffEntryDto MapSignoff(ReviewSignoff s) => new()
    {
        Id = s.Id,
        Party = s.Party,
        PartyName = s.Party.ToString(),
        Action = s.Action,
        ActionName = s.Action.ToString(),
        SignerName = s.SignerName,
        SignedAt = s.SignedAt,
        ClientIpAddress = s.ClientIpAddress,
        Comments = s.Comments,
    };
}
