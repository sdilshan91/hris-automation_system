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

        if (!HasReviewAll())
            return Result<ReviewerConfigurationDto>.Failure(
                "You do not have permission to configure 360 reviewers.", 403, "forbidden");

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

        var assignments = await _dbContext.ReviewerAssignments
            .AsNoTracking()
            .Where(a => a.CycleId == cycleId && a.RevieweeEmployeeId == revieweeEmployeeId)
            .ToListAsync(cancellationToken);

        var reviewerIds = assignments.Select(a => a.ReviewerEmployeeId).ToHashSet();
        var employees = await LoadEmployeeLookupAsync(reviewerIds, cancellationToken);

        var assignmentDtos = assignments
            .OrderBy(a => a.Category)
            .Select(a => ToDto(a, employees))
            .ToList();

        // Suggestions = candidates not already assigned (AC-1/FR-2).
        var assignedReviewerIds = reviewerIds;
        var suggestedPeers = (await GetPeerCandidatesAsync(reviewee, cancellationToken))
            .Where(e => !assignedReviewerIds.Contains(e.Id))
            .Select(ToSuggested).ToList();
        var suggestedReports = (await GetDirectReportCandidatesAsync(reviewee, cancellationToken))
            .Where(e => !assignedReviewerIds.Contains(e.Id))
            .Select(ToSuggested).ToList();

        return Result<ReviewerConfigurationDto>.Success(new ReviewerConfigurationDto
        {
            CycleId = cycleId,
            RevieweeEmployeeId = revieweeEmployeeId,
            RevieweeName = FullName(reviewee),
            Is360Enabled = cycle.Is360Enabled,
            IsAnonymousFeedback = cycle.IsAnonymousFeedback,
            MinPeerReviewers = cycle.Min360PeerReviewers,
            Assignments = assignmentDtos,
            SuggestedPeers = suggestedPeers,
            SuggestedDirectReports = suggestedReports,
        });
    }

    // ── Add reviewer (AC-1/FR-2) ─────────────────────────────────────

    public async Task<Result<ReviewerAssignmentDto>> AddReviewerAsync(
        Guid revieweeEmployeeId, Guid cycleId, Guid reviewerEmployeeId, ReviewerCategory category,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ReviewerAssignmentDto>.Failure("Tenant context is not resolved.", 400);

        if (!HasReviewAll())
            return Result<ReviewerAssignmentDto>.Failure(
                "You do not have permission to configure 360 reviewers.", 403, "forbidden");

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

        if (!HasReviewAll())
            return Result.Failure("You do not have permission to configure 360 reviewers.", 403, "forbidden");

        var assignment = await _dbContext.ReviewerAssignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);
        if (assignment is null)
            return Result.Failure("Reviewer assignment not found.", 404, "assignment_not_found");

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
