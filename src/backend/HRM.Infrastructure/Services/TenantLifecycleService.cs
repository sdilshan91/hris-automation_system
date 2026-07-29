using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Tenants.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// System-admin tenant lifecycle service (US-ADM-004): suspend / terminate / reactivate / restore + lifecycle
/// history. Runs in the system/admin context (no resolved tenant), so every read/write uses
/// <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{TEntity}"/> with explicit tenant-id scoping
/// — exactly like <c>TenantProvisioningService</c> / <c>ImpersonationService</c>. Each transition is validated
/// against the pure BR-1 matrix (<see cref="TenantLifecycleTransitions"/>), refuses the system/platform tenant
/// (BR-2), and writes a <c>TenantLifecycleEvent</c> + system <c>AuditLog</c> (FR-7) atomically in one
/// SaveChanges (NFR-4). Notifications go through the log-only seam; deletion scheduling through the optional
/// Hangfire seam (absent in tests).
/// </summary>
public sealed class TenantLifecycleService : ITenantLifecycleService
{
    private const int MinGraceDays = 7;   // BR-4
    private const int MaxGraceDays = 90;  // BR-4
    // US-ADM-004 AC-3/FR-2: default grace period when the caller omits one. The story calls it
    // "plan-configurable"; no per-plan grace-days column is wired on the Tenant/plan model, so the documented
    // 30-day default is used (BUG-002). Wire a plan-configured value here once the billing/plan model carries one.
    private const int DefaultGraceDays = 30;

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantLifecycleNotificationService _notification;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TenantLifecycleService> _logger;
    private readonly ITenantDeletionScheduler? _scheduler; // optional Hangfire seam (null in tests)
    // ISSUE-342: the subdomain-cache invalidation was previously an inline IDistributedCache + private helper here;
    // it now lives in the shared ITenantResolutionCache so the plan-edit sweep and plan-change endpoint reuse the
    // exact same key format. Optional (null in tests that omit it), matching the prior IDistributedCache seam.
    private readonly ITenantResolutionCache? _resolutionCache;

    public TenantLifecycleService(
        AppDbContext db,
        ICurrentUser currentUser,
        ITenantLifecycleNotificationService notification,
        IConfiguration configuration,
        ILogger<TenantLifecycleService> logger,
        ITenantDeletionScheduler? scheduler = null,
        ITenantResolutionCache? resolutionCache = null)
    {
        _db = db;
        _currentUser = currentUser;
        _notification = notification;
        _configuration = configuration;
        _logger = logger;
        _scheduler = scheduler;
        _resolutionCache = resolutionCache;
    }

    private string Actor => _currentUser.IsAuthenticated ? _currentUser.UserId.ToString() : "system";

    // ── Suspend (AC-1 / FR-1) ───────────────────────────────────────────────

    public async Task<Result<TenantLifecycleResultDto>> SuspendAsync(
        SuspendTenantInput input, CancellationToken cancellationToken = default)
    {
        var reason = (input.Reason ?? string.Empty).Trim();
        if (reason.Length is < 10 or > 500)
            return Result.Failure<TenantLifecycleResultDto>("The reason must be between 10 and 500 characters.", 400, "reason_invalid");

        var (tenant, error) = await LoadTransitionableTenantAsync(
            input.TenantId, TenantLifecycleTransition.Suspend, cancellationToken);
        if (error is not null)
            return Result.Failure<TenantLifecycleResultDto>(error.Message, error.StatusCode, error.Code);

        var now = DateTime.UtcNow;
        tenant!.Status = TenantStatus.Suspended;
        tenant.SuspendedAt = now;
        tenant.SuspendedReason = reason;
        tenant.UpdatedAt = now;

        // BR-5: revoke ALL active refresh tokens for the tenant (kills every session).
        await RevokeAllRefreshTokensAsync(tenant.Id, now, cancellationToken);

        var detail = JsonSerializer.Serialize(new { tenant.Subdomain, Reason = reason, SuspendedAt = now });
        WriteLifecycleEvent(tenant.Id, "suspended", detail, now);
        WriteAudit(tenant.Id, "Tenant.Suspended", detail, now);

        await _db.SaveChangesAsync(cancellationToken);

        // FR-9: drop the subdomain-resolution cache so the tenant stops resolving as Active within the TTL
        // window, closing the login-block bypass.
        await InvalidateTenantSubdomainCacheAsync(tenant.Subdomain, cancellationToken);

        await DispatchNotificationAsync(tenant, "suspended", reason, null, cancellationToken);

        _logger.LogInformation("Suspended tenant {Subdomain} ({TenantId}).", tenant.Subdomain, tenant.Id);
        return Ok(tenant, "suspended");
    }

    // ── Terminate (AC-3 / FR-2) ─────────────────────────────────────────────

    public async Task<Result<TenantLifecycleResultDto>> TerminateAsync(
        TerminateTenantInput input, CancellationToken cancellationToken = default)
    {
        var reason = (input.Reason ?? string.Empty).Trim();
        if (reason.Length is < 10 or > 500)
            return Result.Failure<TenantLifecycleResultDto>("The reason must be between 10 and 500 characters.", 400, "reason_invalid");

        // BUG-002: an omitted grace period — null, or the 0 a non-nullable DTO/JSON default deserializes to —
        // applies the plan default. A supplied positive value is range-checked 7-90 (BR-4). (In the HTTP
        // pipeline the validator already rejects a supplied out-of-range value before the service is reached;
        // this keeps the rule intact for direct/service-level callers too.)
        var graceDays = input.GraceDays is > 0 ? input.GraceDays.Value : DefaultGraceDays;
        if (graceDays is < MinGraceDays or > MaxGraceDays)
            return Result.Failure<TenantLifecycleResultDto>(
                $"The grace period must be between {MinGraceDays} and {MaxGraceDays} days.", 400, "grace_days_invalid");

        var (tenant, error) = await LoadTransitionableTenantAsync(
            input.TenantId, TenantLifecycleTransition.Terminate, cancellationToken);
        if (error is not null)
            return Result.Failure<TenantLifecycleResultDto>(error.Message, error.StatusCode, error.Code);

        var now = DateTime.UtcNow;
        var scheduledAt = now.AddDays(graceDays);
        tenant!.Status = TenantStatus.Terminating;
        tenant.TerminationScheduledAt = scheduledAt;
        tenant.UpdatedAt = now;

        // Schedule the deletion + 14d/7d/1d reminders via the Hangfire seam (skipped when no scheduler — tests).
        // Record the job ids so Restore can de-queue them. NFR-3 (maintenance-window scheduling) is a refinement:
        // we fire at TerminationScheduledAt; a configurable low-traffic window is deferred (documented).
        if (_scheduler is not null)
        {
            var jobs = _scheduler.ScheduleDeletionAndReminders(tenant.Id, scheduledAt);
            foreach (var job in jobs)
            {
                _db.TenantScheduledJobs.Add(new TenantScheduledJob
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = tenant.Id,
                    JobId = job.JobId,
                    JobType = job.JobType,
                    ScheduledFor = job.ScheduledFor,
                    CreatedAt = now,
                });
            }
        }

        var detail = JsonSerializer.Serialize(new
        {
            tenant.Subdomain,
            Reason = reason,
            GraceDays = graceDays,
            TerminationScheduledAt = scheduledAt,
        });
        WriteLifecycleEvent(tenant.Id, "termination_initiated", detail, now);
        WriteAudit(tenant.Id, "Tenant.TerminationInitiated", detail, now);

        await _db.SaveChangesAsync(cancellationToken);

        // FR-9: the tenant is now Terminating — invalidate the cached (Active) resolution.
        await InvalidateTenantSubdomainCacheAsync(tenant.Subdomain, cancellationToken);

        await DispatchNotificationAsync(tenant, "termination_initiated", reason, scheduledAt, cancellationToken);

        _logger.LogInformation(
            "Initiated termination of tenant {Subdomain} ({TenantId}); deletion scheduled {ScheduledAt:o}.",
            tenant.Subdomain, tenant.Id, scheduledAt);
        return Ok(tenant, "termination_initiated");
    }

    // ── Reactivate (AC-5 / FR-5) ────────────────────────────────────────────

    public async Task<Result<TenantLifecycleResultDto>> ReactivateAsync(
        ReactivateTenantInput input, CancellationToken cancellationToken = default)
    {
        var (tenant, error) = await LoadTransitionableTenantAsync(
            input.TenantId, TenantLifecycleTransition.Reactivate, cancellationToken);
        if (error is not null)
            return Result.Failure<TenantLifecycleResultDto>(error.Message, error.StatusCode, error.Code);

        var now = DateTime.UtcNow;
        // Default to Active. (A prior PastDue state isn't tracked separately once suspended, so Active is the
        // safe default; a payment-state-aware reactivation is deferred to the billing/dunning work — Phase 2.)
        tenant!.Status = TenantStatus.Active;
        tenant.SuspendedAt = null;
        tenant.SuspendedReason = null;
        tenant.UpdatedAt = now;

        var detail = JsonSerializer.Serialize(new { tenant.Subdomain, ReactivatedAt = now });
        WriteLifecycleEvent(tenant.Id, "reactivated", detail, now);
        WriteAudit(tenant.Id, "Tenant.Reactivated", detail, now);

        await _db.SaveChangesAsync(cancellationToken);

        // FR-9: refresh the cached status so the reactivated tenant resolves as Active immediately.
        await InvalidateTenantSubdomainCacheAsync(tenant.Subdomain, cancellationToken);

        await DispatchNotificationAsync(tenant, "reactivated", null, null, cancellationToken);

        _logger.LogInformation("Reactivated tenant {Subdomain} ({TenantId}).", tenant.Subdomain, tenant.Id);
        return Ok(tenant, "reactivated");
    }

    // ── Restore (AC-6 / FR-6) ───────────────────────────────────────────────

    public async Task<Result<TenantLifecycleResultDto>> RestoreAsync(
        RestoreTenantInput input, CancellationToken cancellationToken = default)
    {
        var (tenant, error) = await LoadTransitionableTenantAsync(
            input.TenantId, TenantLifecycleTransition.Restore, cancellationToken);
        if (error is not null)
            return Result.Failure<TenantLifecycleResultDto>(error.Message, error.StatusCode, error.Code);

        var now = DateTime.UtcNow;
        // BR-1: revert to the prior state — Suspended if the tenant was suspended before termination, else Active.
        var restoredStatus = tenant!.SuspendedAt is not null ? TenantStatus.Suspended : TenantStatus.Active;
        tenant.Status = restoredStatus;
        tenant.TerminationScheduledAt = null;
        tenant.UpdatedAt = now;

        // FR-6: de-queue the scheduled deletion + reminder jobs and forget their ids.
        var scheduledJobs = await _db.TenantScheduledJobs
            .Where(j => j.TenantId == tenant.Id)
            .ToListAsync(cancellationToken);
        if (_scheduler is not null)
        {
            foreach (var job in scheduledJobs)
                _scheduler.Cancel(job.JobId);
        }
        _db.TenantScheduledJobs.RemoveRange(scheduledJobs);

        var detail = JsonSerializer.Serialize(new { tenant.Subdomain, RestoredTo = restoredStatus.ToString(), RestoredAt = now });
        WriteLifecycleEvent(tenant.Id, "restored", detail, now);
        WriteAudit(tenant.Id, "Tenant.Restored", detail, now);

        await _db.SaveChangesAsync(cancellationToken);

        // FR-9: the status changed (back to Suspended/Active) — invalidate the cached resolution.
        await InvalidateTenantSubdomainCacheAsync(tenant.Subdomain, cancellationToken);

        await DispatchNotificationAsync(tenant, "restored", null, null, cancellationToken);

        _logger.LogInformation(
            "Restored tenant {Subdomain} ({TenantId}) to {Status}.", tenant.Subdomain, tenant.Id, restoredStatus);
        return Ok(tenant, "restored");
    }

    // ── Lifecycle history (System Admin + System Support read) ──────────────

    public async Task<Result<IReadOnlyList<TenantLifecycleEventDto>>> GetLifecycleHistoryAsync(
        Guid tenantId, CancellationToken cancellationToken = default)
    {
        var exists = await _db.Tenants
            .IgnoreQueryFilters()
            .AnyAsync(t => t.Id == tenantId, cancellationToken);
        if (!exists)
            return Result.Failure<IReadOnlyList<TenantLifecycleEventDto>>("The tenant does not exist.", 404, "tenant_not_found");

        var events = await _db.TenantLifecycleEvents
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new TenantLifecycleEventDto(
                e.Id, e.TenantId, e.EventType, e.DetailJson, e.CreatedAt, e.CreatedBy))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<TenantLifecycleEventDto>>(events);
    }

    // ── Change plan (ISSUE-341) ───────────────────────────────────────────────

    /// <summary>
    /// ISSUE-341: moves an existing tenant onto a different subscription plan. Validates the target plan code
    /// exists and is active, writes <c>Tenant.PlanId</c>, recomputes <c>Tenant.EnabledModules</c> via the SAME
    /// derivation used at provisioning (<see cref="PlanModules.DeriveTenantModules"/>), keeps the denormalized
    /// <c>Tenant.MaxEmployees</c> snapshot in step with the new plan, invalidates the subdomain-resolution cache,
    /// and writes a tenant-scoped lifecycle event + audit row (consistent with the other lifecycle transitions).
    ///
    /// <para><b>Downgrade behaviour (judgement call — chosen: ALLOW, grandfather, enforce forward).</b> A change
    /// to a smaller plan is permitted and never deletes data. Removed modules are gated going forward by the
    /// module gate; a lower employee/limit cap is enforced going forward by the live limit gates
    /// (<c>EmployeeService.CheckPlanLimitAsync</c> reads the plan live by PlanId and blocks NEW rows once
    /// <c>count &gt;= cap</c>) while existing over-cap rows are grandfathered. This matches the platform's existing
    /// live-limit semantics, cannot silently corrupt state (no destructive action), and avoids building a
    /// separate downgrade-validation feature. If the product later wants to REFUSE a downgrade while a tenant is
    /// over the new cap, that is a deliberate policy addition on top of this — flagged as a DECISION, not assumed.</para>
    /// </summary>
    public async Task<Result<ChangeTenantPlanResultDto>> ChangeTenantPlanAsync(
        ChangeTenantPlanInput input, CancellationToken cancellationToken = default)
    {
        var planCode = (input.PlanCode ?? string.Empty).Trim().ToLowerInvariant();
        if (planCode.Length == 0)
            return Result.Failure<ChangeTenantPlanResultDto>("A plan code is required.", 400, "plan_code_required");

        var tenant = await _db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == input.TenantId && !t.IsDeleted, cancellationToken);
        if (tenant is null)
            return Result.Failure<ChangeTenantPlanResultDto>("The tenant does not exist.", 404, "tenant_not_found");

        // BR-2 (mirrors the lifecycle transitions): never repurpose the system/platform tenant.
        var systemSubdomain = _configuration["Platform:SystemTenantSubdomain"] ?? "platform";
        if (string.Equals(tenant.Subdomain, systemSubdomain, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<ChangeTenantPlanResultDto>(
                "The system tenant's plan cannot be changed.", 403, "system_tenant_protected");

        // Target plan must exist AND be active (an archived plan cannot be assigned) — same rule as provisioning.
        var plan = await _db.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.Code == planCode, cancellationToken);
        if (plan is null || !plan.IsActive)
            return Result.Failure<ChangeTenantPlanResultDto>(
                "The target subscription plan does not exist or is not active.", 400, "plan_invalid");

        var now = DateTime.UtcNow;
        var fromPlan = tenant.PlanId;
        var fromModules = tenant.EnabledModules?.ToList() ?? new List<string>();
        var toModules = PlanModules.DeriveTenantModules(plan.EnabledModules);

        tenant.PlanId = plan.Code;
        tenant.EnabledModules = toModules;
        tenant.MaxEmployees = plan.MaxEmployees; // keep the denormalized snapshot honest (matches provisioning)
        tenant.UpdatedAt = now;

        var detail = JsonSerializer.Serialize(new
        {
            tenant.Subdomain,
            FromPlan = fromPlan,
            ToPlan = plan.Code,
            FromModules = fromModules,
            ToModules = toModules,
        });
        WriteLifecycleEvent(tenant.Id, "plan_changed", detail, now);
        WriteAudit(tenant.Id, "Tenant.PlanChanged", detail, now);

        await _db.SaveChangesAsync(cancellationToken);

        // The cached resolution snapshot carries EnabledModules — drop it so the new entitlement takes effect
        // within the NFR-1 60s slack rather than the 5-min TTL.
        await InvalidateTenantSubdomainCacheAsync(tenant.Subdomain, cancellationToken);

        _logger.LogInformation(
            "Changed tenant {Subdomain} ({TenantId}) plan {FromPlan} -> {ToPlan}.",
            tenant.Subdomain, tenant.Id, fromPlan, plan.Code);

        return Result.Success(new ChangeTenantPlanResultDto(tenant.Id, tenant.Subdomain, plan.Code, toModules));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private sealed record TransitionError(string Message, int StatusCode, string Code);

    /// <summary>
    /// Loads the target tenant (cross-tenant) and validates BR-2 (system tenant) + BR-1/BR-3 (allowed
    /// transition). Returns the tracked tenant on success, or a typed error.
    /// </summary>
    private async Task<(Tenant? Tenant, TransitionError? Error)> LoadTransitionableTenantAsync(
        Guid tenantId, TenantLifecycleTransition transition, CancellationToken cancellationToken)
    {
        var tenant = await _db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted, cancellationToken);
        if (tenant is null)
            return (null, new TransitionError("The tenant does not exist.", 404, "tenant_not_found"));

        // BR-2: never suspend/terminate the system/platform tenant.
        var systemSubdomain = _configuration["Platform:SystemTenantSubdomain"] ?? "platform";
        if (string.Equals(tenant.Subdomain, systemSubdomain, StringComparison.OrdinalIgnoreCase))
            return (null, new TransitionError("The system tenant cannot be suspended or terminated.", 403, "system_tenant_protected"));

        // BR-1/BR-3: the transition must be valid from the current status.
        if (!TenantLifecycleTransitions.IsAllowed(tenant.Status, transition))
            return (null, new TransitionError(
                $"Cannot {transition.ToString().ToLowerInvariant()} a tenant in '{tenant.Status}' status.",
                409, "invalid_transition"));

        return (tenant, null);
    }

    /// <summary>
    /// FR-9: drops the subdomain-resolution cache entry so a status change is visible before the 5-min TTL
    /// elapses. Delegates to the shared <see cref="ITenantResolutionCache"/> (extracted for ISSUE-342 so the
    /// plan-edit sweep and plan-change endpoint reuse the identical key format + best-effort semantics).
    /// </summary>
    private Task InvalidateTenantSubdomainCacheAsync(string subdomain, CancellationToken cancellationToken)
        => _resolutionCache?.InvalidateAsync(subdomain, cancellationToken) ?? Task.CompletedTask;

    private async Task RevokeAllRefreshTokensAsync(Guid tenantId, DateTime now, CancellationToken cancellationToken)
    {
        var tokens = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .Where(rt => rt.TenantId == tenantId && rt.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens)
            token.RevokedAt = now;
    }

    private void WriteLifecycleEvent(Guid tenantId, string eventType, string detailJson, DateTime now)
        => _db.TenantLifecycleEvents.Add(new TenantLifecycleEvent
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            EventType = eventType,
            DetailJson = detailJson,
            CreatedAt = now,
            CreatedBy = Actor,
        });

    private void WriteAudit(Guid tenantId, string action, string detailJson, DateTime now)
        => _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = action,
            Action = action,
            ResourceType = "Tenant",
            ResourceId = tenantId.ToString(),
            After = detailJson,
            CreatedAt = now,
        });

    /// <summary>
    /// Dispatches the lifecycle notification to the tenant's billing email + Tenant Admin users (FR-1/FR-5).
    /// Non-fatal: the transition is already committed, so a stubbed-notification failure never rolls it back.
    /// </summary>
    private async Task DispatchNotificationAsync(
        Tenant tenant, string eventType, string? reason, DateTime? scheduledAt, CancellationToken cancellationToken)
    {
        try
        {
            var adminEmails = await ResolveTenantAdminEmailsAsync(tenant.Id, cancellationToken);
            await _notification.NotifyLifecycleChangeAsync(new TenantLifecycleNotification(
                tenant.Id, tenant.Name, tenant.Subdomain, eventType, reason, scheduledAt,
                tenant.BillingEmail, adminEmails), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Tenant {TenantId} lifecycle '{EventType}' committed but the notification dispatch failed.",
                tenant.Id, eventType);
        }
    }

    /// <summary>
    /// Resolves the email addresses of the tenant's Tenant Owner / Tenant Admin users (FR-1 notification
    /// recipients). Resolved in steps (no nested-subquery projection) so the shape is identical on InMemory
    /// and PostgreSQL — same pattern as ImpersonationService.ListTargetsAsync.
    /// </summary>
    private async Task<IReadOnlyList<string>> ResolveTenantAdminEmailsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var adminRoleIds = await _db.Roles
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId
                        && (r.Name == PermissionCatalog.BuiltInRoles.TenantOwner
                            || r.Name == PermissionCatalog.BuiltInRoles.TenantAdmin))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
        if (adminRoleIds.Count == 0)
            return Array.Empty<string>();

        var membershipIds = await _db.UserTenantRoles
            .IgnoreQueryFilters()
            .Where(utr => adminRoleIds.Contains(utr.RoleId))
            .Select(utr => utr.UserTenantId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (membershipIds.Count == 0)
            return Array.Empty<string>();

        var userIds = await _db.UserTenants
            .IgnoreQueryFilters()
            .Where(ut => membershipIds.Contains(ut.Id) && ut.TenantId == tenantId)
            .Select(ut => ut.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (userIds.Count == 0)
            return Array.Empty<string>();

        return await _db.Users
            .IgnoreQueryFilters()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => u.Email)
            .ToListAsync(cancellationToken);
    }

    private static Result<TenantLifecycleResultDto> Ok(Tenant tenant, string eventType)
        => Result.Success(new TenantLifecycleResultDto(
            tenant.Id, tenant.Subdomain, tenant.Status.ToString(),
            tenant.SuspendedAt, tenant.SuspendedReason, tenant.TerminationScheduledAt, eventType));
}
