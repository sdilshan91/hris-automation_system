using System.Text.Json;
using System.Text.RegularExpressions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Tenants.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// System-admin tenant provisioning (US-ADM-001). Runs in the system/admin context (no resolved tenant), so it
/// queries every tenant-scoped entity with <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{T}"/>
/// and sets <c>TenantId</c> EXPLICITLY on each seeded row (the TenantInterceptor is a no-op when no tenant is
/// resolved). Provisioning is transactional (FR-3) and idempotent on subdomain (NFR-2/BR-2): a second attempt
/// with the same subdomain returns a clear failure and never duplicates.
///
/// <para><b>Tenant isolation (AC-6):</b> the platform enforces isolation via the EF Core global query filters in
/// <c>AppDbContext.OnModelCreating</c> + the TenantInterceptor (RLS is deferred platform work, Rls:Enabled=false).
/// A newly-provisioned tenant is therefore isolated automatically — every business entity is filtered by
/// <c>TenantId</c> once the tenant resolves on <c>{subdomain}.yourhrm.com</c>. No per-tenant RLS SQL is written
/// here.</para>
///
/// <para><b>Seeded master data (§7):</b> built-in roles (reusing <see cref="PermissionCatalog.BuiltInRoles"/>),
/// default leave types (Annual/Sick/Casual) and a default General Shift are seeded. The "1-step manager-approval
/// leave workflow" has NO configurable-workflow entity in the codebase yet (only the per-request
/// <c>LeaveApprovalHistory</c> audit), so it is intentionally skipped — manager approval is the built-in default
/// of the leave-request flow. The holiday-calendar TEMPLATE is likewise not seeded (no template entity exists).</para>
/// </summary>
public sealed class TenantProvisioningService : ITenantProvisioningService
{
    private static readonly Regex SubdomainFormat = new(
        "^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", RegexOptions.Compiled);

    private static readonly string[] DefaultReservedSubdomains =
    [
        "www", "api", "admin", "app", "mail", "status", "docs", "help", "support",
        "static", "cdn", "dev", "stage", "prod", "test", "qa"
    ];

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantWelcomeEmailService _welcomeEmail;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TenantProvisioningService> _logger;

    public TenantProvisioningService(
        AppDbContext db,
        ICurrentUser currentUser,
        ITenantWelcomeEmailService welcomeEmail,
        IConfiguration configuration,
        ILogger<TenantProvisioningService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _welcomeEmail = welcomeEmail;
        _configuration = configuration;
        _logger = logger;
    }

    private HashSet<string> ReservedSubdomains => _configuration
        .GetSection("Platform:ReservedSubdomains")
        .Get<string[]>()?.ToHashSet(StringComparer.OrdinalIgnoreCase)
        ?? DefaultReservedSubdomains.ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task<Result<ProvisionTenantResultDto>> ProvisionAsync(
        ProvisionTenantInput input, CancellationToken cancellationToken = default)
    {
        var subdomain = (input.Subdomain ?? string.Empty).Trim().ToLowerInvariant();
        var ownerEmail = (input.OwnerEmail ?? string.Empty).Trim().ToLowerInvariant();

        // Defence in depth — the validator already covers shape, but the service must never trust its caller.
        if (subdomain.Length is < 3 or > 50 || !SubdomainFormat.IsMatch(subdomain))
            return Result.Failure<ProvisionTenantResultDto>("Subdomain is unavailable.", 400, "subdomain_invalid");

        if (ReservedSubdomains.Contains(subdomain))
            return Result.Failure<ProvisionTenantResultDto>("Subdomain is unavailable.", 400, "subdomain_reserved");

        // Idempotency / uniqueness on subdomain (NFR-2/BR-2): never duplicate.
        var subdomainTaken = await _db.Tenants
            .IgnoreQueryFilters()
            .AnyAsync(t => t.Subdomain == subdomain, cancellationToken);
        if (subdomainTaken)
            return Result.Failure<ProvisionTenantResultDto>("Subdomain is unavailable.", 409, "subdomain_taken");

        // Plan must exist and be active.
        var plan = await _db.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.Id == input.SubscriptionPlanId, cancellationToken);
        if (plan is null || !plan.IsActive)
            return Result.Failure<ProvisionTenantResultDto>("The selected subscription plan is not available.", 400, "plan_invalid");

        var trialDays = input.TrialDays ?? plan.TrialDays;
        if (trialDays < 0)
            return Result.Failure<ProvisionTenantResultDto>("Trial days cannot be negative.", 400, "trial_days_invalid");

        var now = DateTime.UtcNow;
        var isTrial = trialDays > 0; // BR-3
        var actor = _currentUser.IsAuthenticated ? _currentUser.UserId.ToString() : "system";

        // ── Tenant (BR-3/BR-4/BR-5) ─────────────────────────────────────────
        var tenant = new Tenant
        {
            Id = BaseEntity.NewUuidV7(),
            Subdomain = subdomain,
            Name = input.Name.Trim(),
            Status = isTrial ? TenantStatus.Trial : TenantStatus.Active,
            PlanId = plan.Code,
            TrialEndsAt = isTrial ? now.AddDays(trialDays) : null,
            BillingEmail = ownerEmail,
            ContactEmail = ownerEmail,
            MaxEmployees = plan.MaxEmployees,
            // US-ADM-009 (FR-6 / test hint): inherit the chosen plan's enabled modules. CoreHR is always on. A
            // legacy plan with no configured modules falls back to all canonical modules so existing provisioning
            // behaviour is preserved.
            EnabledModules = DeriveTenantModules(plan),
            CreatedAt = now,
        };
        _db.Tenants.Add(tenant);

        // ── Built-in roles (reuse PermissionCatalog — same set DbInitializer seeds) ──
        var roles = SeedBuiltInRoles(tenant.Id);
        var tenantOwnerRole = roles.Single(r => r.Name == PermissionCatalog.BuiltInRoles.TenantOwner);

        // ── Owner user: link existing global user or create new (AC-3) ───────
        var ownerExists = false;
        var user = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == ownerEmail, cancellationToken);

        if (user is null)
        {
            user = new User
            {
                Id = BaseEntity.NewUuidV7(),
                Email = ownerEmail,
                DisplayName = null,
                PasswordHash = null, // set via the welcome-email set-password link (FR-4)
                IsActive = true,
                CreatedAt = now,
            };
            _db.Users.Add(user);
        }
        else
        {
            ownerExists = true; // link the existing account — no duplicate user (AC-3)
        }

        // ── Membership + Tenant Owner role assignment ───────────────────────
        var userTenant = new UserTenant
        {
            Id = BaseEntity.NewUuidV7(),
            UserId = user.Id,
            TenantId = tenant.Id,
            Status = UserTenantStatus.Active,
            CreatedAt = now,
        };
        _db.UserTenants.Add(userTenant);

        _db.UserTenantRoles.Add(new UserTenantRole
        {
            UserTenantId = userTenant.Id,
            RoleId = tenantOwnerRole.Id,
            AssignedAt = now,
            AssignedBy = actor,
        });

        // ── Default master data (§7) ────────────────────────────────────────
        SeedDefaultLeaveTypes(tenant.Id, now, actor);
        SeedDefaultShift(tenant.Id, now, actor);

        // ── Lifecycle event (AC-4) + structured audit (NFR-3) ───────────────
        var detail = new
        {
            tenant.Name,
            tenant.Subdomain,
            Plan = plan.Code,
            Status = tenant.Status.ToString(),
            OwnerEmail = ownerEmail,     // not a secret
            input.Region,
            TrialDays = trialDays,
            OwnerLinked = ownerExists,
        };
        var detailJson = JsonSerializer.Serialize(detail);

        _db.TenantLifecycleEvents.Add(new TenantLifecycleEvent
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenant.Id,
            EventType = "created",
            DetailJson = detailJson,
            CreatedAt = now,
            CreatedBy = actor,
        });

        _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenant.Id,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = "Tenant.Provisioned",
            Action = "Tenant.Provisioned",
            ResourceType = "Tenant",
            ResourceId = tenant.Id.ToString(),
            After = detailJson, // payload minus secrets (no password/token)
            CreatedAt = now,
        });

        // Persist everything atomically (FR-3). EF Core batches one SaveChanges into a single transaction.
        await _db.SaveChangesAsync(cancellationToken);

        // ── Welcome email (AC-1/FR-4). Non-fatal: the tenant is already committed (§11 partial-failure). ──
        try
        {
            await _welcomeEmail.SendWelcomeAsync(new TenantWelcomeEmailMessage(
                tenant.Id, tenant.Name, tenant.Subdomain, ownerEmail, user.DisplayName, ownerExists, isTrial),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Tenant {TenantId} provisioned but the welcome email dispatch failed; it can be retried.",
                tenant.Id);
        }

        _logger.LogInformation(
            "Provisioned tenant {Subdomain} ({TenantId}) on plan {Plan}, status {Status}, ownerLinked={OwnerLinked}.",
            tenant.Subdomain, tenant.Id, plan.Code, tenant.Status, ownerExists);

        return Result.Success<ProvisionTenantResultDto>(new ProvisionTenantResultDto(
            tenant.Id, tenant.Subdomain, tenant.Status.ToString(), tenant.CreatedAt, ownerExists));
    }

    public async Task<Result<IReadOnlyList<TenantListItemDto>>> ListTenantsAsync(
        string? search = null, CancellationToken cancellationToken = default)
    {
        var query = _db.Tenants
            .IgnoreQueryFilters()
            .Where(t => !t.IsDeleted);

        // US-ADM-002 AC-2: case-insensitive match on tenant name/subdomain. ToLower() translates on both
        // Npgsql (→ lower()) and the InMemory provider.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(t =>
                t.Name.ToLower().Contains(term) || t.Subdomain.ToLower().Contains(term));
        }

        var tenants = await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TenantListItemDto(
                t.Id, t.Name, t.Subdomain, t.Status.ToString(), t.PlanId, t.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<TenantListItemDto>>(tenants);
    }

    public async Task<Result<SubdomainAvailabilityDto>> CheckSubdomainAvailabilityAsync(
        string subdomain, CancellationToken cancellationToken = default)
    {
        subdomain = (subdomain ?? string.Empty).Trim().ToLowerInvariant();

        if (subdomain.Length is < 3 or > 50 || !SubdomainFormat.IsMatch(subdomain))
            return Result.Success(new SubdomainAvailabilityDto(false, "Invalid subdomain format."));

        if (ReservedSubdomains.Contains(subdomain))
            return Result.Success(new SubdomainAvailabilityDto(false, "Subdomain is reserved."));

        var taken = await _db.Tenants
            .IgnoreQueryFilters()
            .AnyAsync(t => t.Subdomain == subdomain, cancellationToken);

        return taken
            ? Result.Success(new SubdomainAvailabilityDto(false, "Subdomain is already in use."))
            : Result.Success(new SubdomainAvailabilityDto(true, null));
    }

    public async Task<Result<IReadOnlyList<SubscriptionPlanDto>>> ListActivePlansAsync(
        CancellationToken cancellationToken = default)
    {
        var plans = await _db.SubscriptionPlans
            .Where(p => p.IsActive)
            .OrderBy(p => p.PriceMonthly)
            .Select(p => new SubscriptionPlanDto(
                p.Id, p.Name, p.Code, p.PriceMonthly, p.TrialDays, p.MaxEmployees))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<SubscriptionPlanDto>>(plans);
    }

    /// <summary>
    /// US-ADM-009 (FR-6): the tenant's enabled modules are derived from the chosen plan. CoreHR is always
    /// enabled; the result is deduped and kept in canonical order. A legacy plan with no configured modules
    /// falls back to the full canonical list so existing provisioning behaviour is preserved.
    /// </summary>
    private static List<string> DeriveTenantModules(SubscriptionPlan plan)
    {
        if (plan.EnabledModules is null || plan.EnabledModules.Count == 0)
            return PlanModules.All.ToList();

        var enabled = new HashSet<string>(plan.EnabledModules) { PlanModules.CoreHr };
        return PlanModules.All.Where(enabled.Contains).ToList();
    }

    // ── Seeding helpers (reuse PermissionCatalog + the DbInitializer conventions) ──

    private List<Role> SeedBuiltInRoles(Guid tenantId)
    {
        var roles = new List<Role>();
        foreach (var roleName in PermissionCatalog.BuiltInRoles.All)
        {
            var role = new Role
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                Name = roleName,
                Description = $"Built-in {roleName} role",
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
            };
            foreach (var perm in PermissionCatalog.DefaultPermissionsFor(roleName))
            {
                role.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = perm });
            }
            _db.Roles.Add(role);
            roles.Add(role);
        }
        return roles;
    }

    private void SeedDefaultLeaveTypes(Guid tenantId, DateTime now, string actor)
    {
        // §7 seeded data: Annual / Sick / Casual. Minimal sensible defaults; tenants edit via US-LV-001.
        var defaults = new (string Name, string Code, string Color, decimal Entitlement, int Order)[]
        {
            ("Annual Leave", "AL", "#4CAF50", 14m, 1),
            ("Sick Leave", "SL", "#F44336", 7m, 2),
            ("Casual Leave", "CL", "#2196F3", 7m, 3),
        };

        foreach (var (name, code, color, entitlement, order) in defaults)
        {
            _db.LeaveTypes.Add(new LeaveType
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                Name = name,
                Code = code,
                Color = color,
                AnnualEntitlement = entitlement,
                AccrualFrequency = AccrualFrequency.Upfront,
                Gender = LeaveTypeGender.All,
                SystemCategory = LeaveTypeSystemCategory.None,
                DisplayOrder = order,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = actor,
            });
        }
    }

    private void SeedDefaultShift(Guid tenantId, DateTime now, string actor)
    {
        // Mirrors DbInitializer.EnsureDefaultShiftAsync (US-ATT-005 BR-1/FR-5): one default General Shift.
        _db.Shifts.Add(new Shift
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            Name = "General Shift",
            Type = ShiftType.Single,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(17, 0),
            BreakDurationMinutes = 60,
            GracePeriodMinutes = 15,
            MinimumHours = null,
            WorkingDays = new List<int> { 1, 2, 3, 4, 5 },
            IsDefault = true,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = actor,
        });
    }
}
