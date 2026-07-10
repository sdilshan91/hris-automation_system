using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Persistence;

public static class DbInitializer
{
    private const string DefaultAdminEmail = "admin@hrm.local";
    private const string DefaultAdminPassword = "Admin@123!";
    private const string DefaultTenantSubdomain = "platform";
    private const string DefaultTenantName = "HRM Platform Admin";
    private const string SystemAdminRoleName = "SystemAdmin";

    // DEV/TEST-ONLY E2E business tenant (used by the Playwright E2E layer for a real password login).
    // Seeded ONLY in the Development environment — see SeedE2EDevTenantAsync.
    private const string E2ETenantSubdomain = "e2e";
    private const string E2ETenantName = "E2E Test Org";
    private const string E2EOwnerEmail = "owner@e2e.test";
    private const string E2EOwnerPassword = "E2ePass@123!";
    // US-ADM-003 (BR-1/AC-6): the platform read-only support role. Uses the catalog's "System Support" name.
    private static readonly string SystemSupportRoleName = PermissionCatalog.SystemRoles.SystemSupport;

    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");

        await MigrateAsync(dbContext, logger, cancellationToken);

        await SeedAsync(dbContext, logger, cancellationToken);

        // DEV/TEST-ONLY: seed the E2E business tenant + owner login. Gated strictly to Development so it
        // never runs in Staging/Production. The host environment is resolved from DI (null-safe).
        var environment = scope.ServiceProvider.GetService<IHostEnvironment>();
        if (environment is not null && environment.IsDevelopment())
        {
            await SeedE2EDevTenantAsync(dbContext, logger, cancellationToken);
        }

        // RLS increment 3a: reconcile row-level-security ENFORCEMENT to the Rls:Enabled flag. Runs AFTER
        // migrate + seed so the schema, the dormant policies (migration 20260710120000), and the seed data all
        // exist first. Gated + idempotent: a no-op on every current environment (Rls:Enabled=false everywhere).
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        await ReconcileRowLevelSecurityAsync(dbContext, configuration, logger, cancellationToken);
    }

    /// <summary>
    /// RLS increment 3a — the flag-gated ENABLE/FORCE (and, on rollback, DISABLE) reconciler.
    ///
    /// <para>The dormant <c>tenant_isolation</c> policies land via migration
    /// <c>20260710120000_Platform_RlsPolicies_Dormant</c>; a policy is INERT until its table also has
    /// <c>ENABLE ROW LEVEL SECURITY</c>. This step couples enforcement to the SAME <c>Rls:Enabled</c> flag that
    /// gates <c>TenantTransactionBehavior</c> / <c>TenantJobRunner</c> so they turn on together and the flip is
    /// reversible by config + restart with NO down-migration:</para>
    /// <list type="bullet">
    ///   <item><b>Rls:Enabled = true</b> ⇒ <c>ENABLE + FORCE ROW LEVEL SECURITY</c> on every policy-bearing table
    ///     (the exact §1 set the migration policied: a <c>tenant_id</c> column on a base table, excluding
    ///     <c>users</c>/<c>tenants</c>). Idempotent — ENABLE/FORCE are no-ops if already set.</item>
    ///   <item><b>Rls:Enabled = false</b> ⇒ <c>NO FORCE</c> + <c>DISABLE ROW LEVEL SECURITY</c> on that same set,
    ///     so flipping the flag back to false + restarting ACTIVELY rolls enforcement OFF (critical R7: otherwise a
    ///     rollback leaves tables enforced while the GUC stops being set → total breakage). Idempotent.</item>
    /// </list>
    ///
    /// <para>Runs on the connection the router selects for a NULL ambient tenant (startup) — the privileged
    /// <c>hrm_owner</c> (or <c>DefaultConnection</c> when <c>PrivilegedConnection</c> is blank), which owns the
    /// tables and can run the DDL. No-op on the InMemory provider (RLS is a database-engine feature). When the
    /// flag is true but the connected role bypasses RLS (superuser/BYPASSRLS — e.g. dev's <c>developer</c>), logs a
    /// WARNING because isolation is NOT actually enforced for that connection.</para>
    /// </summary>
    public static async Task ReconcileRowLevelSecurityAsync(
        AppDbContext db, IConfiguration configuration, ILogger logger, CancellationToken ct)
    {
        // RLS is a real-Postgres feature — the EF InMemory provider implements none of it.
        if (!db.Database.IsRelational())
            return;

        var enabled = configuration.GetValue("Rls:Enabled", false);

        // The exact policy-bearing set from migration 20260710120000: a `tenant_id` column on a base table,
        // excluding the global identity/tenant tables. Discovered by reflection over information_schema (never
        // hand-maintained) so a future tenant table is picked up automatically. These SQL statements are constant
        // literals with no interpolated/external input — no injection surface.
        var affected = await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value"
            FROM information_schema.columns c
            JOIN information_schema.tables t
              ON t.table_schema = c.table_schema AND t.table_name = c.table_name
            WHERE c.table_schema = 'public'
              AND c.column_name  = 'tenant_id'
              AND t.table_type   = 'BASE TABLE'
              AND c.table_name NOT IN ('users', 'tenants')
            """).SingleAsync(ct);

        if (enabled)
        {
            // R3 — if the connected role bypasses RLS (superuser or BYPASSRLS), enforcement is silently ineffective
            // for this connection. Warn loudly rather than give a false sense of isolation.
            var currentUser = await db.Database.SqlQueryRaw<string>(
                """SELECT current_user AS "Value" """).SingleAsync(ct);
            var bypasses = await db.Database.SqlQueryRaw<bool>(
                """SELECT (rolsuper OR rolbypassrls) AS "Value" FROM pg_roles WHERE rolname = current_user""")
                .SingleAsync(ct);
            if (bypasses)
            {
                logger.LogWarning(
                    "RLS is ENABLED but the app connection (current_user={CurrentUser}) bypasses RLS "
                    + "(superuser/BYPASSRLS) — isolation is NOT actually enforced for this connection; point "
                    + "DefaultConnection at hrm_app to enforce.", currentUser);
            }

            await db.Database.ExecuteSqlRawAsync("""
                DO $rls$
                DECLARE r record;
                BEGIN
                    FOR r IN
                        SELECT c.table_name
                        FROM information_schema.columns c
                        JOIN information_schema.tables t
                          ON t.table_schema = c.table_schema AND t.table_name = c.table_name
                        WHERE c.table_schema = 'public'
                          AND c.column_name  = 'tenant_id'
                          AND t.table_type   = 'BASE TABLE'
                          AND c.table_name NOT IN ('users', 'tenants')
                    LOOP
                        EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', r.table_name);
                        EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY', r.table_name);
                    END LOOP;
                END
                $rls$;
                """, ct);

            logger.LogInformation(
                "RLS reconciler: ENABLED + FORCED row-level security on {Count} tenant-scoped table(s) "
                + "(Rls:Enabled=true).", affected);
        }
        else
        {
            // Rls:Enabled=false ⇒ actively roll enforcement OFF (R7). DISABLE + NO FORCE are no-ops when a table is
            // already unenforced, so this is behaviour-neutral on every current environment (the committed default).
            await db.Database.ExecuteSqlRawAsync("""
                DO $rls$
                DECLARE r record;
                BEGIN
                    FOR r IN
                        SELECT c.table_name
                        FROM information_schema.columns c
                        JOIN information_schema.tables t
                          ON t.table_schema = c.table_schema AND t.table_name = c.table_name
                        WHERE c.table_schema = 'public'
                          AND c.column_name  = 'tenant_id'
                          AND t.table_type   = 'BASE TABLE'
                          AND c.table_name NOT IN ('users', 'tenants')
                    LOOP
                        EXECUTE format('ALTER TABLE public.%I NO FORCE ROW LEVEL SECURITY', r.table_name);
                        EXECUTE format('ALTER TABLE public.%I DISABLE ROW LEVEL SECURITY', r.table_name);
                    END LOOP;
                END
                $rls$;
                """, ct);

            logger.LogInformation(
                "RLS reconciler: DISABLED row-level security on {Count} tenant-scoped table(s) "
                + "(Rls:Enabled=false — enforcement off).", affected);
        }
    }

    /// <summary>
    /// Applies only the pending EF Core migrations, safely:
    ///  - <see cref="RelationalDatabaseFacadeExtensions.MigrateAsync"/> is idempotent — it runs only the
    ///    migrations not yet recorded in <c>__EFMigrationsHistory</c>, so applying twice is a no-op.
    ///  - We first list pending migrations purely to log what is about to be applied (and skip the call
    ///    entirely when the schema is already current).
    ///  - Transient failures (e.g. a Postgres container still booting, or a brief connection blip) are
    ///    retried with bounded exponential backoff so startup neither fails on the first hiccup nor hangs
    ///    forever. After the final attempt the exception is rethrown for the caller to handle.
    /// </summary>
    private static async Task MigrateAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        const int maxAttempts = 5;
        var databaseName = db.Database.GetDbConnection().Database;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
                if (pending.Count == 0)
                {
                    logger.LogInformation("Database {Database} schema is up to date — no pending migrations.", databaseName);
                    return;
                }

                logger.LogInformation("Applying {Count} pending migration(s) to {Database}: {Migrations}",
                    pending.Count, databaseName, string.Join(", ", pending));
                await db.Database.MigrateAsync(ct);
                logger.LogInformation("Successfully applied {Count} migration(s) to {Database}.", pending.Count, databaseName);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
                logger.LogWarning(ex,
                    "Migration attempt {Attempt}/{MaxAttempts} on {Database} failed; retrying in {Delay}s.",
                    attempt, maxAttempts, databaseName, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
        }
    }

    private static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        // US-ADM-001: seed the default subscription plans tenant provisioning selects from.
        await SeedSubscriptionPlansAsync(db, logger, ct);

        // US-NTF-002: seed the platform system-default email templates for every catalog event (BR-2).
        await SeedSystemNotificationTemplatesAsync(db, logger, ct);

        var defaultEnabledModules = PermissionCatalog.ByModule.Keys.OrderBy(module => module).ToList();
        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Subdomain == DefaultTenantSubdomain, ct);

        if (tenant is null)
        {
            tenant = new Tenant
            {
                Id = BaseEntity.NewUuidV7(),
                Subdomain = DefaultTenantSubdomain,
                Name = DefaultTenantName,
                Status = TenantStatus.Active,
                PlanId = "default",
                EnabledModules = defaultEnabledModules,
                ContactEmail = DefaultAdminEmail,
                CreatedAt = DateTime.UtcNow,
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded default admin tenant {Subdomain}", tenant.Subdomain);
        }
        else if (string.IsNullOrWhiteSpace(tenant.PlanId) || tenant.EnabledModules.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(tenant.PlanId))
            {
                tenant.PlanId = "default";
            }

            if (tenant.EnabledModules.Count == 0)
            {
                tenant.EnabledModules = defaultEnabledModules;
            }

            tenant.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Updated default admin tenant metadata for {Subdomain}", tenant.Subdomain);
        }

        // Seed SystemAdmin role (platform-level) with all permissions
        var systemAdminRole = await db.Roles
            .IgnoreQueryFilters()
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.TenantId == tenant.Id && r.Name == SystemAdminRoleName, ct);

        if (systemAdminRole is null)
        {
            systemAdminRole = new Role
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenant.Id,
                Name = SystemAdminRoleName,
                Description = "Platform administrator with full access",
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
            };

            // SystemAdmin gets all permissions from the catalog
            foreach (var perm in PermissionCatalog.AllPermissions)
            {
                systemAdminRole.RolePermissions.Add(new RolePermission
                {
                    RoleId = systemAdminRole.Id,
                    Permission = perm,
                });
            }

            db.Roles.Add(systemAdminRole);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Role} role with {Count} permissions for tenant {Subdomain}",
                systemAdminRole.Name, PermissionCatalog.AllPermissions.Count, tenant.Subdomain);
        }

        // US-ADM-003 (BR-1/AC-6): seed the read-only "System Support" system role in the platform tenant so
        // read-only impersonation is a real, testable capability (it can initiate impersonation + view
        // monitoring, but holds no destructive/provisioning permissions). Idempotent + reconciled below.
        await SeedSystemSupportRoleAsync(db, tenant.Id, logger, ct);

        // Seed built-in tenant roles with their default permissions (FR-2)
        await SeedBuiltInTenantRolesAsync(db, tenant.Id, logger, ct);

        var user = await db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == DefaultAdminEmail, ct);

        if (user is null)
        {
            user = new User
            {
                Id = BaseEntity.NewUuidV7(),
                Email = DefaultAdminEmail,
                DisplayName = "Platform Administrator",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(DefaultAdminPassword, workFactor: 12),
                IsActive = true,
                PasswordChangedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded default admin user {Email}", user.Email);
        }

        var userTenant = await db.UserTenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ut => ut.UserId == user.Id && ut.TenantId == tenant.Id, ct);

        if (userTenant is null)
        {
            userTenant = new UserTenant
            {
                Id = BaseEntity.NewUuidV7(),
                UserId = user.Id,
                TenantId = tenant.Id,
                Status = UserTenantStatus.Active,
                CreatedAt = DateTime.UtcNow,
            };
            db.UserTenants.Add(userTenant);
            await db.SaveChangesAsync(ct);
        }

        var roleAssigned = await db.UserTenantRoles
            .IgnoreQueryFilters()
            .AnyAsync(utr => utr.UserTenantId == userTenant.Id && utr.RoleId == systemAdminRole.Id, ct);

        if (!roleAssigned)
        {
            db.UserTenantRoles.Add(new UserTenantRole
            {
                UserTenantId = userTenant.Id,
                RoleId = systemAdminRole.Id,
                AssignedAt = DateTime.UtcNow,
                AssignedBy = "system-seed",
            });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Assigned {Role} to admin user", SystemAdminRoleName);
        }

        // Reconcile across ALL tenants (existing + newly-seeded) so new catalog permissions and the
        // US-ATT-005 default shift land on tenants provisioned before this release (idempotent).
        await ReconcileAllTenantsAsync(db, logger, ct);
    }

    /// <summary>
    /// Seeds the default platform subscription plans (US-ADM-001) tenant provisioning selects from. Idempotent:
    /// each plan is keyed by its unique <c>Code</c> and only inserted when absent. Full plan CRUD is US-ADM-009.
    /// </summary>
    private static async Task SeedSubscriptionPlansAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var defaults = new (string Name, string Code, decimal PriceMonthly, int TrialDays, int? MaxEmployees)[]
        {
            ("Starter", "starter", 0m, 30, 25),
            ("Professional", "professional", 49m, 14, 200),
            ("Enterprise", "enterprise", 199m, 0, null),
        };

        var existingCodes = await db.SubscriptionPlans
            .Select(p => p.Code)
            .ToListAsync(ct);
        var existing = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var (name, code, price, trialDays, maxEmployees) in defaults)
        {
            if (existing.Contains(code))
                continue;

            db.SubscriptionPlans.Add(new SubscriptionPlan
            {
                Id = BaseEntity.NewUuidV7(),
                Name = name,
                Code = code,
                PriceMonthly = price,
                TrialDays = trialDays,
                MaxEmployees = maxEmployees,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Count} default subscription plan(s)", added);
        }
    }

    /// <summary>
    /// US-NTF-002 BR-2: seed the platform system-default email template for every catalog event in the default
    /// language ("en"), so every event always has a baseline tenants can fall back to / override. Idempotent —
    /// keyed by (event_key, language) and only inserted when absent. These rows live in the NON-tenant-scoped
    /// system_notification_template table; managed by System Admins (story §10).
    /// </summary>
    private static async Task SeedSystemNotificationTemplatesAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var lang = NotificationEventCatalog.DefaultLanguage;

        var existing = (await db.SystemNotificationTemplates
                .Where(t => t.Language == lang)
                .Select(t => t.EventKey)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var def in NotificationEventCatalog.All)
        {
            if (existing.Contains(def.EventKey))
                continue;

            db.SystemNotificationTemplates.Add(new SystemNotificationTemplate
            {
                Id = BaseEntity.NewUuidV7(),
                EventKey = def.EventKey,
                Language = lang,
                Subject = def.DefaultSubject,
                BodyHtml = def.DefaultBodyHtml,
                BodyText = def.DefaultBodyText,
                CreatedAt = DateTime.UtcNow,
            });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Count} system notification template(s)", added);
        }
    }

    /// <summary>
    /// Idempotent per-tenant reconcile run on every startup:
    ///  - Adds any missing default permissions to built-in roles (so new catalog entries such as
    ///    US-ATT-005 Attendance.Shift.Manage reach tenants provisioned before the permission existed).
    ///  - Ensures every tenant has exactly one default shift (US-ATT-005 BR-1/FR-5 "created during
    ///    tenant provisioning").
    /// </summary>
    private static async Task ReconcileAllTenantsAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var tenants = await db.Tenants
            .IgnoreQueryFilters()
            .Where(t => !t.IsDeleted)
            .Select(t => t.Id)
            .ToListAsync(ct);

        foreach (var tenantId in tenants)
        {
            await ReconcileBuiltInRolePermissionsAsync(db, tenantId, logger, ct);
            await EnsureDefaultShiftAsync(db, tenantId, logger, ct);
        }
    }

    /// <summary>
    /// Adds missing default permissions to a tenant's built-in roles. Only ADDS — never removes (so a
    /// tenant's bespoke grants on a built-in role are preserved).
    /// </summary>
    private static async Task ReconcileBuiltInRolePermissionsAsync(
        AppDbContext db, Guid tenantId, ILogger logger, CancellationToken ct)
    {
        var builtInNames = PermissionCatalog.BuiltInRoles.All.ToHashSet();

        var roles = await db.Roles
            .IgnoreQueryFilters()
            .Include(r => r.RolePermissions)
            .Where(r => r.TenantId == tenantId && r.IsBuiltIn && builtInNames.Contains(r.Name))
            .ToListAsync(ct);

        var added = 0;
        foreach (var role in roles)
        {
            var current = role.RolePermissions.Select(rp => rp.Permission).ToHashSet();
            var defaults = PermissionCatalog.DefaultPermissionsFor(role.Name);
            foreach (var perm in defaults)
            {
                if (current.Add(perm))
                {
                    role.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = perm });
                    added++;
                }
            }
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Reconciled {Count} missing built-in role permission(s) for tenant {TenantId}",
                added, tenantId);
        }
    }

    /// <summary>
    /// Ensures the tenant has a default shift (US-ATT-005 BR-1/FR-5). Seeds a standard Mon–Fri 09:00–17:00
    /// SINGLE shift named "General Shift" with a 60-minute break when none exists. Idempotent: skipped
    /// if any default shift is already present for the tenant.
    /// </summary>
    private static async Task EnsureDefaultShiftAsync(
        AppDbContext db, Guid tenantId, ILogger logger, CancellationToken ct)
    {
        var hasDefault = await db.Shifts
            .IgnoreQueryFilters()
            .AnyAsync(s => s.TenantId == tenantId && s.IsDefault && !s.IsDeleted, ct);
        if (hasDefault)
            return;

        // Avoid colliding with an existing non-default shift that happens to use the name.
        var nameTaken = await db.Shifts
            .IgnoreQueryFilters()
            .AnyAsync(s => s.TenantId == tenantId && s.Name == "General Shift" && !s.IsDeleted, ct);

        var shift = new Shift
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            Name = nameTaken ? "Default Shift" : "General Shift",
            Type = ShiftType.Single,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(17, 0),
            BreakDurationMinutes = 60,
            GracePeriodMinutes = 15,
            MinimumHours = null,
            WorkingDays = new List<int> { 1, 2, 3, 4, 5 },
            IsDefault = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "system-seed",
        };

        db.Shifts.Add(shift);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded default shift for tenant {TenantId}", tenantId);
    }

    /// <summary>
    /// US-ADM-003 (BR-1/AC-6): seeds (and reconciles) the read-only "System Support" system role in the platform
    /// tenant. Idempotent: created with <see cref="PermissionCatalog.SystemSupportPermissions"/> when absent;
    /// when present, any missing default permissions are added (never removed). Also reconciles the SystemAdmin
    /// role so the new <c>Impersonation.Initiate</c> permission reaches platforms seeded before this release.
    /// </summary>
    private static async Task SeedSystemSupportRoleAsync(
        AppDbContext db, Guid platformTenantId, ILogger logger, CancellationToken ct)
    {
        // Reconcile SystemAdmin: it is seeded with AllPermissions, but an existing row predates the new
        // Impersonation.Initiate permission — add any catalog permissions it is missing.
        var systemAdmin = await db.Roles
            .IgnoreQueryFilters()
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.TenantId == platformTenantId && r.Name == SystemAdminRoleName, ct);
        if (systemAdmin is not null)
        {
            var current = systemAdmin.RolePermissions.Select(rp => rp.Permission).ToHashSet();
            var addedAdmin = 0;
            foreach (var perm in PermissionCatalog.AllPermissions)
            {
                if (current.Add(perm))
                {
                    systemAdmin.RolePermissions.Add(new RolePermission { RoleId = systemAdmin.Id, Permission = perm });
                    addedAdmin++;
                }
            }
            if (addedAdmin > 0)
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Reconciled {Count} missing permission(s) onto {Role}", addedAdmin, SystemAdminRoleName);
            }
        }

        var support = await db.Roles
            .IgnoreQueryFilters()
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.TenantId == platformTenantId && r.Name == SystemSupportRoleName, ct);

        if (support is null)
        {
            support = new Role
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = platformTenantId,
                Name = SystemSupportRoleName,
                Description = "Read-only platform support: impersonate tenant users (read-only) + view monitoring",
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
            };
            foreach (var perm in PermissionCatalog.SystemSupportPermissions)
            {
                support.RolePermissions.Add(new RolePermission { RoleId = support.Id, Permission = perm });
            }
            db.Roles.Add(support);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Role} role with {Count} permissions for the platform tenant",
                SystemSupportRoleName, PermissionCatalog.SystemSupportPermissions.Count);
            return;
        }

        // Reconcile: add any missing default permissions (never remove a bespoke grant).
        var supportCurrent = support.RolePermissions.Select(rp => rp.Permission).ToHashSet();
        var added = 0;
        foreach (var perm in PermissionCatalog.SystemSupportPermissions)
        {
            if (supportCurrent.Add(perm))
            {
                support.RolePermissions.Add(new RolePermission { RoleId = support.Id, Permission = perm });
                added++;
            }
        }
        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Reconciled {Count} missing permission(s) onto {Role}", added, SystemSupportRoleName);
        }
    }

    /// <summary>
    /// DEV/TEST-ONLY: seeds a business tenant <c>e2e</c> ("E2E Test Org") with an owner user
    /// (<c>owner@e2e.test</c> / BCrypt password) so the Playwright E2E layer has a real password login.
    /// Idempotent — every row is only inserted when absent. MUST NOT run outside Development (the caller
    /// in <see cref="RunAsync"/> gates this on <see cref="IHostEnvironment.IsDevelopment"/>).
    /// </summary>
    private static async Task SeedE2EDevTenantAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var enabledModules = PermissionCatalog.ByModule.Keys.OrderBy(module => module).ToList();

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Subdomain == E2ETenantSubdomain, ct);

        if (tenant is null)
        {
            tenant = new Tenant
            {
                Id = BaseEntity.NewUuidV7(),
                Subdomain = E2ETenantSubdomain,
                Name = E2ETenantName,
                Status = TenantStatus.Active,
                PlanId = "default",
                EnabledModules = enabledModules,
                ContactEmail = E2EOwnerEmail,
                CreatedAt = DateTime.UtcNow,
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded DEV E2E tenant {Subdomain}", tenant.Subdomain);
        }

        // Built-in tenant roles (incl. "Tenant Owner") + the default shift for this tenant.
        await SeedBuiltInTenantRolesAsync(db, tenant.Id, logger, ct);
        await EnsureDefaultShiftAsync(db, tenant.Id, logger, ct);

        var ownerRole = await db.Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.TenantId == tenant.Id && r.Name == PermissionCatalog.BuiltInRoles.TenantOwner, ct);
        if (ownerRole is null)
        {
            logger.LogWarning("DEV E2E: Tenant Owner role missing for tenant {Subdomain}; skipping owner assignment.",
                tenant.Subdomain);
            return;
        }

        var user = await db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == E2EOwnerEmail, ct);

        if (user is null)
        {
            user = new User
            {
                Id = BaseEntity.NewUuidV7(),
                Email = E2EOwnerEmail,
                DisplayName = "E2E Owner",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(E2EOwnerPassword, workFactor: 12),
                IsActive = true,
                PasswordChangedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded DEV E2E owner user {Email}", user.Email);
        }

        var userTenant = await db.UserTenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ut => ut.UserId == user.Id && ut.TenantId == tenant.Id, ct);

        if (userTenant is null)
        {
            userTenant = new UserTenant
            {
                Id = BaseEntity.NewUuidV7(),
                UserId = user.Id,
                TenantId = tenant.Id,
                Status = UserTenantStatus.Active,
                CreatedAt = DateTime.UtcNow,
            };
            db.UserTenants.Add(userTenant);
            await db.SaveChangesAsync(ct);
        }

        var roleAssigned = await db.UserTenantRoles
            .IgnoreQueryFilters()
            .AnyAsync(utr => utr.UserTenantId == userTenant.Id && utr.RoleId == ownerRole.Id, ct);

        if (!roleAssigned)
        {
            db.UserTenantRoles.Add(new UserTenantRole
            {
                UserTenantId = userTenant.Id,
                RoleId = ownerRole.Id,
                AssignedAt = DateTime.UtcNow,
                AssignedBy = "system-seed",
            });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Assigned Tenant Owner to DEV E2E owner user");
        }

        // Employee linked to the owner user so employee-self-service endpoints (Leave apply, Attendance
        // clock-in/status) work in E2E — they fail-closed 403 unless Employee.UserId == the acting user.
        // Employee requires a Department + JobTitle (both required FKs), so seed those first. Idempotent.
        var hasEmployee = await db.Employees
            .IgnoreQueryFilters()
            .AnyAsync(e => e.TenantId == tenant.Id && e.UserId == user.Id, ct);

        if (!hasEmployee)
        {
            var department = await db.Departments
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(d => d.TenantId == tenant.Id && d.Code == "OPS", ct);
            if (department is null)
            {
                department = new Department
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = tenant.Id,
                    Name = "Operations",
                    Code = "OPS",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                };
                db.Departments.Add(department);
            }

            var jobTitle = await db.JobTitles
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(j => j.TenantId == tenant.Id && j.TitleName == "Administrator", ct);
            if (jobTitle is null)
            {
                jobTitle = new JobTitle
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = tenant.Id,
                    TitleName = "Administrator",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                };
                db.JobTitles.Add(jobTitle);
            }

            await db.SaveChangesAsync(ct);

            db.Employees.Add(new Employee
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenant.Id,
                EmployeeNo = "E2E-0001",
                FirstName = "E2E",
                LastName = "Owner",
                Email = E2EOwnerEmail,
                DateOfJoining = DateTime.UtcNow,
                DepartmentId = department.Id,
                JobTitleId = jobTitle.Id,
                EmploymentType = EmploymentType.FullTime,
                Status = EmployeeStatus.Active,
                UserId = user.Id,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded DEV E2E employee linked to owner user {Email}", user.Email);
        }
    }

    /// <summary>
    /// Seeds built-in tenant roles (Tenant Owner, Tenant Admin, HR Manager, HR Officer,
    /// Manager, Employee, Recruiter, Auditor) with their default permissions.
    /// These roles are marked as is_built_in and cannot be edited/deleted by tenants.
    /// Called during tenant provisioning.
    /// </summary>
    private static async Task SeedBuiltInTenantRolesAsync(
        AppDbContext db, Guid tenantId, ILogger logger, CancellationToken ct)
    {
        foreach (var roleName in PermissionCatalog.BuiltInRoles.All)
        {
            var exists = await db.Roles
                .IgnoreQueryFilters()
                .AnyAsync(r => r.TenantId == tenantId && r.Name == roleName, ct);

            if (exists)
                continue;

            var role = new Role
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                Name = roleName,
                Description = $"Built-in {roleName} role",
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow,
            };

            // Assign default permissions from the catalog
            var defaultPerms = PermissionCatalog.DefaultPermissionsFor(roleName);
            foreach (var perm in defaultPerms)
            {
                role.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    Permission = perm,
                });
            }

            db.Roles.Add(role);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded built-in role {Role} with {Count} permissions for tenant {TenantId}",
                roleName, defaultPerms.Count, tenantId);
        }
    }
}
