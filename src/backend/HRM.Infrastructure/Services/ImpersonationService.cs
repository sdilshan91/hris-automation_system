using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Impersonation.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-ADM-003: System Admin / System Support tenant-user impersonation. Runs in the system/admin context (no
/// resolved tenant), so it reads cross-tenant data with <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{TEntity}"/>
/// exactly like <c>TenantProvisioningService</c> — every read is explicitly scoped by tenant/user id in the WHERE
/// clause (never a blanket cross-tenant read). The <c>ImpersonationSession</c> is system-level platform data with
/// no tenant query filter (see <c>ImpersonationSessionConfiguration</c>).
///
/// <para><b>NFR-1 audit immutability</b> is deferred platform/infra work (a DB-role UPDATE/DELETE revocation on
/// the audit table, same family as the deferred Postgres RLS, <c>Rls:Enabled=false</c>). Audit remains
/// append-only by code convention — no code path updates/deletes audit rows. <b>NFR-5 traceId</b>: reuses the
/// existing <c>AuditLog.TraceId</c> column when a trace id is available, else null. <b>FR-5 notification</b> is
/// the log-only <see cref="IImpersonationNotificationService"/> seam (real email/in-app delivery deferred to US-NTF).</para>
/// </summary>
public sealed class ImpersonationService : IImpersonationService
{
    /// <summary>NFR-2: the impersonation token / session is capped at 60 minutes.</summary>
    private const int SessionTtlMinutes = 60;

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IJwtService _jwtService;
    private readonly IImpersonationNotificationService _notification;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ImpersonationService> _logger;

    public ImpersonationService(
        AppDbContext db,
        ICurrentUser currentUser,
        IJwtService jwtService,
        IImpersonationNotificationService notification,
        IConfiguration configuration,
        ILogger<ImpersonationService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _jwtService = jwtService;
        _notification = notification;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Result<StartImpersonationResultDto>> StartAsync(
        StartImpersonationInput input, CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure<StartImpersonationResultDto>("Authentication is required.", 401, "unauthenticated");

        var impersonatorId = _currentUser.UserId;
        var reason = (input.Reason ?? string.Empty).Trim();

        // Defence in depth — the validator covers shape, but never trust the caller.
        if (reason.Length is < 10 or > 500)
            return Result.Failure<StartImpersonationResultDto>("The reason must be between 10 and 500 characters.", 400, "reason_invalid");

        // BR-5: the target tenant must exist and not be terminated.
        var tenant = await _db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == input.TargetTenantId && !t.IsDeleted, cancellationToken);
        if (tenant is null)
            return Result.Failure<StartImpersonationResultDto>("The target tenant does not exist.", 404, "tenant_not_found");
        if (tenant.Status is TenantStatus.Terminated or TenantStatus.Terminating)
            return Result.Failure<StartImpersonationResultDto>("Impersonation is not allowed for a terminated tenant.", 403, "tenant_terminated");

        // BR-2: never impersonate into the system/platform tenant.
        var systemTenantId = await GetSystemTenantIdAsync(cancellationToken);
        if (systemTenantId is not null && input.TargetTenantId == systemTenantId.Value)
            return Result.Failure<StartImpersonationResultDto>("System users cannot be impersonated.", 403, "target_is_system");

        // Precondition: the target user must have an ACTIVE membership in the target tenant.
        var membership = await _db.UserTenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                ut => ut.TenantId == input.TargetTenantId
                      && ut.UserId == input.TargetUserId
                      && ut.Status == UserTenantStatus.Active,
                cancellationToken);
        if (membership is null)
            return Result.Failure<StartImpersonationResultDto>("The target user has no active membership in the target tenant.", 404, "membership_not_found");

        var targetUser = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == input.TargetUserId, cancellationToken);
        if (targetUser is null)
            return Result.Failure<StartImpersonationResultDto>("The target user does not exist.", 404, "user_not_found");

        // BR-2: never impersonate a user who is a member of the system/platform tenant (a system admin/support).
        if (systemTenantId is not null)
        {
            var isSystemUser = await _db.UserTenants
                .IgnoreQueryFilters()
                .AnyAsync(ut => ut.UserId == input.TargetUserId && ut.TenantId == systemTenantId.Value, cancellationToken);
            if (isSystemUser)
                return Result.Failure<StartImpersonationResultDto>("System users cannot be impersonated.", 403, "target_is_system");
        }

        // BR-3: only one ACTIVE session per impersonator at a time.
        var hasActive = await _db.ImpersonationSessions
            .AnyAsync(s => s.ImpersonatorUserId == impersonatorId && s.Status == ImpersonationSessionStatus.Active,
                cancellationToken);
        if (hasActive)
            return Result.Failure<StartImpersonationResultDto>(
                "You already have an active impersonation session. End it before starting another.", 409, "session_active");

        // AC-5/AC-6/BR-1: read-only when the initiator is System Support OR the target tenant is suspended.
        var initiatorIsSupport = _currentUser.Roles.Contains(PermissionCatalog.SystemRoles.SystemSupport);
        var isReadOnly = initiatorIsSupport || tenant.Status == TenantStatus.Suspended;

        // The target user's roles + permissions WITHIN the target tenant (the impersonation token must carry the
        // target's authority, not the impersonator's).
        var (roles, permissions) = await GetTargetRolesAndPermissionsAsync(membership.Id, cancellationToken);

        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(SessionTtlMinutes);

        var session = new ImpersonationSession
        {
            Id = BaseEntity.NewUuidV7(),
            ImpersonatorUserId = impersonatorId,
            TargetUserId = input.TargetUserId,
            TargetTenantId = input.TargetTenantId,
            Reason = reason,
            IsReadOnly = isReadOnly,
            Status = ImpersonationSessionStatus.Active,
            StartedAt = now,
            ExpiresAt = expiresAt,
            ActionsCount = 0,
        };
        _db.ImpersonationSessions.Add(session);

        // System audit (AC-2/FR-3) — written with explicit impersonator attribution. This is the system-side
        // "Impersonation.Started" record; the tenant-scoped audit_log entries accrue as the operator acts.
        var startedDetail = JsonSerializer.Serialize(new
        {
            SessionId = session.Id,
            session.TargetTenantId,
            TargetTenantSubdomain = tenant.Subdomain,
            session.TargetUserId,
            TargetUserEmail = targetUser.Email,
            ImpersonatorUserId = impersonatorId,
            ImpersonatorEmail = _currentUser.Email,
            session.IsReadOnly,
            Reason = reason,
            session.StartedAt,
            session.ExpiresAt,
        });
        _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = input.TargetTenantId,
            UserId = impersonatorId,
            EventType = "Impersonation.Started",
            Action = "Impersonation.Started",
            ResourceType = "ImpersonationSession",
            ResourceId = session.Id.ToString(),
            After = startedDetail,
            ImpersonatorUserId = impersonatorId,
            ImpersonationSessionId = session.Id,
            IsImpersonationAction = true,
            CreatedAt = now,
        });

        await _db.SaveChangesAsync(cancellationToken);

        // Mint the impersonation token (AC-1/FR-1/FR-2/NFR-2) for the TARGET user.
        var token = _jwtService.GenerateImpersonationToken(
            targetUser,
            input.TargetTenantId,
            membership.Id,
            roles,
            permissions,
            session.Id,
            impersonatorId,
            reason,
            isReadOnly,
            expiresAt);

        // Tenant-admin notification (AC-4/FR-5) — non-fatal: the session is already committed.
        try
        {
            await _notification.NotifyImpersonationStartedAsync(new ImpersonationStartedNotification(
                session.Id, input.TargetTenantId, tenant.Name, impersonatorId, _currentUser.Email,
                input.TargetUserId, targetUser.Email, reason, isReadOnly, now, expiresAt), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Impersonation session {SessionId} started but the tenant-admin notification dispatch failed.",
                session.Id);
        }

        var baseDomain = _configuration["Platform:BaseDomain"] ?? "yourhrm.com";
        var redirectUrl = $"https://{tenant.Subdomain}.{baseDomain}/?impersonation={session.Id}";

        _logger.LogInformation(
            "Impersonation session {SessionId} started by {Actor} as user {Target} in tenant {Subdomain} " +
            "(readOnly={ReadOnly}), expires {Expiry:o}.",
            session.Id, impersonatorId, input.TargetUserId, tenant.Subdomain, isReadOnly, expiresAt);

        return Result.Success<StartImpersonationResultDto>(new StartImpersonationResultDto(
            session.Id, token, redirectUrl, expiresAt, isReadOnly));
    }

    public async Task<Result<EndImpersonationResultDto>> EndAsync(
        Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _db.ImpersonationSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null)
            return Result.Failure<EndImpersonationResultDto>("The impersonation session does not exist.", 404, "session_not_found");

        // Only the impersonator who owns the session (the actor — read from imp_actor_id when impersonating, else
        // the authenticated system user) may end it.
        var actorId = _currentUser.IsImpersonating ? _currentUser.ImpersonatorId ?? Guid.Empty : _currentUser.UserId;
        if (session.ImpersonatorUserId != actorId)
            return Result.Failure<EndImpersonationResultDto>("You are not the owner of this impersonation session.", 403, "not_session_owner");

        if (session.Status == ImpersonationSessionStatus.Active)
        {
            var now = DateTime.UtcNow;
            session.Status = ImpersonationSessionStatus.Ended;
            session.EndedAt = now;

            var endedDetail = JsonSerializer.Serialize(new
            {
                SessionId = session.Id,
                session.TargetTenantId,
                session.TargetUserId,
                session.ImpersonatorUserId,
                session.ActionsCount,
                EndedAt = now,
            });
            _db.AuditLogs.Add(new AuditLog
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = session.TargetTenantId,
                UserId = session.ImpersonatorUserId,
                EventType = "Impersonation.Ended",
                Action = "Impersonation.Ended",
                ResourceType = "ImpersonationSession",
                ResourceId = session.Id.ToString(),
                After = endedDetail,
                ImpersonatorUserId = session.ImpersonatorUserId,
                ImpersonationSessionId = session.Id,
                IsImpersonationAction = true,
                CreatedAt = now,
            });

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Impersonation session {SessionId} ended by {Actor}; {Count} action(s) recorded.",
                session.Id, actorId, session.ActionsCount);
        }

        return Result.Success<EndImpersonationResultDto>(new EndImpersonationResultDto(
            session.Id, session.Status.ToString(), session.ActionsCount, session.EndedAt));
    }

    public async Task<Result<IReadOnlyList<ImpersonationTargetDto>>> ListTargetsAsync(
        Guid targetTenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == targetTenantId && !t.IsDeleted, cancellationToken);
        if (tenant is null)
            return Result.Failure<IReadOnlyList<ImpersonationTargetDto>>("The target tenant does not exist.", 404, "tenant_not_found");

        var systemTenantId = await GetSystemTenantIdAsync(cancellationToken);

        // BR-2: exclude users who are members of the system/platform tenant.
        var systemUserIds = systemTenantId is null
            ? new HashSet<Guid>()
            : (await _db.UserTenants
                .IgnoreQueryFilters()
                .Where(ut => ut.TenantId == systemTenantId.Value)
                .Select(ut => ut.UserId)
                .ToListAsync(cancellationToken)).ToHashSet();

        // Active memberships in the target tenant. Resolved in steps (no nested-subquery projection) so the
        // shape is identical on InMemory and PostgreSQL.
        var memberships = await _db.UserTenants
            .IgnoreQueryFilters()
            .Where(ut => ut.TenantId == targetTenantId && ut.Status == UserTenantStatus.Active)
            .Select(ut => new { ut.Id, ut.UserId })
            .ToListAsync(cancellationToken);

        var visible = memberships.Where(m => !systemUserIds.Contains(m.UserId)).ToList();
        if (visible.Count == 0)
            return Result.Success<IReadOnlyList<ImpersonationTargetDto>>(new List<ImpersonationTargetDto>());

        var userIds = visible.Select(m => m.UserId).ToList();
        var users = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email, u.DisplayName })
            .ToListAsync(cancellationToken);
        var userById = users.ToDictionary(u => u.Id);

        var membershipIds = visible.Select(m => m.Id).ToList();
        var roleLinks = await _db.UserTenantRoles
            .IgnoreQueryFilters()
            .Where(utr => membershipIds.Contains(utr.UserTenantId))
            .Select(utr => new { utr.UserTenantId, utr.RoleId })
            .ToListAsync(cancellationToken);
        var roleIds = roleLinks.Select(rl => rl.RoleId).Distinct().ToList();
        var roleNameById = (await _db.Roles
            .IgnoreQueryFilters()
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Name })
            .ToListAsync(cancellationToken))
            .ToDictionary(r => r.Id, r => r.Name);

        var targets = visible
            .Select(m =>
            {
                userById.TryGetValue(m.UserId, out var u);
                var roles = roleLinks
                    .Where(rl => rl.UserTenantId == m.Id && roleNameById.ContainsKey(rl.RoleId))
                    .Select(rl => roleNameById[rl.RoleId])
                    .ToList();
                return new ImpersonationTargetDto(
                    m.UserId, u?.Email ?? string.Empty, u?.DisplayName, roles);
            })
            .ToList();

        return Result.Success<IReadOnlyList<ImpersonationTargetDto>>(targets);
    }

    /// <summary>
    /// Resolves the system/platform tenant id (the tenant that hosts the SystemAdmin / System Support roles),
    /// identified by the reserved "platform" subdomain. Cached per request scope is unnecessary — one cheap read.
    /// </summary>
    private async Task<Guid?> GetSystemTenantIdAsync(CancellationToken cancellationToken)
    {
        var subdomain = _configuration["Platform:SystemTenantSubdomain"] ?? "platform";
        return await _db.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.Subdomain == subdomain)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves the target user's role names + flattened permission set within the target tenant (via the
    /// membership → UserTenantRole → Role → RolePermission chain). All reads use IgnoreQueryFilters since the
    /// service runs in the system context, but every read is explicitly scoped by the membership/tenant id.
    /// </summary>
    private async Task<(List<string> Roles, List<string> Permissions)> GetTargetRolesAndPermissionsAsync(
        Guid userTenantId, CancellationToken cancellationToken)
    {
        var roleIds = await _db.UserTenantRoles
            .IgnoreQueryFilters()
            .Where(utr => utr.UserTenantId == userTenantId)
            .Select(utr => utr.RoleId)
            .ToListAsync(cancellationToken);

        if (roleIds.Count == 0)
            return (new List<string>(), new List<string>());

        var roles = await _db.Roles
            .IgnoreQueryFilters()
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Name)
            .ToListAsync(cancellationToken);

        var permissions = await _db.RolePermissions
            .IgnoreQueryFilters()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.Permission)
            .Distinct()
            .ToListAsync(cancellationToken);

        return (roles, permissions);
    }
}
