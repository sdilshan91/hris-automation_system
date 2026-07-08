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
/// 360-degree reviewer configuration service (US-PRF-005 AC-1/AC-2/FR-1/FR-2/FR-3). HR (Performance.Review.All)
/// configures who reviews an employee. Self + Manager are auto-assigned (Manager from the org tree), Peers
/// (same department) and Direct Reports (org tree) are auto-suggested; manual add/remove is supported. BR-2
/// (the reviewee is never their own Peer), BR-1 (only when the cycle's 360 toggle is on) and BR-3
/// de-duplication are enforced here. Every read/write is tenant-scoped via the EF global query filter (NFR-2).
/// </summary>
public sealed class ReviewerAssignmentService : IReviewerAssignmentService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IPerformanceNotificationService _notifications;
    private readonly ILogger<ReviewerAssignmentService> _logger;

    public ReviewerAssignmentService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IPerformanceNotificationService notifications,
        ILogger<ReviewerAssignmentService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _notifications = notifications;
        _logger = logger;
    }

    // ── Configuration view (AC-1) ────────────────────────────────────

    public async Task<Result<ReviewerConfigurationDto>> GetConfigurationAsync(
        Guid revieweeEmployeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ReviewerConfigurationDto>.Failure("Tenant context is not resolved.", 400);

        // BUG-244: HR (Review.All) unrestricted, or a Team manager of the reviewee when the tenant allows it.
        var authz = await AuthorizeConfigureAsync(revieweeEmployeeId, cancellationToken);
        if (authz.IsFailure)
            return Result<ReviewerConfigurationDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        var cycle = await _dbContext.AppraisalCycles
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (cycle is null)
            return Result<ReviewerConfigurationDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        var reviewee = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == revieweeEmployeeId, cancellationToken);
        if (reviewee is null)
            return Result<ReviewerConfigurationDto>.Failure("Employee not found.", 404, "employee_not_found");

        // BR-1: 360 only when the cycle toggle is on.
        if (!cycle.Is360Enabled)
            return Result<ReviewerConfigurationDto>.Failure(
                "360-degree feedback is not enabled for this cycle.", 409, "not_360_enabled");

        // Auto-seed Self + Manager + suggested peers/reports the first time the config is opened (FR-2).
        await EnsureAutoAssignmentsAsync(reviewee, cycleId, cancellationToken);

        return Result<ReviewerConfigurationDto>.Success(
            await BuildConfigurationAsync(reviewee, cycle, cancellationToken));
    }

    /// <summary>
    /// Projects the current reviewer-configuration view for a reviewee + cycle WITHOUT the auto-seeding side
    /// effect (unlike <see cref="GetConfigurationAsync"/>, which seeds first). BUG-244 #1 calls this after a
    /// full-replace save so the response reflects exactly the just-saved state rather than re-seeding removals.
    /// </summary>
    private async Task<ReviewerConfigurationDto> BuildConfigurationAsync(
        Employee reviewee, AppraisalCycle cycle, CancellationToken cancellationToken)
    {
        var assignments = await _dbContext.ReviewerAssignments
            .AsNoTracking()
            .Where(a => a.CycleId == cycle.Id && a.RevieweeEmployeeId == reviewee.Id)
            .ToListAsync(cancellationToken);

        var reviewerIds = assignments.Select(a => a.ReviewerEmployeeId).ToHashSet();
        var employees = await LoadEmployeeLookupAsync(reviewerIds, cancellationToken);

        var assignmentDtos = assignments
            .OrderBy(a => a.Category)
            .Select(a => ToDto(a, employees))
            .ToList();

        // Suggestions = candidates not already assigned (AC-1/FR-2).
        var suggestedPeers = (await GetPeerCandidatesAsync(reviewee, cancellationToken))
            .Where(e => !reviewerIds.Contains(e.Id))
            .Select(ToSuggested).ToList();
        var suggestedReports = (await GetDirectReportCandidatesAsync(reviewee, cancellationToken))
            .Where(e => !reviewerIds.Contains(e.Id))
            .Select(ToSuggested).ToList();

        return new ReviewerConfigurationDto
        {
            CycleId = cycle.Id,
            RevieweeEmployeeId = reviewee.Id,
            RevieweeName = FullName(reviewee),
            Is360Enabled = cycle.Is360Enabled,
            IsAnonymousFeedback = cycle.IsAnonymousFeedback,
            MinPeerReviewers = cycle.Min360PeerReviewers,
            Assignments = assignmentDtos,
            SuggestedPeers = suggestedPeers,
            SuggestedDirectReports = suggestedReports,
        };
    }

    // ── Config-screen LOAD for the active cycle (BUG-244) ────────────

    public async Task<Result<ReviewerConfigurationDto>> GetConfigurationForActiveCycleAsync(
        Guid revieweeEmployeeId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ReviewerConfigurationDto>.Failure("Tenant context is not resolved.", 400);

        // Authorize before resolving the cycle so a non-permitted caller can't probe cycle state (matches #3).
        var authz = await AuthorizeConfigureAsync(revieweeEmployeeId, cancellationToken);
        if (authz.IsFailure)
            return Result<ReviewerConfigurationDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        var cycleResult = await ResolveActive360CycleAsync(cancellationToken);
        if (cycleResult.IsFailure)
            return Result<ReviewerConfigurationDto>.Failure(cycleResult.Error!, cycleResult.StatusCode ?? 400, cycleResult.ErrorCode);

        // Delegate to the cycle-keyed load so the Self/Manager + suggested-peer/report auto-seed runs (FR-2).
        return await GetConfigurationAsync(revieweeEmployeeId, cycleResult.Value!.Id, cancellationToken);
    }

    // ── Full-replace reviewer set (BUG-244 #1) ───────────────────────

    public async Task<Result<ReviewerConfigurationDto>> SaveReviewersAsync(
        Guid revieweeEmployeeId, IReadOnlyList<ReviewerInputDto> reviewers, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ReviewerConfigurationDto>.Failure("Tenant context is not resolved.", 400);

        var authz = await AuthorizeConfigureAsync(revieweeEmployeeId, cancellationToken);
        if (authz.IsFailure)
            return Result<ReviewerConfigurationDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        var cycleResult = await ResolveActive360CycleAsync(cancellationToken);
        if (cycleResult.IsFailure)
            return Result<ReviewerConfigurationDto>.Failure(cycleResult.Error!, cycleResult.StatusCode ?? 400, cycleResult.ErrorCode);
        var cycle = cycleResult.Value!;

        var reviewee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == revieweeEmployeeId, cancellationToken);
        if (reviewee is null)
            return Result<ReviewerConfigurationDto>.Failure("Employee not found.", 404, "employee_not_found");

        var incoming = reviewers ?? [];

        // Only Peer + DirectReport are client-managed; Self + Manager are server-owned (FR-2).
        foreach (var r in incoming)
        {
            if (r.Category is not (ReviewerCategory.Peer or ReviewerCategory.DirectReport))
                return Result<ReviewerConfigurationDto>.Failure(
                    "Only Peer and Direct Report reviewers can be configured; Self and Manager are assigned automatically.",
                    422, "invalid_category");
            if (r.ReviewerId == Guid.Empty)
                return Result<ReviewerConfigurationDto>.Failure("A reviewer is required.", 422, "invalid_reviewer");
            // BR-2: the reviewee can never be their own Peer.
            if (r.Category == ReviewerCategory.Peer && r.ReviewerId == revieweeEmployeeId)
                return Result<ReviewerConfigurationDto>.Failure(
                    "An employee cannot be their own peer reviewer.", 422, "self_as_peer");
        }

        // BR-3: no duplicate reviewer+category within the payload.
        var seen = new HashSet<(Guid, ReviewerCategory)>();
        foreach (var r in incoming)
            if (!seen.Add((r.ReviewerId, r.Category)))
                return Result<ReviewerConfigurationDto>.Failure(
                    "The same reviewer is listed twice in a category.", 409, "duplicate_reviewer");

        // Validate all reviewers exist in-tenant (the global filter scopes the query).
        var reviewerIds = incoming.Select(r => r.ReviewerId).ToHashSet();
        var existingIds = await _dbContext.Employees.AsNoTracking()
            .Where(e => reviewerIds.Contains(e.Id))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
        if (reviewerIds.Except(existingIds).Any())
            return Result<ReviewerConfigurationDto>.Failure(
                "One or more selected reviewers do not exist in this tenant.", 422, "invalid_reviewer");

        // Full-replace of the manual (Peer + DirectReport) rows only; Self + Manager are left untouched.
        var current = await _dbContext.ReviewerAssignments
            .Where(a => a.CycleId == cycle.Id && a.RevieweeEmployeeId == revieweeEmployeeId
                && (a.Category == ReviewerCategory.Peer || a.Category == ReviewerCategory.DirectReport))
            .ToListAsync(cancellationToken);

        var desired = incoming.Select(r => (r.ReviewerId, r.Category)).ToHashSet();
        var currentKeys = current.Select(a => (a.ReviewerEmployeeId, a.Category)).ToHashSet();

        // Remove rows no longer desired — but a reviewer who already submitted cannot be removed (AC-3).
        foreach (var a in current)
        {
            if (desired.Contains((a.ReviewerEmployeeId, a.Category)))
                continue;
            if (a.Status == ReviewerAssignmentStatus.Completed)
                return Result<ReviewerConfigurationDto>.Failure(
                    "A reviewer who has already submitted feedback cannot be removed.", 409, "already_completed");
            a.IsDeleted = true;
        }

        // Add newly desired rows (the soft-delete-aware unique index lets a previously-removed pair re-appear).
        foreach (var (reviewerId, category) in desired)
        {
            if (currentKeys.Contains((reviewerId, category)))
                continue;
            _dbContext.ReviewerAssignments.Add(new ReviewerAssignment
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                CycleId = cycle.Id,
                RevieweeEmployeeId = revieweeEmployeeId,
                ReviewerEmployeeId = reviewerId,
                Category = category,
                Status = ReviewerAssignmentStatus.Pending,
                IsDeleted = false,
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "360 reviewers saved (full-replace). RevieweeId={RevieweeId}, CycleId={CycleId}, Count={Count}, " +
            "TenantId={TenantId}, By={User}",
            revieweeEmployeeId, cycle.Id, desired.Count, _tenantContext.TenantId, _currentUser.Email);

        // Return the refreshed config from the just-saved state (no re-seeding).
        return Result<ReviewerConfigurationDto>.Success(
            await BuildConfigurationAsync(reviewee, cycle, cancellationToken));
    }

    // ── Completion tracker (BUG-244 #3) ──────────────────────────────

    public async Task<Result<CompletionTrackerDto>> GetTrackerAsync(
        Guid revieweeEmployeeId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CompletionTrackerDto>.Failure("Tenant context is not resolved.", 400);

        var authz = await AuthorizeConfigureAsync(revieweeEmployeeId, cancellationToken);
        if (authz.IsFailure)
            return Result<CompletionTrackerDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        var cycleResult = await ResolveActive360CycleAsync(cancellationToken);
        if (cycleResult.IsFailure)
            return Result<CompletionTrackerDto>.Failure(cycleResult.Error!, cycleResult.StatusCode ?? 400, cycleResult.ErrorCode);
        var cycle = cycleResult.Value!;

        var assignments = await _dbContext.ReviewerAssignments
            .AsNoTracking()
            .Where(a => a.CycleId == cycle.Id && a.RevieweeEmployeeId == revieweeEmployeeId)
            .ToListAsync(cancellationToken);

        // BUG-244 #3: no 360 window → overdue=0; only Peer has a configured minimum.
        var categories = ThreeSixtyCompletion.ByCategory(assignments)
            .Select(c => new CategoryProgressDto
            {
                Category = c.Category,
                Submitted = c.Completed,
                Pending = c.Assigned - c.Completed,
                Overdue = 0,
                Minimum = c.Category == ReviewerCategory.Peer ? cycle.Min360PeerReviewers : 0,
            })
            .ToList();

        return Result<CompletionTrackerDto>.Success(new CompletionTrackerDto
        {
            EmployeeId = revieweeEmployeeId,
            Categories = categories,
        });
    }

    // ── Add reviewer (AC-1/FR-2) ─────────────────────────────────────

    public async Task<Result<ReviewerAssignmentDto>> AddReviewerAsync(
        Guid revieweeEmployeeId, Guid cycleId, Guid reviewerEmployeeId, ReviewerCategory category,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ReviewerAssignmentDto>.Failure("Tenant context is not resolved.", 400);

        // BUG-244: HR (Review.All) unrestricted, or a Team manager of the reviewee when the tenant allows it.
        var authz = await AuthorizeConfigureAsync(revieweeEmployeeId, cancellationToken);
        if (authz.IsFailure)
            return Result<ReviewerAssignmentDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        // BR-2: the reviewee can never be their own Peer (self-assessment is a separate category).
        if (category == ReviewerCategory.Peer && reviewerEmployeeId == revieweeEmployeeId)
            return Result<ReviewerAssignmentDto>.Failure(
                "An employee cannot be their own peer reviewer.", 422, "self_as_peer");

        var cycle = await _dbContext.AppraisalCycles
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (cycle is null)
            return Result<ReviewerAssignmentDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        if (!cycle.Is360Enabled)
            return Result<ReviewerAssignmentDto>.Failure(
                "360-degree feedback is not enabled for this cycle.", 409, "not_360_enabled");

        var reviewee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == revieweeEmployeeId, cancellationToken);
        if (reviewee is null)
            return Result<ReviewerAssignmentDto>.Failure("Reviewee not found.", 404, "employee_not_found");

        var reviewer = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == reviewerEmployeeId, cancellationToken);
        if (reviewer is null)
            return Result<ReviewerAssignmentDto>.Failure("Reviewer not found.", 404, "reviewer_not_found");

        // BR-3: de-dupe — one reviewer per reviewee per category per cycle.
        var existing = await _dbContext.ReviewerAssignments
            .FirstOrDefaultAsync(a => a.CycleId == cycleId
                && a.RevieweeEmployeeId == revieweeEmployeeId
                && a.ReviewerEmployeeId == reviewerEmployeeId
                && a.Category == category, cancellationToken);
        if (existing is not null)
            return Result<ReviewerAssignmentDto>.Failure(
                "This reviewer is already assigned in this category.", 409, "duplicate_reviewer");

        var assignment = new ReviewerAssignment
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            CycleId = cycleId,
            RevieweeEmployeeId = revieweeEmployeeId,
            ReviewerEmployeeId = reviewerEmployeeId,
            Category = category,
            Status = ReviewerAssignmentStatus.Pending,
            IsDeleted = false,
        };
        _dbContext.ReviewerAssignments.Add(assignment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "360 reviewer added. AssignmentId={Id}, RevieweeId={RevieweeId}, ReviewerId={ReviewerId}, " +
            "Category={Category}, CycleId={CycleId}, TenantId={TenantId}, By={User}",
            assignment.Id, revieweeEmployeeId, reviewerEmployeeId, category, cycleId,
            _tenantContext.TenantId, _currentUser.Email);

        var lookup = await LoadEmployeeLookupAsync(new HashSet<Guid> { reviewerEmployeeId }, cancellationToken);
        return Result<ReviewerAssignmentDto>.Success(ToDto(assignment, lookup));
    }

    // ── Remove reviewer (AC-1/FR-2) ──────────────────────────────────

    public async Task<Result> RemoveReviewerAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var assignment = await _dbContext.ReviewerAssignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);
        if (assignment is null)
            return Result.Failure("Reviewer assignment not found.", 404, "assignment_not_found");

        // BUG-244: HR (Review.All) unrestricted, or a Team manager of the reviewee when the tenant allows it.
        var authz = await AuthorizeConfigureAsync(assignment.RevieweeEmployeeId, cancellationToken);
        if (authz.IsFailure)
            return authz;

        if (assignment.Status == ReviewerAssignmentStatus.Completed)
            return Result.Failure(
                "A reviewer who has already submitted feedback cannot be removed.", 409, "already_completed");

        assignment.IsDeleted = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "360 reviewer removed. AssignmentId={Id}, TenantId={TenantId}, By={User}",
            assignmentId, _tenantContext.TenantId, _currentUser.Email);

        return Result.Success();
    }

    // ── Notify reviewers — enter 360 phase (AC-2) ────────────────────

    public async Task<Result<int>> NotifyReviewersAsync(
        Guid revieweeEmployeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<int>.Failure("Tenant context is not resolved.", 400);

        if (!HasReviewAll())
            return Result<int>.Failure("You do not have permission to configure 360 reviewers.", 403, "forbidden");

        var cycle = await _dbContext.AppraisalCycles.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (cycle is null)
            return Result<int>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        var assignments = await _dbContext.ReviewerAssignments
            .Where(a => a.CycleId == cycleId
                && a.RevieweeEmployeeId == revieweeEmployeeId
                && a.Status == ReviewerAssignmentStatus.Pending)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var a in assignments)
        {
            a.NotifiedAt = now;
            await _notifications.NotifyReviewerAssignedAsync(
                a.ReviewerEmployeeId, revieweeEmployeeId, cycleId, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "360 reviewers notified. RevieweeId={RevieweeId}, CycleId={CycleId}, Count={Count}, TenantId={TenantId}",
            revieweeEmployeeId, cycleId, assignments.Count, _tenantContext.TenantId);

        return Result<int>.Success(assignments.Count);
    }

    // ── Auto-assignment (FR-2) ───────────────────────────────────────

    private async Task EnsureAutoAssignmentsAsync(Employee reviewee, Guid cycleId, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.ReviewerAssignments
            .Where(a => a.CycleId == cycleId && a.RevieweeEmployeeId == reviewee.Id)
            .Select(a => new { a.ReviewerEmployeeId, a.Category })
            .ToListAsync(cancellationToken);

        var present = existing.Select(e => (e.ReviewerEmployeeId, e.Category)).ToHashSet();
        var added = false;

        // Self (auto-assigned, FR-2).
        added |= TryAdd(reviewee.Id, reviewee.Id, cycleId, ReviewerCategory.Self, present);

        // Manager (auto-assigned from org tree, FR-2).
        if (reviewee.ReportsToEmployeeId is { } managerId)
            added |= TryAdd(reviewee.Id, managerId, cycleId, ReviewerCategory.Manager, present);

        // Suggested peers (same department) — pre-seeded as assignments the HR can later remove (AC-1/FR-2).
        foreach (var peer in await GetPeerCandidatesAsync(reviewee, cancellationToken))
            added |= TryAdd(reviewee.Id, peer.Id, cycleId, ReviewerCategory.Peer, present);

        // Suggested direct reports (org tree).
        foreach (var report in await GetDirectReportCandidatesAsync(reviewee, cancellationToken))
            added |= TryAdd(reviewee.Id, report.Id, cycleId, ReviewerCategory.DirectReport, present);

        if (added)
            await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private bool TryAdd(
        Guid revieweeId, Guid reviewerId, Guid cycleId, ReviewerCategory category,
        HashSet<(Guid, ReviewerCategory)> present)
    {
        // BR-2: never auto-assign the reviewee as their own Peer.
        if (category == ReviewerCategory.Peer && reviewerId == revieweeId)
            return false;
        if (!present.Add((reviewerId, category)))
            return false;

        _dbContext.ReviewerAssignments.Add(new ReviewerAssignment
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            CycleId = cycleId,
            RevieweeEmployeeId = revieweeId,
            ReviewerEmployeeId = reviewerId,
            Category = category,
            Status = ReviewerAssignmentStatus.Pending,
            IsDeleted = false,
        });
        return true;
    }

    private async Task<List<Employee>> GetPeerCandidatesAsync(Employee reviewee, CancellationToken cancellationToken)
        => await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.DepartmentId == reviewee.DepartmentId
                && e.Id != reviewee.Id // BR-2
                && e.Status == EmployeeStatus.Active)
            .OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
            .ToListAsync(cancellationToken);

    private async Task<List<Employee>> GetDirectReportCandidatesAsync(Employee reviewee, CancellationToken cancellationToken)
        => await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.ReportsToEmployeeId == reviewee.Id
                && e.Status == EmployeeStatus.Active)
            .OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
            .ToListAsync(cancellationToken);

    // ── Helpers ──────────────────────────────────────────────────────

    private bool HasReviewAll() => _currentUser.Permissions.Contains(PermissionCatalog.Performance.ReviewAll);

    /// <summary>
    /// BUG-244 authorization for the 360 reviewer-CONFIG surface (get/save/add/remove/tracker). Four outcomes:
    ///  1. Caller holds <c>Performance.Review.All</c> (HR) → allowed, unrestricted.
    ///  2. Tenant toggle on AND caller holds <c>Performance.Review.Team</c> AND caller is the DIRECT manager of
    ///     the target employee → allowed.
    ///  3. Tenant toggle on AND caller holds <c>Performance.Review.Team</c> but is NOT the target's manager →
    ///     403 <c>not_team_manager</c>.
    ///  4. Otherwise (no Review.All, and either the toggle is off or the caller lacks Review.Team) →
    ///     403 <c>forbidden</c>.
    /// The manager check is exact (caller's employee == target's <c>ReportsToEmployeeId</c>) and tenant-scoped
    /// via the global query filter.
    /// </summary>
    private async Task<Result> AuthorizeConfigureAsync(Guid targetEmployeeId, CancellationToken cancellationToken)
    {
        if (HasReviewAll())
            return Result.Success();

        var managersAllowed = await IsManagerReviewerConfigAllowedAsync(cancellationToken);
        var isTeamReviewer = _currentUser.Permissions.Contains(PermissionCatalog.Performance.ReviewTeam);

        if (managersAllowed && isTeamReviewer)
        {
            var caller = await GetCurrentEmployeeAsync(cancellationToken);
            var target = await _dbContext.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == targetEmployeeId, cancellationToken);
            if (caller is not null && target is not null && target.ReportsToEmployeeId == caller.Id)
                return Result.Success();

            return Result.Failure(
                "You can only configure 360 reviewers for your own direct reports.", 403, "not_team_manager");
        }

        return Result.Failure("You do not have permission to configure 360 reviewers.", 403, "forbidden");
    }

    /// <summary>BUG-244: reads the tenant toggle (default true) that lets Team managers configure reviewers.</summary>
    private async Task<bool> IsManagerReviewerConfigAllowedAsync(CancellationToken cancellationToken)
    {
        // The Tenant row is scoped by Id (it IS the tenant); the only filter is !IsDeleted.
        var allowed = await _dbContext.Tenants.AsNoTracking()
            .Where(t => t.Id == _tenantContext.TenantId)
            .Select(t => (bool?)t.AllowManagerReviewerConfig)
            .FirstOrDefaultAsync(cancellationToken);
        return allowed ?? true;
    }

    /// <summary>Resolves the caller's own employee record (portal user → employee via UserId), or null.</summary>
    private Task<Employee?> GetCurrentEmployeeAsync(CancellationToken cancellationToken)
        => _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);

    /// <summary>
    /// Resolves the single Active, 360-enabled appraisal cycle for BUG-244 #1/#3, mirroring
    /// <see cref="AppraisalCycleService.GetActiveAsync"/>'s canonical single-active selection (most-recently
    /// started Active cycle → 404 <c>no_active_cycle</c>) then adds the 360 gate (409 <c>not_360_enabled</c>).
    /// The query (not the DTO) is replicated inline so this service needs no extra dependency — keeping its
    /// constructor stable for the existing unit/integration fixtures. Every read is tenant-scoped via the EF
    /// global query filter (NFR-2).
    /// </summary>
    private async Task<Result<AppraisalCycle>> ResolveActive360CycleAsync(CancellationToken cancellationToken)
    {
        var cycle = await _dbContext.AppraisalCycles.AsNoTracking()
            .Where(c => c.Status == AppraisalCycleStatus.Active)
            .OrderByDescending(c => c.StartDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (cycle is null)
            return Result<AppraisalCycle>.Failure("No active appraisal cycle.", 404, "no_active_cycle");
        if (!cycle.Is360Enabled)
            return Result<AppraisalCycle>.Failure(
                "360-degree feedback is not enabled for the active cycle.", 409, "not_360_enabled");

        return Result<AppraisalCycle>.Success(cycle);
    }

    private async Task<Dictionary<Guid, Employee>> LoadEmployeeLookupAsync(
        HashSet<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return new Dictionary<Guid, Employee>();
        return await _dbContext.Employees
            .AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, cancellationToken);
    }

    private static ReviewerAssignmentDto ToDto(ReviewerAssignment a, IReadOnlyDictionary<Guid, Employee> employees)
    {
        employees.TryGetValue(a.ReviewerEmployeeId, out var reviewer);
        return new ReviewerAssignmentDto
        {
            Id = a.Id,
            CycleId = a.CycleId,
            RevieweeEmployeeId = a.RevieweeEmployeeId,
            ReviewerEmployeeId = a.ReviewerEmployeeId,
            ReviewerName = reviewer is null ? string.Empty : FullName(reviewer),
            ReviewerEmployeeNo = reviewer?.EmployeeNo,
            Category = a.Category,
            CategoryName = a.Category.ToString(),
            Status = a.Status,
            StatusName = a.Status.ToString(),
            NotifiedAt = a.NotifiedAt,
            CompletedAt = a.CompletedAt,
        };
    }

    private static SuggestedReviewerDto ToSuggested(Employee e) => new()
    {
        EmployeeId = e.Id,
        Name = FullName(e),
        EmployeeNo = e.EmployeeNo,
    };

    private static string FullName(Employee e) => $"{e.FirstName} {e.LastName}".Trim();
}
