using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRM.Infrastructure.Persistence.Interceptors;

/// <summary>
/// EF Core SaveChanges interceptor that sets audit fields (CreatedAt, UpdatedAt, CreatedBy, UpdatedBy)
/// on all entities that inherit from BaseEntity.
/// </summary>
public sealed class AuditInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _currentUser;

    public AuditInterceptor(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
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
        var userId = _currentUser.IsAuthenticated ? _currentUser.Email : "system";

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

        StampImpersonationAttribution(context);
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
