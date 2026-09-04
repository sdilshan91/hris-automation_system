using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.SubscriptionPlans.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-ADM-009: System-admin subscription-plan management. Runs in the system/admin context (no resolved tenant),
/// so cross-tenant reads use <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{T}"/> exactly like
/// <c>TenantProvisioningService</c>/<c>ImpersonationService</c>. <see cref="SubscriptionPlan"/> and
/// <see cref="PlanLimitOverride"/> are platform tables with NO tenant query filter, so they are read directly.
///
/// <para>Key rules: code is UNIQUE + IMMUTABLE (FR-3/BR-5 — create checks uniqueness, update never touches it);
/// archive sets IsActive=false (AC-4); delete is REJECTED when any tenant references the plan code (FR-7/BR-5);
/// every write is audited to the system audit log (NFR-3).</para>
///
/// <para><b>Which edits need propagating, and which do not.</b> MOST numeric limits are read live from the plan
/// at runtime via <see cref="PlanLimitLookup"/>, so editing them benefits existing tenants immediately (AC-3)
/// and needs no sweep. THREE fields are different because they are DENORMALIZED onto every tenant row and read
/// from there: <c>EnabledModules</c> (the module gate reads the tenant snapshot — ISSUE-342),
/// <c>MaxEmployees</c>, and <c>AuditLogRetentionDays</c> (<c>AuditLogPurgeService</c> reads the tenant column
/// RAW, with no plan lookup at all — ISSUE-474). A change to any of those three must be swept out to the
/// tenants on the plan; see <see cref="RecomputeTenantsOnPlanAsync"/>.</para>
/// </summary>
public sealed class SubscriptionPlanService : ISubscriptionPlanService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<SubscriptionPlanService> _logger;
    // ISSUE-342: drops the resolution-cache entry of every tenant whose EnabledModules snapshot the plan edit
    // recomputed, so the new entitlement is picked up within NFR-1's 60s window rather than the 5-min TTL.
    // Optional (null in isolated tests that omit it), matching the same optional-seam pattern used by
    // TenantLifecycleService; DI injects the shared implementation in production.
    private readonly ITenantResolutionCache? _resolutionCache;

    public SubscriptionPlanService(
        AppDbContext db, ICurrentUser currentUser, ILogger<SubscriptionPlanService> logger,
        ITenantResolutionCache? resolutionCache = null)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
        _resolutionCache = resolutionCache;
    }

    private Guid? ActorId => _currentUser.IsAuthenticated ? _currentUser.UserId : null;

    // ── List (AC-1/FR-5) ──────────────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<PlanListItemDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _db.SubscriptionPlans
            .OrderBy(p => p.PriceMonthly).ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);

        // ACTIVE tenant count per plan: tenants referencing the plan code that are not deleted and not
        // terminated/terminating (those no longer "occupy" the plan). System context ⇒ IgnoreQueryFilters.
        var counts = await CountActiveTenantsByPlanAsync(cancellationToken);

        var items = plans
            .Select(p => new PlanListItemDto(
                p.Id, p.Code, p.Name, p.PriceMonthly, p.PriceYearly, p.Currency,
                p.IsPublic, p.IsActive, counts.GetValueOrDefault(p.Code, 0)))
            .ToList();

        return Result.Success<IReadOnlyList<PlanListItemDto>>(items);
    }

    // ── Get (AC-2/FR-2) ───────────────────────────────────────────────────────

    public async Task<Result<PlanDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (plan is null)
            return Result.Failure<PlanDetailDto>("The subscription plan does not exist.", 404, "plan_not_found");

        var count = await CountActiveTenantsForCodeAsync(plan.Code, cancellationToken);
        return Result.Success(ToDetailDto(plan, count));
    }

    // ── Create (AC-2/FR-2/FR-3/FR-6) ──────────────────────────────────────────

    public async Task<Result<CreatePlanResultDto>> CreateAsync(
        string code, PlanEditableFields fields, CancellationToken cancellationToken = default)
    {
        code = (code ?? string.Empty).Trim().ToLowerInvariant();

        var moduleError = ValidateModules(fields.EnabledModules);
        if (moduleError is not null)
            return Result.Failure<CreatePlanResultDto>(moduleError, 400, "module_invalid");

        // FR-3: code must be unique (case-insensitive; codes are lowercased).
        var codeTaken = await _db.SubscriptionPlans.AnyAsync(p => p.Code == code, cancellationToken);
        if (codeTaken)
            return Result.Failure<CreatePlanResultDto>("A plan with this code already exists.", 409, "code_taken");

        var now = DateTime.UtcNow;
        var plan = new SubscriptionPlan
        {
            Id = BaseEntity.NewUuidV7(),
            Code = code,
            CreatedAt = now,
        };
        ApplyEditableFields(plan, fields);
        _db.SubscriptionPlans.Add(plan);

        WriteAudit("Plan.Created", plan.Id, before: null, after: ToAuditState(plan), now);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created subscription plan {Code} ({PlanId}).", plan.Code, plan.Id);
        return Result.Success(new CreatePlanResultDto(plan.Id, plan.Code));
    }

    // ── Update (AC-3) — code immutable (FR-3/BR-5) ────────────────────────────

    public async Task<Result> UpdateAsync(
        Guid id, PlanEditableFields fields, CancellationToken cancellationToken = default)
    {
        var moduleError = ValidateModules(fields.EnabledModules);
        if (moduleError is not null)
            return Result.Failure(moduleError, 400, "module_invalid");

        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (plan is null)
            return Result.Failure("The subscription plan does not exist.", 404, "plan_not_found");

        var before = ToAuditState(plan);
        // Capture the normalized module set BEFORE the edit so we only sweep when it actually changed — an
        // unrelated edit (price, name, a live-read numeric limit) must not churn every tenant (ISSUE-342).
        var modulesBefore = plan.EnabledModules.ToList();
        // ISSUE-474: the two limits that are DENORMALIZED onto every tenant, captured for the same reason.
        // Every other numeric limit really is read live off the plan, so only these two need sweeping.
        var maxEmployeesBefore = plan.MaxEmployees;
        var retentionDaysBefore = plan.AuditLogRetentionDays;

        ApplyEditableFields(plan, fields); // never touches Code (immutable, FR-3/BR-5)
        plan.UpdatedAt = DateTime.UtcNow;

        // ISSUE-342: propagate a module-list change to every tenant on this plan. The recomputed tenant snapshots
        // are staged into the SAME change tracker and committed atomically with the plan edit below (no window in
        // which a tenant's entitlement disagrees with its plan). Order-insensitive set comparison.
        var moduleListChanged = !new HashSet<string>(modulesBefore, StringComparer.Ordinal)
            .SetEquals(plan.EnabledModules);

        // ISSUE-474: a limit-only edit must sweep too. Gating the sweep on the MODULE list alone meant the
        // exact scenario in the finding — raise audit retention from 90 to 365 days, touching no module —
        // never ran the sweep at all, so re-stamping inside it would have fixed nothing. AuditLogPurgeService
        // reads Tenant.AuditLogRetentionDays RAW, with no plan lookup, so the daily purge job would keep
        // destroying audit history on the old 90-day window: irreversible data loss against a compliance
        // setting the operator believes they just changed.
        var snapshotLimitsChanged =
            plan.MaxEmployees != maxEmployeesBefore || plan.AuditLogRetentionDays != retentionDaysBefore;

        IReadOnlyList<string> affectedSubdomains = Array.Empty<string>();
        if (moduleListChanged || snapshotLimitsChanged)
            affectedSubdomains = await RecomputeTenantsOnPlanAsync(plan, cancellationToken);

        WriteAudit("Plan.Updated", plan.Id, before, after: ToAuditState(plan), plan.UpdatedAt.Value);
        await _db.SaveChangesAsync(cancellationToken); // plan edit + swept tenant snapshots commit together

        // Best-effort, post-commit: invalidate the affected tenants' resolution-cache entries (never throws —
        // a miss/outage just falls back to the DB within the TTL window).
        if (affectedSubdomains.Count > 0 && _resolutionCache is not null)
            await _resolutionCache.InvalidateManyAsync(affectedSubdomains, cancellationToken);

        _logger.LogInformation(
            "Updated subscription plan {Code} ({PlanId}); recomputed {TenantCount} tenant(s) "
            + "(moduleListChanged={ModuleListChanged}, snapshotLimitsChanged={SnapshotLimitsChanged}).",
            plan.Code, plan.Id, affectedSubdomains.Count, moduleListChanged, snapshotLimitsChanged);
        return Result.Success();
    }

    // ── Archive (AC-4) ────────────────────────────────────────────────────────

    public async Task<Result> ArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (plan is null)
            return Result.Failure("The subscription plan does not exist.", 404, "plan_not_found");

        if (!plan.IsActive)
            return Result.Success(); // idempotent

        var before = ToAuditState(plan);
        plan.IsActive = false;
        plan.UpdatedAt = DateTime.UtcNow;

        WriteAudit("Plan.Archived", plan.Id, before, after: ToAuditState(plan), plan.UpdatedAt.Value);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Archived subscription plan {Code} ({PlanId}).", plan.Code, plan.Id);
        return Result.Success();
    }

    // ── Delete (FR-7/BR-5) ────────────────────────────────────────────────────

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (plan is null)
            return Result.Failure("The subscription plan does not exist.", 404, "plan_not_found");

        // FR-7/BR-5: refuse delete if ANY tenant references the code — including terminated tenants whose rows
        // are retained. IgnoreQueryFilters so the (soft-delete) global filter does not hide retained rows.
        var referenced = await _db.Tenants
            .IgnoreQueryFilters()
            .AnyAsync(t => t.PlanId == plan.Code, cancellationToken);
        if (referenced)
            return Result.Failure(
                "This plan is referenced by one or more tenants and cannot be deleted. Archive it instead.",
                409, "plan_referenced");

        var before = ToAuditState(plan);
        _db.SubscriptionPlans.Remove(plan);

        WriteAudit("Plan.Deleted", plan.Id, before, after: null, DateTime.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted subscription plan {Code} ({PlanId}).", plan.Code, plan.Id);
        return Result.Success();
    }

    // ── Plan limit overrides (AC-5/FR-4) ──────────────────────────────────────

    public async Task<Result<IReadOnlyList<PlanLimitOverrideDto>>> ListOverridesAsync(
        Guid tenantId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.PlanLimitOverrides
            .Where(o => o.TenantId == tenantId)
            .OrderBy(o => o.LimitKey)
            .ToListAsync(cancellationToken);

        var dtos = rows.Select(ToOverrideDto).ToList();
        return Result.Success<IReadOnlyList<PlanLimitOverrideDto>>(dtos);
    }

    public async Task<Result<PlanLimitOverrideDto>> UpsertOverrideAsync(
        Guid tenantId, string limitKey, long? value, DateTime? expiresAt,
        CancellationToken cancellationToken = default)
    {
        limitKey = (limitKey ?? string.Empty).Trim();
        if (!PlanLimitKeys.IsValid(limitKey))
            return Result.Failure<PlanLimitOverrideDto>("Unknown plan-limit key.", 400, "limit_key_invalid");

        // The override targets a real tenant (system context ⇒ IgnoreQueryFilters).
        var tenantExists = await _db.Tenants
            .IgnoreQueryFilters()
            .AnyAsync(t => t.Id == tenantId, cancellationToken);
        if (!tenantExists)
            return Result.Failure<PlanLimitOverrideDto>("The target tenant does not exist.", 404, "tenant_not_found");

        var now = DateTime.UtcNow;
        var existing = await _db.PlanLimitOverrides
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.LimitKey == limitKey, cancellationToken);

        string action;
        string? before;
        if (existing is null)
        {
            existing = new PlanLimitOverride
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                LimitKey = limitKey,
                Value = value,
                ExpiresAt = expiresAt,
                CreatedAt = now,
            };
            _db.PlanLimitOverrides.Add(existing);
            action = "Plan.OverrideCreated";
            before = null;
        }
        else
        {
            before = ToOverrideAuditState(existing);
            existing.Value = value;
            existing.ExpiresAt = expiresAt;
            existing.UpdatedAt = now;
            action = "Plan.OverrideUpdated";
        }

        WriteAudit(action, existing.Id, before, after: ToOverrideAuditState(existing), now, tenantId);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Upserted plan-limit override {LimitKey}={Value} for tenant {TenantId}.", limitKey, value, tenantId);
        return Result.Success(ToOverrideDto(existing));
    }

    public async Task<Result> RemoveOverrideAsync(Guid overrideId, CancellationToken cancellationToken = default)
    {
        var row = await _db.PlanLimitOverrides.FirstOrDefaultAsync(o => o.Id == overrideId, cancellationToken);
        if (row is null)
            return Result.Failure("The plan-limit override does not exist.", 404, "override_not_found");

        var before = ToOverrideAuditState(row);
        var tenantId = row.TenantId;
        _db.PlanLimitOverrides.Remove(row);

        WriteAudit("Plan.OverrideRemoved", overrideId, before, after: null, DateTime.UtcNow, tenantId);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Removed plan-limit override {OverrideId} for tenant {TenantId}.", overrideId, tenantId);
        return Result.Success();
    }

    // ── ISSUE-342: plan-edit module propagation sweep ───────────────────────────

    /// <summary>
    /// Recomputes and re-stamps the tenant snapshots this plan owns — <c>EnabledModules</c>, <c>MaxEmployees</c>
    /// and <c>AuditLogRetentionDays</c> — for every non-deleted tenant on this plan, using the
    /// SAME derivation as provisioning + the plan-change endpoint (<see cref="PlanModules.DeriveTenantModules"/>)
    /// so the three call sites cannot drift. Tenants join a plan by STRING CODE
    /// (<c>Tenant.PlanId == plan.Code</c> — there is no FK), and this runs in the system context, so tenants are
    /// read cross-tenant with <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{T}"/>. Returns the
    /// affected subdomains for the post-commit cache invalidation.
    ///
    /// <para><b>Fail-open on a dangling PlanId:</b> the sweep is keyed by <c>plan.Code</c>, so a tenant whose
    /// <c>PlanId</c> points at a plan that does not exist (e.g. the seeded <c>"default"</c>, which is never
    /// seeded as a plan row) is simply never matched and its modules are left untouched — it is never crashed on
    /// nor stripped, matching every other tenant→plan call site.</para>
    ///
    /// <para><b>Judgement call — synchronous, in-transaction sweep (chosen over a Hangfire job).</b> The tenant
    /// snapshots are staged into the same <c>SaveChanges</c> as the plan edit, so the plan row and every tenant
    /// entitlement commit atomically (no partial/eventually-consistent state). NFR-1 permits 60s propagation and
    /// at current scale (tens–hundreds of tenants per plan) the added write latency is negligible. If a single
    /// plan ever spans thousands of tenants, move this to a batched Hangfire job — the derivation + cache seams
    /// are already shared, so that move is local to this method and its caller.</para>
    ///
    /// <para><b>ISSUE-474 — why the LIMIT columns are re-stamped here too, not just the modules.</b> This sweep
    /// used to write <c>EnabledModules</c> and <c>UpdatedAt</c> only, while
    /// <c>TenantLifecycleService.ChangeTenantPlanAsync</c> re-stamped <c>MaxEmployees</c> and
    /// <c>AuditLogRetentionDays</c> as well — two paths onto the same columns, disagreeing, so a test of either
    /// one proved nothing about the other. The consequence is worse than the "silent limit drift" it was filed
    /// as: <c>AuditLogPurgeService</c> reads <c>Tenant.AuditLogRetentionDays</c> RAW (there is no plan lookup
    /// and no resolver in that path), so a stale snapshot makes the DAILY purge job delete audit rows on the
    /// wrong retention window. Raise a plan from 90 to 365 days and audit history is still destroyed at 90 —
    /// irreversible data loss against an intended compliance setting, not drift.</para>
    /// </summary>
    private async Task<IReadOnlyList<string>> RecomputeTenantsOnPlanAsync(
        SubscriptionPlan plan, CancellationToken cancellationToken)
    {
        var tenants = await _db.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.PlanId == plan.Code && !t.IsDeleted)
            .ToListAsync(cancellationToken);
        if (tenants.Count == 0)
            return Array.Empty<string>();

        var now = DateTime.UtcNow;
        var derived = PlanModules.DeriveTenantModules(plan.EnabledModules);
        foreach (var tenant in tenants)
        {
            tenant.EnabledModules = derived.ToList(); // fresh list per tenant (jsonb column; don't share a reference)

            // ISSUE-474: the same two snapshot columns ChangeTenantPlanAsync stamps. Staged into the SAME
            // SaveChanges as the plan edit, so the plan row and every tenant snapshot commit atomically —
            // the deliberate in-transaction choice documented above is unchanged.
            tenant.MaxEmployees = plan.MaxEmployees;
            tenant.AuditLogRetentionDays = plan.AuditLogRetentionDays;

            tenant.UpdatedAt = now;
        }

        return tenants.Select(t => t.Subdomain).ToList();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string? ValidateModules(IReadOnlyList<string>? modules)
    {
        if (modules is null)
            return "Enabled modules are required.";
        var unknown = modules.Where(m => !PlanModules.IsValid(m)).ToList();
        return unknown.Count > 0
            ? $"Unknown module(s): {string.Join(", ", unknown)}."
            : null;
    }

    /// <summary>Applies the editable fields onto a plan, normalizing the modules (CoreHR always enabled, deduped).</summary>
    private static void ApplyEditableFields(SubscriptionPlan plan, PlanEditableFields f)
    {
        plan.Name = f.Name.Trim();
        plan.Description = string.IsNullOrWhiteSpace(f.Description) ? null : f.Description.Trim();
        plan.PriceMonthly = f.PriceMonthly;
        plan.PriceYearly = f.PriceYearly;
        plan.Currency = (f.Currency ?? "USD").Trim().ToUpperInvariant();
        plan.TrialDays = f.TrialDays;
        plan.IsPublic = f.IsPublic;
        plan.MaxEmployees = f.MaxEmployees;
        plan.MaxStorageGb = f.MaxStorageGb;
        plan.MaxApiCallsPerMonth = f.MaxApiCallsPerMonth;
        plan.MaxEmailSendsPerMonth = f.MaxEmailSendsPerMonth;
        plan.MaxCustomRoles = f.MaxCustomRoles;
        plan.MaxCustomFieldsPerEntity = f.MaxCustomFieldsPerEntity;
        plan.MaxWorkflows = f.MaxWorkflows;
        plan.AuditLogRetentionDays = f.AuditLogRetentionDays;
        plan.SlaTier = (f.SlaTier ?? "standard").Trim();

        // CoreHR is always enabled (FR-6); dedupe + preserve canonical order.
        var enabled = new HashSet<string>(f.EnabledModules ?? new List<string>()) { PlanModules.CoreHr };
        plan.EnabledModules = PlanModules.All.Where(enabled.Contains).ToList();

        plan.FeatureFlags = new PlanFeatureFlags
        {
            Sso = f.FeatureFlags.Sso,
            CustomDomain = f.FeatureFlags.CustomDomain,
            WhiteLabel = f.FeatureFlags.WhiteLabel,
            Scim = f.FeatureFlags.Scim,
            Sandbox = f.FeatureFlags.Sandbox,
        };
    }

    /// <summary>Active-tenant count: not deleted and not terminated/terminating (AC-1/FR-5).</summary>
    private async Task<Dictionary<string, int>> CountActiveTenantsByPlanAsync(CancellationToken cancellationToken)
    {
        var grouped = await _db.Tenants
            .IgnoreQueryFilters()
            .Where(t => !t.IsDeleted
                        && t.Status != TenantStatus.Terminated
                        && t.Status != TenantStatus.Terminating)
            .GroupBy(t => t.PlanId)
            .Select(g => new { Code = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return grouped.ToDictionary(x => x.Code, x => x.Count, StringComparer.Ordinal);
    }

    private Task<int> CountActiveTenantsForCodeAsync(string code, CancellationToken cancellationToken)
        => _db.Tenants
            .IgnoreQueryFilters()
            .CountAsync(t => t.PlanId == code
                             && !t.IsDeleted
                             && t.Status != TenantStatus.Terminated
                             && t.Status != TenantStatus.Terminating,
                cancellationToken);

    private static PlanDetailDto ToDetailDto(SubscriptionPlan p, int activeTenantCount) => new(
        p.Id, p.Code, p.Name, p.Description, p.PriceMonthly, p.PriceYearly, p.Currency, p.TrialDays,
        p.IsPublic, p.IsActive,
        p.MaxEmployees, p.MaxStorageGb, p.MaxApiCallsPerMonth, p.MaxEmailSendsPerMonth,
        p.MaxCustomRoles, p.MaxCustomFieldsPerEntity, p.MaxWorkflows,
        p.AuditLogRetentionDays, p.SlaTier,
        p.EnabledModules.ToList(),
        new PlanFeatureFlagsDto(p.FeatureFlags.Sso, p.FeatureFlags.CustomDomain, p.FeatureFlags.WhiteLabel,
            p.FeatureFlags.Scim, p.FeatureFlags.Sandbox),
        activeTenantCount, p.CreatedAt, p.UpdatedAt);

    private static PlanLimitOverrideDto ToOverrideDto(PlanLimitOverride o) =>
        new(o.Id, o.TenantId, o.LimitKey, o.Value, o.ExpiresAt, o.CreatedAt, o.UpdatedAt);

    private static string ToAuditState(SubscriptionPlan p) => JsonSerializer.Serialize(new
    {
        p.Id, p.Code, p.Name, p.Description, p.PriceMonthly, p.PriceYearly, p.Currency, p.TrialDays,
        p.IsPublic, p.IsActive, p.MaxEmployees, p.MaxStorageGb, p.MaxApiCallsPerMonth, p.MaxEmailSendsPerMonth,
        p.MaxCustomRoles, p.MaxCustomFieldsPerEntity, p.MaxWorkflows, p.AuditLogRetentionDays, p.SlaTier,
        p.EnabledModules, p.FeatureFlags,
    });

    private static string ToOverrideAuditState(PlanLimitOverride o) => JsonSerializer.Serialize(new
    {
        o.Id, o.TenantId, o.LimitKey, o.Value, o.ExpiresAt,
    });

    /// <summary>
    /// Writes a system audit-log row for a plan action (NFR-3). Plan-level actions have no tenant scope
    /// (TenantId null); override actions carry the affected tenant id.
    /// </summary>
    private void WriteAudit(string action, Guid resourceId, string? before, string? after, DateTime now, Guid? tenantId = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            UserId = ActorId,
            EventType = action,
            Action = action,
            ResourceType = action.StartsWith("Plan.Override", StringComparison.Ordinal)
                ? "PlanLimitOverride"
                : "SubscriptionPlan",
            ResourceId = resourceId.ToString(),
            Before = before,
            After = after,
            CreatedAt = now,
        });
    }
}
