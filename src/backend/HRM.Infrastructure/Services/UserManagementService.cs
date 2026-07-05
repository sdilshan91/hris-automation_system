using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.DTOs;
using HRM.Application.Features.Users.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-ADM-005: Tenant-scoped user + role-assignment management for a Tenant Admin. Runs in the NORMAL
/// resolved-tenant context — isolation (AC-6) is the EF global query filters on UserTenant / Role /
/// UserTenantRole / RefreshToken / UserInvitation, so a Tenant A admin literally cannot load a Tenant B
/// membership (the filter returns nothing → 404, not 403, to avoid existence disclosure). RLS is the
/// deferred platform layer (Rls:Enabled=false); isolation here is fully satisfied by the query filters.
///
/// <para><b>Pending-membership model:</b> a brand-new invitee is NOT given a UserTenant — the pending state
/// IS the <see cref="UserInvitation"/> row (Invited). The global User is found-or-created at invite time so
/// the email is stable, but the active UserTenant + role assignment are created on ACCEPTANCE (that flow is
/// owned by AUTH/onboarding, deferred). The plan user-limit (BR-5) counts active UserTenant memberships PLUS
/// pending invitations against Tenant.MaxEmployees and is enforced only at invite time.</para>
///
/// <para><b>Force-password-reset (AC-5)</b> is the ONE deliberate cross-tenant write: the password is global,
/// so all of the user's refresh tokens across ALL tenants are revoked (IgnoreQueryFilters, scoped strictly by
/// UserId) and PasswordChangedAt is nulled.</para>
/// </summary>
public sealed class UserManagementService : IUserManagementService
{
    private const int InvitationLifetimeHours = 72; // BR-6

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionCache _permissionCache;
    private readonly IUserManagementNotificationService _notifications;
    private readonly ILogger<UserManagementService> _logger;

    public UserManagementService(
        AppDbContext db,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IPermissionCache permissionCache,
        IUserManagementNotificationService notifications,
        ILogger<UserManagementService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _permissionCache = permissionCache;
        _notifications = notifications;
        _logger = logger;
    }

    // ── List users (AC-1 / FR-1) ────────────────────────────────────────────

    public async Task<Result<PagedResult<TenantUserListItemDto>>> ListUsersAsync(
        ListTenantUsersInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure<PagedResult<TenantUserListItemDto>>("Tenant context is not resolved.", 400);

        var page = input.Page < 1 ? 1 : input.Page;
        var pageSize = input.PageSize switch { < 1 => 20, > 100 => 100, _ => input.PageSize };

        // Tenant-scoped by the global query filter on UserTenant; the User/Role joins resolve names.
        var query = _db.UserTenants.AsNoTracking();

        // Status filter (FR-1). A UserTenant only ever has Active/Disabled/Suspended — the "Invited" state is
        // NOT a membership status: it is modeled as a UserInvitation row (no UserTenant), surfaced by
        // ListInvitationsAsync / the user-detail invitations list, never by this membership query. So an
        // unrecognized status value (including "Invited" and any garbage) matches no membership and yields an
        // empty page — mirroring how the RoleId filter returns empty for a non-matching value. It must NOT be
        // silently dropped (the old code fell through to "return ALL users" when the value failed to parse).
        if (!string.IsNullOrWhiteSpace(input.Status))
        {
            query = Enum.TryParse<UserTenantStatus>(input.Status, ignoreCase: true, out var status)
                ? query.Where(ut => ut.Status == status)
                : query.Where(_ => false);
        }

        if (input.RoleId is { } roleId)
        {
            query = query.Where(ut => ut.UserTenantRoles.Any(utr => utr.RoleId == roleId));
        }

        if (!string.IsNullOrWhiteSpace(input.Search))
        {
            var term = input.Search.Trim().ToLower();
            query = query.Where(ut =>
                ut.User.Email.ToLower().Contains(term)
                || (ut.User.DisplayName != null && ut.User.DisplayName.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Project the membership + user + role ids, then resolve role names + linked employee separately
        // (projecting through the filtered Role required-navigation empties the list on the InMemory provider).
        var rows = await query
            .OrderBy(ut => ut.User.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ut => new
            {
                ut.Id,
                ut.UserId,
                ut.User.Email,
                ut.User.DisplayName,
                Status = ut.Status,
                RoleIds = ut.UserTenantRoles.Select(utr => utr.RoleId).ToList(),
            })
            .ToListAsync(cancellationToken);

        var roleNames = await GetRoleNameMapAsync(cancellationToken);
        var userIds = rows.Select(r => r.UserId).ToList();

        var lastLogins = await _db.RefreshTokens.AsNoTracking()
            .Where(rt => userIds.Contains(rt.UserId))
            .GroupBy(rt => rt.UserId)
            .Select(g => new { UserId = g.Key, LastActive = g.Max(rt => (DateTime?)rt.IssuedAt) })
            .ToDictionaryAsync(x => x.UserId, x => x.LastActive, cancellationToken);

        var linkedEmployees = await _db.Employees.AsNoTracking()
            .Where(e => e.UserId != null && userIds.Contains(e.UserId.Value))
            .Select(e => new { UserId = e.UserId!.Value, e.Id })
            .ToDictionaryAsync(x => x.UserId, x => x.Id, cancellationToken);

        var items = rows.Select(r => new TenantUserListItemDto(
            r.Id,
            r.UserId,
            r.Email,
            r.DisplayName,
            r.Status.ToString(),
            r.RoleIds
                .Where(roleNames.ContainsKey)
                .Select(id => new TenantUserRoleDto(id, roleNames[id]))
                .ToList(),
            lastLogins.TryGetValue(r.UserId, out var ll) ? ll : null,
            linkedEmployees.TryGetValue(r.UserId, out var eid) ? eid : null)).ToList();

        return Result.Success<PagedResult<TenantUserListItemDto>>(
            new PagedResult<TenantUserListItemDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
            });
    }

    // ── User detail (FR-6) ──────────────────────────────────────────────────

    public async Task<Result<TenantUserDetailDto>> GetUserDetailAsync(
        Guid userTenantId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure<TenantUserDetailDto>("Tenant context is not resolved.", 400);

        var ut = await _db.UserTenants.AsNoTracking()
            .Where(x => x.Id == userTenantId)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                x.User.Email,
                x.User.DisplayName,
                x.Status,
                RoleIds = x.UserTenantRoles.Select(r => r.RoleId).ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (ut is null)
            return Result.Failure<TenantUserDetailDto>("User membership not found in this tenant.", 404, "not_found");

        var roleNames = await GetRoleNameMapAsync(cancellationToken);

        // Active sessions in THIS tenant (RefreshToken is tenant-filtered).
        var sessions = await _db.RefreshTokens.AsNoTracking()
            .Where(rt => rt.UserId == ut.UserId && rt.RevokedAt == null && rt.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(rt => rt.IssuedAt)
            .Select(rt => new ActiveSessionDto(
                rt.Id, rt.IssuedAt, rt.ExpiresAt, rt.LastActiveAt, rt.UserAgent, rt.IpAddress))
            .ToListAsync(cancellationToken);

        var invitations = await _db.UserInvitations.AsNoTracking()
            .Where(i => i.Email == ut.Email)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        var lastLogin = sessions.Count > 0 ? sessions.Max(s => s.IssuedAt) : (DateTime?)null;

        var linkedEmployeeId = await _db.Employees.AsNoTracking()
            .Where(e => e.UserId == ut.UserId)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var detail = new TenantUserDetailDto(
            ut.Id,
            ut.UserId,
            ut.Email,
            ut.DisplayName,
            ut.Status.ToString(),
            ut.RoleIds.Where(roleNames.ContainsKey).Select(id => new TenantUserRoleDto(id, roleNames[id])).ToList(),
            lastLogin,
            linkedEmployeeId,
            sessions,
            invitations.Select(ToInvitationDto).ToList());

        return Result.Success(detail);
    }

    // ── Invite (AC-2 / FR-2) ────────────────────────────────────────────────

    public async Task<Result<InviteResultDto>> InviteAsync(
        string email, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure<InviteResultDto>("Tenant context is not resolved.", 400);

        // Resolve the valid role set for THIS tenant up front (id-based invite).
        var validRoleIds = await _db.Roles
            .Where(r => r.TenantId == _tenantContext.TenantId && roleIds.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (validRoleIds.Count == 0 || validRoleIds.Count != roleIds.Distinct().Count())
            return Result.Failure<InviteResultDto>("One or more role IDs are invalid for this tenant.", 400, "invalid_role");

        var outcome = await InviteOneAsync(email, validRoleIds, cancellationToken);
        if (outcome.Error is not null)
        {
            // ISSUE-005: audit an over-plan-limit invite denial (BR-5). InviteOneAsync fails before staging
            // the user/invitation, so nothing else is pending — this persists only the denial audit row.
            if (outcome.ErrorCode == "plan_limit_reached")
            {
                WriteInviteDeniedAudit(email, "plan_limit");
                await _db.SaveChangesAsync(cancellationToken);
            }

            return Result.Failure<InviteResultDto>(outcome.Error.Error ?? "Invite failed.", outcome.StatusCode ?? 400, outcome.ErrorCode);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await DispatchInvitationAsync(outcome.Invitation!, outcome.RawToken!, cancellationToken);

        return Result.Success(new InviteResultDto(
            new[] { ToInvitationDto(outcome.Invitation!) },
            Array.Empty<InviteRowErrorDto>()));
    }

    // ── Bulk CSV invite (FR-3) ──────────────────────────────────────────────

    public async Task<Result<InviteResultDto>> BulkInviteAsync(
        IReadOnlyList<BulkInviteRow> rows, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure<InviteResultDto>("Tenant context is not resolved.", 400);

        if (rows.Count == 0)
            return Result.Failure<InviteResultDto>("No rows supplied.", 400);

        // Map role names → ids once for this tenant (CSV references roles by name, FR-3).
        var tenantRoles = await _db.Roles
            .Where(r => r.TenantId == _tenantContext.TenantId)
            .Select(r => new { r.Id, r.Name })
            .ToListAsync(cancellationToken);
        var roleByName = tenantRoles.ToDictionary(r => r.Name, r => r.Id, StringComparer.OrdinalIgnoreCase);

        var created = new List<UserInvitation>();
        var rawTokens = new List<string>();
        var errors = new List<InviteRowErrorDto>();
        var deniedAudits = 0; // ISSUE-005: count of staged plan-limit denial audit rows.

        foreach (var row in rows)
        {
            var email = (row.Email ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(email) || !IsValidEmail(email))
            {
                errors.Add(new InviteRowErrorDto(email, "Invalid email address."));
                continue;
            }

            if (row.RoleNames is null || row.RoleNames.Count == 0)
            {
                errors.Add(new InviteRowErrorDto(email, "At least one role is required."));
                continue;
            }

            var resolvedRoleIds = new List<Guid>();
            var unknownRole = false;
            foreach (var name in row.RoleNames)
            {
                if (roleByName.TryGetValue(name.Trim(), out var rid))
                    resolvedRoleIds.Add(rid);
                else
                {
                    errors.Add(new InviteRowErrorDto(email, $"Unknown role '{name}'."));
                    unknownRole = true;
                    break;
                }
            }
            if (unknownRole)
                continue;

            var outcome = await InviteOneAsync(email, resolvedRoleIds.Distinct().ToList(), cancellationToken);
            if (outcome.Error is not null)
            {
                // ISSUE-005: audit an over-plan-limit invite denial (BR-5). Staged here and persisted by the
                // batch SaveChanges below (which now also runs when the batch produced only denials).
                if (outcome.ErrorCode == "plan_limit_reached")
                {
                    WriteInviteDeniedAudit(email, "plan_limit");
                    deniedAudits++;
                }

                errors.Add(new InviteRowErrorDto(email, outcome.Error.Error!));
                continue;
            }

            created.Add(outcome.Invitation!);
            rawTokens.Add(outcome.RawToken!);
        }

        if (created.Count > 0 || deniedAudits > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            for (var i = 0; i < created.Count; i++)
                await DispatchInvitationAsync(created[i], rawTokens[i], cancellationToken);
        }

        return Result.Success(new InviteResultDto(
            created.Select(ToInvitationDto).ToList(),
            errors));
    }

    /// <summary>
    /// Stages ONE invitation (no SaveChanges): find-or-create the global user, reject an existing active
    /// member of this tenant, enforce the plan user-limit (BR-5), and add the UserInvitation + (for a
    /// brand-new user) the global User. The plan-limit count includes already-staged adds in this unit of
    /// work so a bulk run cannot overshoot the cap.
    /// </summary>
    private async Task<InviteOneOutcome> InviteOneAsync(
        string rawEmail, List<Guid> roleIds, CancellationToken cancellationToken)
    {
        var email = rawEmail.Trim().ToLowerInvariant();

        // Already an ACTIVE member of this tenant? (AC-2: don't re-invite an active member.)
        var alreadyMember = await _db.UserTenants
            .AnyAsync(ut => ut.User.Email == email && ut.Status == UserTenantStatus.Active, cancellationToken);
        if (alreadyMember)
            return InviteOneOutcome.Fail("This user is already an active member of this tenant.", 409, "already_member");

        // Already has a pending invitation? (avoid duplicates.)
        var alreadyInvited = await _db.UserInvitations
            .AnyAsync(i => i.Email == email && i.Status == InvitationStatus.Invited, cancellationToken);
        if (alreadyInvited)
            return InviteOneOutcome.Fail("This user already has a pending invitation.", 409, "already_invited");

        // Plan user-limit (BR-5): active memberships + pending invitations vs Tenant.MaxEmployees.
        var tenant = await _db.Tenants
            .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId, cancellationToken);
        if (tenant is null)
            return InviteOneOutcome.Fail("Tenant not found.", 404, "not_found");

        if (tenant.MaxEmployees is { } cap)
        {
            var activeCount = await _db.UserTenants
                .CountAsync(ut => ut.Status == UserTenantStatus.Active, cancellationToken);
            var pendingCount = await _db.UserInvitations
                .CountAsync(i => i.Status == InvitationStatus.Invited, cancellationToken);
            // Count adds already staged in THIS unit of work (bulk safety).
            var stagedPending = _db.ChangeTracker.Entries<UserInvitation>()
                .Count(e => e.State == EntityState.Added);

            if (activeCount + pendingCount + stagedPending >= cap)
                return InviteOneOutcome.Fail("User limit reached for your plan.", 409, "plan_limit_reached");
        }

        // Find-or-create the global user (no duplicate — AC-2). User table is global → IgnoreQueryFilters
        // is not needed (it has no tenant filter), but query by email directly.
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null)
        {
            user = new User
            {
                Id = BaseEntity.NewUuidV7(),
                Email = email,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            };
            _db.Users.Add(user);
        }

        var (rawToken, tokenHash) = GenerateToken();
        var now = DateTime.UtcNow;
        var invitation = new UserInvitation
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            Email = email,
            TokenHash = tokenHash,
            Status = InvitationStatus.Invited,
            ExpiresAt = now.AddHours(InvitationLifetimeHours),
            InvitedByUserId = _currentUser.UserId,
            InvitedRoleIds = roleIds,
            CreatedAt = now,
            CreatedBy = _currentUser.Email,
        };
        _db.UserInvitations.Add(invitation);

        WriteAudit("User.Invited", "UserInvitation", invitation.Id, before: null, after: new
        {
            invitation.Email,
            RoleIds = roleIds,
            invitation.ExpiresAt,
        });

        return InviteOneOutcome.Ok(invitation, rawToken);
    }

    // ── Edit roles (AC-3 / FR-4, BR-2) ──────────────────────────────────────

    public async Task<Result> EditRolesAsync(
        Guid userTenantId, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var userTenant = await _db.UserTenants
            .Where(ut => ut.Id == userTenantId)
            .Include(ut => ut.UserTenantRoles)
            .FirstOrDefaultAsync(cancellationToken);

        if (userTenant is null)
            return Result.Failure("User membership not found in this tenant.", 404, "not_found");

        var validRoles = await _db.Roles
            .Where(r => r.TenantId == _tenantContext.TenantId && roleIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        if (validRoles.Count != roleIds.Distinct().Count())
            return Result.Failure("One or more role IDs are invalid for this tenant.", 400, "invalid_role");

        // BR-2: a Tenant Admin cannot REMOVE the Tenant Owner role from an owner.
        var tenantOwnerRole = await _db.Roles
            .FirstOrDefaultAsync(
                r => r.TenantId == _tenantContext.TenantId && r.Name == PermissionCatalog.BuiltInRoles.TenantOwner,
                cancellationToken);

        var beforeRoleIds = userTenant.UserTenantRoles.Select(utr => utr.RoleId).OrderBy(g => g).ToList();

        if (tenantOwnerRole is not null)
        {
            var currentlyOwner = beforeRoleIds.Contains(tenantOwnerRole.Id);
            var newSetOwner = roleIds.Contains(tenantOwnerRole.Id);
            if (currentlyOwner && !newSetOwner)
            {
                // ISSUE-005: a BR-2 denial (owner-strip) must leave a security-audit trail, not just fail
                // silently. Nothing has been mutated yet on this path, so this SaveChanges persists only the
                // denial audit row (actor = the requesting admin, via WriteAudit's _currentUser).
                WriteAudit("User.RoleChangeDenied", "UserTenant", userTenant.Id,
                    before: new { RoleIds = beforeRoleIds },
                    after: new
                    {
                        AttemptedRoleIds = roleIds.Distinct().OrderBy(g => g).ToList(),
                        Reason = "owner_role_protected",
                    });
                await _db.SaveChangesAsync(cancellationToken);

                return Result.Failure(
                    "The Tenant Owner role cannot be removed. Transfer ownership first.", 400, "owner_role_protected");
            }
        }

        // Replace the role set.
        _db.UserTenantRoles.RemoveRange(userTenant.UserTenantRoles);
        var now = DateTime.UtcNow;
        foreach (var roleId in roleIds.Distinct())
        {
            _db.UserTenantRoles.Add(new UserTenantRole
            {
                UserTenantId = userTenantId,
                RoleId = roleId,
                AssignedAt = now,
                AssignedBy = _currentUser.Email,
            });
        }
        userTenant.UpdatedAt = now;

        WriteAudit("User.RolesChanged", "UserTenant", userTenant.Id,
            before: new { RoleIds = beforeRoleIds },
            after: new { RoleIds = roleIds.Distinct().OrderBy(g => g).ToList() });

        await _db.SaveChangesAsync(cancellationToken);
        await _permissionCache.InvalidateAsync(_tenantContext.TenantId, userTenant.UserId, cancellationToken);

        return Result.Success();
    }

    // ── Deactivate (AC-4, BR-3) ─────────────────────────────────────────────

    public async Task<Result> DeactivateAsync(Guid userTenantId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var userTenant = await _db.UserTenants
            .FirstOrDefaultAsync(ut => ut.Id == userTenantId, cancellationToken);

        if (userTenant is null)
            return Result.Failure("User membership not found in this tenant.", 404, "not_found");

        // BR-3: cannot deactivate your OWN membership.
        if (userTenant.UserId == _currentUser.UserId)
        {
            // ISSUE-005: audit the DENIED self-deactivation before rejecting. Nothing has been mutated yet,
            // so this SaveChanges persists only the denial audit row.
            WriteAudit("User.DeactivateDenied", "UserTenant", userTenant.Id,
                before: new { Status = userTenant.Status.ToString() },
                after: new { Reason = "self_deactivation" });
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Failure("You cannot deactivate your own account.", 400, "self_deactivation");
        }

        var beforeStatus = userTenant.Status.ToString();
        userTenant.Status = UserTenantStatus.Disabled;
        userTenant.UpdatedAt = DateTime.UtcNow;

        // FR-5: revoke this user's refresh tokens in THIS tenant only (RefreshToken is tenant-filtered).
        var revoked = await RevokeTenantSessionsAsync(userTenant.UserId, cancellationToken);

        WriteAudit("User.Deactivated", "UserTenant", userTenant.Id,
            before: new { Status = beforeStatus },
            after: new { Status = UserTenantStatus.Disabled.ToString(), SessionsRevoked = revoked });

        await _db.SaveChangesAsync(cancellationToken);
        await _permissionCache.InvalidateAsync(_tenantContext.TenantId, userTenant.UserId, cancellationToken);

        return Result.Success();
    }

    // ── Force password reset (AC-5) — cross-tenant by design ─────────────────

    public async Task<Result> ForcePasswordResetAsync(Guid userTenantId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        // Resolve the membership tenant-scoped first (admin can only target a member of their tenant).
        var userTenant = await _db.UserTenants
            .FirstOrDefaultAsync(ut => ut.Id == userTenantId, cancellationToken);
        if (userTenant is null)
            return Result.Failure("User membership not found in this tenant.", 404, "not_found");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userTenant.UserId, cancellationToken);
        if (user is null)
            return Result.Failure("User not found.", 404, "not_found");

        // Deliberate cross-tenant write: the password is global. Revoke ALL of the user's refresh tokens
        // across ALL tenants, scoped strictly by UserId.
        var now = DateTime.UtcNow;
        var allTokens = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .Where(rt => rt.UserId == user.Id && rt.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in allTokens)
            token.RevokedAt = now;

        user.PasswordChangedAt = null; // force reset (AC-5)
        user.UpdatedAt = now;

        WriteAudit("User.ForcePasswordReset", "User", user.Id,
            before: null,
            after: new { TokensRevoked = allTokens.Count });

        await _db.SaveChangesAsync(cancellationToken);

        await _notifications.SendPasswordResetAsync(
            new PasswordResetEmailMessage(_tenantContext.TenantId, _tenantContext.Subdomain, user.Email),
            cancellationToken);

        return Result.Success();
    }

    // ── End all sessions in this tenant (FR-5) ───────────────────────────────

    public async Task<Result> EndAllSessionsAsync(Guid userTenantId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var userTenant = await _db.UserTenants
            .FirstOrDefaultAsync(ut => ut.Id == userTenantId, cancellationToken);
        if (userTenant is null)
            return Result.Failure("User membership not found in this tenant.", 404, "not_found");

        var revoked = await RevokeTenantSessionsAsync(userTenant.UserId, cancellationToken);

        WriteAudit("User.SessionsEnded", "UserTenant", userTenant.Id,
            before: null, after: new { SessionsRevoked = revoked });

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ── Invitations: list / resend / revoke ──────────────────────────────────

    public async Task<Result<IReadOnlyList<InvitationDto>>> ListInvitationsAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure<IReadOnlyList<InvitationDto>>("Tenant context is not resolved.", 400);

        var invitations = await _db.UserInvitations.AsNoTracking()
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<InvitationDto>>(invitations.Select(ToInvitationDto).ToList());
    }

    public async Task<Result<InvitationDto>> ResendInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure<InvitationDto>("Tenant context is not resolved.", 400);

        var invitation = await _db.UserInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId, cancellationToken);
        if (invitation is null)
            return Result.Failure<InvitationDto>("Invitation not found.", 404, "not_found");

        if (invitation.Status is InvitationStatus.Accepted or InvitationStatus.Revoked)
            return Result.Failure<InvitationDto>("This invitation can no longer be resent.", 409, "invitation_closed");

        // BR-6: new token + fresh 72h expiry; an expired-by-time invitation is re-opened to Invited.
        var (rawToken, tokenHash) = GenerateToken();
        var now = DateTime.UtcNow;
        invitation.TokenHash = tokenHash;
        invitation.Status = InvitationStatus.Invited;
        invitation.ExpiresAt = now.AddHours(InvitationLifetimeHours);
        invitation.UpdatedAt = now;
        invitation.UpdatedBy = _currentUser.Email;

        WriteAudit("User.InvitationResent", "UserInvitation", invitation.Id,
            before: null, after: new { invitation.Email, invitation.ExpiresAt });

        await _db.SaveChangesAsync(cancellationToken);
        await DispatchInvitationAsync(invitation, rawToken, cancellationToken);

        return Result.Success(ToInvitationDto(invitation));
    }

    public async Task<Result> RevokeInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var invitation = await _db.UserInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId, cancellationToken);
        if (invitation is null)
            return Result.Failure("Invitation not found.", 404, "not_found");

        if (invitation.Status != InvitationStatus.Invited)
            return Result.Failure("Only a pending invitation can be revoked.", 409, "invitation_not_pending");

        invitation.Status = InvitationStatus.Revoked;
        invitation.UpdatedAt = DateTime.UtcNow;
        invitation.UpdatedBy = _currentUser.Email;

        WriteAudit("User.InvitationRevoked", "UserInvitation", invitation.Id,
            before: new { Status = InvitationStatus.Invited.ToString() },
            after: new { Status = InvitationStatus.Revoked.ToString() });

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Dictionary<Guid, string>> GetRoleNameMapAsync(CancellationToken cancellationToken)
        => await _db.Roles.AsNoTracking()
            .Where(r => r.TenantId == _tenantContext.TenantId)
            .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

    private async Task<int> RevokeTenantSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        // RefreshToken is tenant-filtered, so this only touches THIS tenant's tokens (FR-5 scope, AC-4).
        var tokens = await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens)
            token.RevokedAt = now;
        return tokens.Count;
    }

    private async Task DispatchInvitationAsync(UserInvitation invitation, string rawToken, CancellationToken cancellationToken)
    {
        try
        {
            await _notifications.SendInvitationAsync(new UserInvitationEmailMessage(
                _tenantContext.TenantId,
                _tenantContext.Subdomain,
                _tenantContext.Subdomain,
                invitation.Email,
                rawToken,
                invitation.ExpiresAt), cancellationToken);
        }
        catch (Exception ex)
        {
            // Non-fatal: the invitation row is committed and can be resent (§ partial-failure).
            _logger.LogError(ex, "Invitation {InvitationId} created but the email dispatch failed; it can be resent.",
                invitation.Id);
        }
    }

    /// <summary>
    /// ISSUE-005: security-audit row for a DENIED invite (BR-5 plan-limit). No invitation row is created, so
    /// the resource id is <see cref="Guid.Empty"/> and the target email + reason are recorded in the detail.
    /// </summary>
    private void WriteInviteDeniedAudit(string email, string reason)
        => WriteAudit("User.InviteDenied", "UserInvitation", Guid.Empty,
            before: null, after: new { Email = email, Reason = reason });

    private void WriteAudit(string action, string resourceType, Guid resourceId, object? before, object? after)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = action,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId.ToString(),
            Before = before is null ? null : JsonSerializer.Serialize(before),
            After = after is null ? null : JsonSerializer.Serialize(after),
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static (string RawToken, string TokenHash) GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var raw = Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        return (raw, hash);
    }

    private static bool IsValidEmail(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 && at < email.Length - 1 && email.IndexOf('.', at) > at;
    }

    private static InvitationDto ToInvitationDto(UserInvitation i) => new(
        i.Id,
        i.Email,
        i.Status.ToString(),
        i.InvitedRoleIds,
        i.CreatedAt,
        i.ExpiresAt,
        i.Status == InvitationStatus.Invited && i.ExpiresAt <= DateTime.UtcNow);

    private sealed record InviteOneOutcome(
        UserInvitation? Invitation,
        string? RawToken,
        Result? Error,
        int? StatusCode,
        string? ErrorCode)
    {
        public static InviteOneOutcome Ok(UserInvitation invitation, string rawToken)
            => new(invitation, rawToken, null, null, null);

        public static InviteOneOutcome Fail(string error, int statusCode, string errorCode)
            => new(null, null, Result.Failure(error, statusCode, errorCode), statusCode, errorCode);
    }
}
