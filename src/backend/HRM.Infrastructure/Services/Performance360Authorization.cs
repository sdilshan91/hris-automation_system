using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Domain.Authorization;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Shared 360-degree "may this caller MANAGE this reviewee" authorization (BUG-244 / US-PRF-005). The exact
/// four-outcome gate used by the reviewer-CONFIG surface (<see cref="ReviewerAssignmentService"/>) and the
/// results-RELEASE surface (<see cref="Feedback360Service"/>) — kept in ONE place so the two never drift:
/// <list type="number">
///   <item>Caller holds <c>Performance.Review.All</c> (HR) → allowed, unrestricted.</item>
///   <item>Tenant toggle ON AND caller holds <c>Performance.Review.Team</c> AND caller is the DIRECT manager of
///     the target employee → allowed.</item>
///   <item>Tenant toggle ON AND caller holds <c>Performance.Review.Team</c> but is NOT the target's manager →
///     403 <c>not_team_manager</c>.</item>
///   <item>Otherwise (no Review.All, and either the toggle is off or the caller lacks Review.Team) →
///     403 <c>forbidden</c>.</item>
/// </list>
/// The manager check is exact (caller's employee == target's <c>ReportsToEmployeeId</c>) and tenant-scoped via
/// the EF global query filter. Static + parameterized so no service constructor has to change to reuse it.
/// </summary>
internal static class Performance360Authorization
{
    public static async Task<Result> AuthorizeReviewerManagementAsync(
        AppDbContext db, ITenantContext tenantContext, ICurrentUser currentUser,
        Guid targetEmployeeId, CancellationToken cancellationToken)
    {
        if (currentUser.Permissions.Contains(PermissionCatalog.Performance.ReviewAll))
            return Result.Success();

        var managersAllowed = await IsManagerReviewerConfigAllowedAsync(db, tenantContext, cancellationToken);
        var isTeamReviewer = currentUser.Permissions.Contains(PermissionCatalog.Performance.ReviewTeam);

        if (managersAllowed && isTeamReviewer)
        {
            var caller = await db.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == currentUser.UserId, cancellationToken);
            var target = await db.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == targetEmployeeId, cancellationToken);
            if (caller is not null && target is not null && target.ReportsToEmployeeId == caller.Id)
                return Result.Success();

            return Result.Failure(
                "You can only manage 360 reviewers/results for your own direct reports.", 403, "not_team_manager");
        }

        return Result.Failure("You do not have permission to manage 360 reviewers/results.", 403, "forbidden");
    }

    /// <summary>BUG-244: reads the tenant toggle (default true) that lets Team managers manage 360 reviewers/results.</summary>
    private static async Task<bool> IsManagerReviewerConfigAllowedAsync(
        AppDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken)
    {
        // The Tenant row is scoped by Id (it IS the tenant); the only filter is !IsDeleted.
        var allowed = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => (bool?)t.AllowManagerReviewerConfig)
            .FirstOrDefaultAsync(cancellationToken);
        return allowed ?? true;
    }
}
