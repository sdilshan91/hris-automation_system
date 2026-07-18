using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Net.Http.Headers;

namespace HRM.Infrastructure.Persistence.Interceptors;

/// <summary>
/// EF Core SaveChanges interceptor that sets audit fields (CreatedAt, UpdatedAt, CreatedBy, UpdatedBy)
/// on all entities that inherit from BaseEntity.
/// </summary>
public sealed class AuditInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _currentUser;
    // ISSUE-006: optional (nullable) so isolated unit/integration construction (`new AuditInterceptor(cu)`)
    // still compiles; the DI container injects the registered IHttpContextAccessor in production. Null (or a
    // null HttpContext) means "no request in scope" — e.g. a Hangfire background job — and is handled safely.
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public AuditInterceptor(ICurrentUser currentUser, IHttpContextAccessor? httpContextAccessor = null)
    {
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        SetAuditFields(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        SetAuditFields(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void SetAuditFields(DbContext? context)
    {
        if (context is null) return;

        var now = DateTime.UtcNow;
        // ISSUE-015: stamp the actor's user UUID (not their email) into CreatedBy/UpdatedBy so these audit
        // columns match the rest of the audit envelope (AuditLog.UserId, the structured audit rows). The
        // "system" sentinel is preserved for background/unauthenticated writes. Applies platform-wide to
        // every BaseEntity (this is the single stamping seam) — see TC-CHR-081 for the employee case.
        var userId = _currentUser.IsAuthenticated ? _currentUser.UserId.ToString() : "system";

        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = userId;
                    if (entry.Entity.Id == Guid.Empty)
                        entry.Entity.Id = BaseEntity.NewUuidV7();
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userId;
                    break;
            }
        }

        StampRequestContext(context);
        StampImpersonationAttribution(context);
    }

    /// <summary>
    /// ISSUE-006: populate <see cref="AuditLog.IpAddress"/> / <see cref="AuditLog.UserAgent"/> from the current
    /// request on any NEWLY-ADDED audit row that didn't set them explicitly. This centralizes the fields for
    /// the many audit-write sites (TenantLifecycleService, EmployeeService, etc.) that don't thread them
    /// through, while never overwriting the values the auth flows already set. Additive and null-safe: when
    /// there is no HttpContext (background jobs) it is a no-op. <see cref="AuditLog"/> is not a
    /// <see cref="BaseEntity"/>, so it is handled here directly.
    /// </summary>
    private void StampRequestContext(DbContext context)
    {
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext is null)
            return;

        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext.Request.Headers[HeaderNames.UserAgent].ToString();

        foreach (var entry in context.ChangeTracker.Entries<AuditLog>())
        {
            if (entry.State != EntityState.Added)
                continue;

            if (string.IsNullOrEmpty(entry.Entity.IpAddress) && !string.IsNullOrEmpty(ip))
                entry.Entity.IpAddress = ip;
            if (string.IsNullOrEmpty(entry.Entity.UserAgent) && !string.IsNullOrEmpty(userAgent))
                entry.Entity.UserAgent = userAgent;
        }
    }

    /// <summary>
    /// US-ADM-003 (FR-3/AC-2): when the current request is operating under an impersonation session, stamp every
    /// NEWLY-ADDED <see cref="AuditLog"/> row with the impersonator's identity + session id so the tenant audit
    /// trail attributes the action to "platform support", not just the impersonated user. Backward-compatible and
    /// additive: when the caller is not impersonating, or a writer already set these fields explicitly, this is a
    /// no-op. <see cref="AuditLog"/> is not a <see cref="BaseEntity"/>, so it is handled here directly.
    /// </summary>
    private void StampImpersonationAttribution(DbContext context)
    {
        if (!_currentUser.IsImpersonating)
            return;

        foreach (var entry in context.ChangeTracker.Entries<AuditLog>())
        {
            if (entry.State != EntityState.Added)
                continue;

            entry.Entity.IsImpersonationAction = true;
            entry.Entity.ImpersonatorUserId ??= _currentUser.ImpersonatorId;
            entry.Entity.ImpersonationSessionId ??= _currentUser.ImpersonationSessionId;
        }
    }
}
