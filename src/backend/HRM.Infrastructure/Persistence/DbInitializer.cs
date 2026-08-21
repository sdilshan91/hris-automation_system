using System.Data;
using System.Data.Common;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Notifications;
using HRM.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HRM.Infrastructure.Persistence.Seed;

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

    /// <summary>
    /// BUG-307 — the plan code seeded tenants are given.
    /// </summary>
    /// <remarks>
    /// This used to be the literal <c>"default"</c>, which matches NO row in <c>subscription_plans</c> (whose
    /// codes are starter/professional/enterprise). Every plan-limit lookup therefore resolved to null and was
    /// read as "unlimited" — so the seeder itself manufactured the fail-open on every fresh deployment. It was
    /// not stale data; it was generated, three times in this file.
    ///
    /// <para><b>Why <c>enterprise</c> specifically.</b> Its <c>MaxEmployees</c> is NULL, i.e. genuinely
    /// unlimited. Repointing to it PRESERVES the effective behaviour these tenants already had (uncapped)
    /// while making it explicit and resolvable, instead of silently imposing a cap on a tenant that never had
    /// one. Behaviour-preserving is the right property for a migration that runs unattended at startup.</para>
    /// </remarks>
    private const string DefaultSeededPlanCode = "enterprise";

    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");

        await MigrateAsync(dbContext, logger, cancellationToken);

        // P3-4: one-time (idempotent) encryption of any pre-existing plaintext values in the sensitive PIP /
        // Recommendation columns that this release moved behind IFieldEncryptor. Runs AFTER Migrate (the schema +
        // the numeric→text column changes must exist first) and is a no-op once every value is already enc:v1:.
        var fieldEncryptor = scope.ServiceProvider.GetRequiredService<IFieldEncryptor>();
        await EncryptSensitiveFieldsAtRestAsync(dbContext, fieldEncryptor, logger, cancellationToken);

        // US-PLT-005 (Scope A): upgrade any LEGACY PLAINTEXT MFA secret (users.mfa_secret) to the
        // DataProtection-encrypted form. Runs alongside the field-encryption back-fill above and is likewise a
        // no-op once every row is already protected.
        var fieldProtector = scope.ServiceProvider.GetRequiredService<IFieldProtector>();
        await BackfillLegacyMfaSecretsAsync(dbContext, fieldProtector, logger, cancellationToken);

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
        // exist first. Gated + idempotent.
        // ⚠ GAP-L10 (2026-08-10): this used to say "a no-op on every current environment (Rls:Enabled=false)".
        // STALE — appsettings.json:20-22 ships "Rls": { "Enabled": true } with a fail-closed startup guard, and
        // the Docker dev stack sets Rls__Enabled=true. It is appsettings.Development.json that overrides it to
        // false, so DEV and CI run on two isolation layers while production runs on three. Do not read the
        // remaining "Rls:Enabled = false" mentions below as the shipped default; they describe that BRANCH
        // (Rls:Enabled=false everywhere).
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

    /// <summary>
    /// P3-4 — idempotent, one-time back-fill that encrypts any existing plaintext values in the encrypted-column
    /// set. The column list comes from the shared <see cref="Security.EncryptedFieldRegistry"/> (its
    /// <c>StartupBackfillFields</c> subset — behaviour-identical to the private list this replaced), the SAME
    /// registry the key-rotation re-encryption sweep consumes, so the two paths cannot drift (a future encrypted
    /// field added to one cannot silently miss the other — the DF-19 lesson). A raw SQL migration cannot run the
    /// app encryptor, so this runs in code after <see cref="MigrateAsync"/>: for each column it reads rows whose
    /// value is non-null and does NOT already start with <c>enc:v1:</c>, encrypts the raw value via
    /// <paramref name="encryptor"/>, and writes it back — so it is SAFE to run on every startup
    /// (already-encrypted values are skipped by the <c>NOT LIKE 'enc:v1:%'</c> filter). Tenant-agnostic (raw SQL
    /// over the base tables, no query filter). Relational-only: on the InMemory provider the value converters
    /// already encrypt on write and there is no distinct raw stored form to back-fill.
    /// </summary>
    public static async Task EncryptSensitiveFieldsAtRestAsync(
        AppDbContext db, IFieldEncryptor encryptor, ILogger logger, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return;

        var connection = db.Database.GetDbConnection();
        var openedHere = false;
        if (connection.State != ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(ct);
            openedHere = true;
        }

        try
        {
            var total = 0;
            foreach (var field in Security.EncryptedFieldRegistry.StartupBackfillFields)
            {
                total += await BackfillColumnAsync(connection, field.Table, field.Column, encryptor, ct);
            }

            if (total > 0)
            {
                logger.LogInformation(
                    "P3-4: encrypted {Count} pre-existing plaintext value(s) across the sensitive PIP/Recommendation "
                    + "columns (idempotent back-fill).", total);
            }
        }
        finally
        {
            if (openedHere)
                await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Encrypts every not-yet-encrypted value in one column. Table/column names come from the constant
    /// <see cref="Security.EncryptedFieldRegistry"/> (no external input — no injection surface); the value is
    /// parameterized.
    /// </summary>
    private static async Task<int> BackfillColumnAsync(
        DbConnection connection, string table, string column, IFieldEncryptor encryptor, CancellationToken ct)
    {
        var pending = new List<(object Id, string Raw)>();

        await using (var select = connection.CreateCommand())
        {
            select.CommandText =
                $"SELECT id, {column} FROM {table} WHERE {column} IS NOT NULL AND {column} NOT LIKE 'enc:v1:%'";
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                pending.Add((reader.GetValue(0), reader.GetString(1)));
            }
        }

        foreach (var (id, raw) in pending)
        {
            await using var update = connection.CreateCommand();
            update.CommandText = $"UPDATE {table} SET {column} = @val WHERE id = @id";

            var valueParam = update.CreateParameter();
            valueParam.ParameterName = "val";
            valueParam.Value = encryptor.Encrypt(raw)!;
            update.Parameters.Add(valueParam);

            var idParam = update.CreateParameter();
            idParam.ParameterName = "id";
            idParam.Value = id;
            update.Parameters.Add(idParam);

            await update.ExecuteNonQueryAsync(ct);
        }

        return pending.Count;
    }

    /// <summary>
    /// US-PLT-005 (Scope A) — idempotent startup back-fill that UPGRADES any legacy plaintext MFA secret to the
    /// DataProtection-encrypted form. Motivation: <see cref="IFieldProtector.Unprotect"/> deliberately TOLERATES
    /// legacy plaintext (it returns an undecryptable value unchanged so a pre-encryption enrollment still
    /// verifies) — but that tolerance means a plaintext secret would otherwise stay plaintext at rest FOREVER,
    /// because nothing ever re-wraps it. This pass closes that gap: for every user whose stored secret is not
    /// already protected it writes back the <see cref="IFieldProtector.Protect"/>ed form, so a raw DB read can no
    /// longer disclose a TOTP secret.
    ///
    /// <para>SAFE to run on EVERY startup — that is the whole point of <see cref="IFieldProtector.IsProtected"/>:
    /// an already-encrypted row is skipped, so the pass never double-wraps and becomes a no-op once healed.</para>
    ///
    /// <para><c>users</c> is a GLOBAL table — <c>User</c> is NOT a <c>BaseEntity</c> and has no <c>tenant_id</c>,
    /// so there is deliberately NO tenant predicate (the MFA secret is tenant-agnostic identity data). Unlike the
    /// sibling <see cref="EncryptSensitiveFieldsAtRestAsync"/> back-fill (raw SQL filtered by an <c>enc:v1:</c>
    /// prefix), this one uses EF change-tracking and decides membership via <see cref="IFieldProtector.IsProtected"/>
    /// in code, because the DataProtection payload is opaque — there is no prefix to filter on in SQL. It is NOT
    /// gated to relational providers: <c>mfa_secret</c> has no EF value converter, so even the InMemory store holds
    /// a genuine raw plaintext form that needs the same heal. The row set is only the MFA-enrolled users
    /// (<c>MfaSecret != null</c>), which is small, so a single materialized list matches the sibling's idiom.</para>
    /// </summary>
    public static async Task BackfillLegacyMfaSecretsAsync(
        AppDbContext db, IFieldProtector protector, ILogger logger, CancellationToken ct)
    {
        // Empty is treated exactly like null — "no secret at all". Protecting "" would replace a visibly-empty
        // value with an opaque blob that merely decrypts back to "", obscuring the anomaly and counting it as
        // "healed" while healing nothing. Kept in lock-step with the same filter in
        // FieldEncryptionMaintenanceService.CountLegacyPlaintextMfaSecretsAsync so the gauge and the back-fill
        // never disagree about which rows are in scope.
        var mfaUsers = await db.Users
            .IgnoreQueryFilters()
            .Where(u => u.MfaSecret != null && u.MfaSecret != "")
            .ToListAsync(ct);

        var upgraded = 0;
        var skippedTooLong = 0;
        foreach (var user in mfaUsers)
        {
            if (protector.IsProtected(user.MfaSecret!))
                continue;

            var protectedValue = protector.Protect(user.MfaSecret!);

            // Boot-safety guard. This runs during application startup, so an over-long value must NOT be
            // allowed to reach SaveChanges: Postgres would throw "value too long for type character
            // varying(512)" and take the whole service down on boot — turning a single malformed row into a
            // total outage. Skipping instead leaves that one secret plaintext (no worse than before this
            // back-fill existed) while the service starts, and the row stays visible in the encryption report's
            // MfaSecretsLegacyPlaintext count, so it is degraded-and-observable rather than silent.
            //
            // Expected to be unreachable in practice: legacy plaintext predates the column widening (200 -> 512,
            // migration 20260708055825) so it is <= 200 chars, which protects to ~370. The guard exists because
            // that is an inference about historical data, not something the code can verify.
            if (protectedValue.Length > UserConfiguration.MfaSecretMaxLength)
            {
                logger.LogError(
                    "US-PLT-005: cannot upgrade legacy MFA secret for user {UserId} — the protected payload is "
                    + "{ActualLength} chars, over the {MaxLength}-char users.mfa_secret column. Left as legacy "
                    + "plaintext so startup can continue; it remains counted by the encryption report. Widen the "
                    + "column to heal this row.",
                    user.Id, protectedValue.Length, UserConfiguration.MfaSecretMaxLength);
                skippedTooLong++;
                continue;
            }

            user.MfaSecret = protectedValue;
            upgraded++;
        }

        if (upgraded > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "US-PLT-005: upgraded {Count} legacy plaintext MFA secret(s) to encrypted-at-rest storage "
                + "(idempotent back-fill).", upgraded);
        }

        if (skippedTooLong > 0)
        {
            logger.LogWarning(
                "US-PLT-005: {Count} legacy MFA secret(s) could not be upgraded because the protected payload "
                + "exceeds the column width. See the preceding errors.", skippedTooLong);
        }
    }

    private static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        // US-ADM-001: seed the default subscription plans tenant provisioning selects from.
        await SeedSubscriptionPlansAsync(db, logger, ct);

        // US-NTF-002: seed the platform system-default email templates for every catalog event (BR-2).
        await SeedSystemNotificationTemplatesAsync(db, logger, ct);

        // ISSUE-335: this MUST come from PlanModules — the canonical product-module vocabulary — and not from
        // PermissionCatalog.ByModule.Keys, which is a list of PERMISSION PREFIXES (Audit, CustomField, Roles,
        // Tenant, ...). The two sets overlap enough to look right and are not interchangeable: the permission
        // list has no CoreHR/Asset/CustomReportBuilder/PublicCareersPage and calls Reporting "Reports". Because
        // nothing read enabled_modules until US-ADM-012, the mismatch sat in seeded data undetected; a module
        // gate reading it would have denied every request for the seeded tenants. PlanModulesSeedDriftTests
        // pins this.
        var defaultEnabledModules = PlanModules.All.ToList();
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
                PlanId = DefaultSeededPlanCode,
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
                tenant.PlanId = DefaultSeededPlanCode;
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
        // GAP-004: AuditLogRetentionDays per tier, from the technical doc's plan matrix
        // ("Audit log retention (days) | 90 / 365 / 7y") and §19.13 ("7 years (configurable; some plans =
        // 90 days/1 year)"). These were previously unset, so all three tiers fell to the entity default of
        // 90 and an Enterprise tenant paying for 7-year retention was purged at 90 days.
        const int RetentionStarter = 90;
        const int RetentionProfessional = 365;
        const int RetentionEnterprise = 2555;   // 7 years

        var defaults = new (string Name, string Code, decimal PriceMonthly, int TrialDays, int? MaxEmployees,
            int AuditLogRetentionDays)[]
        {
            ("Starter", "starter", 0m, 30, 25, RetentionStarter),
            ("Professional", "professional", 49m, 14, 200, RetentionProfessional),
            ("Enterprise", "enterprise", 199m, 0, null, RetentionEnterprise),
        };

        var existingCodes = await db.SubscriptionPlans
            .Select(p => p.Code)
            .ToListAsync(ct);
        var existing = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var (name, code, price, trialDays, maxEmployees, auditLogRetentionDays) in defaults)
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
                AuditLogRetentionDays = auditLogRetentionDays,
                // DF-5/BR-6: seed the historical default of 2 template language variants so existing behaviour
                // is preserved for freshly-seeded plans. The consuming service also falls back to 2 when unset.
                MaxTemplateLanguageVariants = 2,
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
            await BackfillCustomRoleReportScopeAsync(db, tenantId, logger, ct);
            await EnsureDefaultShiftAsync(db, tenantId, logger, ct);
            await EnsureDefaultLeaveWorkflowAsync(db, tenantId, logger, ct);
            await EnsureResolvablePlanIdAsync(db, tenantId, logger, ct);
        }
    }

    /// <summary>
    /// BUG-307 — repoints a tenant whose <c>plan_id</c> matches no subscription plan, loudly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An unresolvable <c>plan_id</c> made every plan limit resolve to null, which the call sites read as
    /// "unlimited" — a revenue rule failing OPEN with no error and no log. Measured before the fix: 2 of 3
    /// tenants were in this state, seeded that way by this very file.
    /// </para>
    /// <para>
    /// <b>Repoints to <see cref="DefaultSeededPlanCode"/> (enterprise, MaxEmployees = NULL), which PRESERVES
    /// the uncapped behaviour these tenants already had.</b> Capping them here would be a silent, unattended
    /// downgrade of a live tenant — the opposite mistake, and a worse one. The goal is to make the existing
    /// state explicit and enforceable, not to make a pricing decision at startup.
    /// </para>
    /// <para>
    /// WARNING, not Information: reaching this branch means something upstream wrote a plan code that does not
    /// exist, and that cause is worth seeing rather than silently repaired.
    /// </para>
    /// </remarks>
    public static async Task EnsureResolvablePlanIdAsync(
        AppDbContext db, Guid tenantId, ILogger logger, CancellationToken ct)
    {
        var tenant = await db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null)
            return;

        var resolves = await db.SubscriptionPlans.AsNoTracking()
            .AnyAsync(p => p.Code == tenant.PlanId, ct);
        if (resolves)
            return;

        var previous = tenant.PlanId;
        tenant.PlanId = DefaultSeededPlanCode;
        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "Tenant {TenantId} had plan_id '{PreviousPlanId}', which matches no subscription plan — every "
            + "plan limit was silently resolving to unlimited (BUG-307). Repointed to '{NewPlanId}', which "
            + "preserves the uncapped behaviour it already had while making it explicit and enforceable.",
            tenantId, previous, DefaultSeededPlanCode);
    }

    /// <summary>
    /// GAP-029 / C1 — backfills the default leave-approval workflow onto a tenant that has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Seeding only at provisioning would split tenants into two populations with different approval
    /// mechanics, distinguishable solely by signup date and with nothing in the UI to explain it. This runs
    /// for every existing tenant so there is ONE behaviour everywhere.
    /// </para>
    /// <para>
    /// <b>Safe for in-flight work.</b> <c>LeaveRequest.WorkflowInstanceId</c> is assigned at SUBMIT time, so
    /// requests already pending keep it null and continue down the legacy path untouched. Only submissions
    /// made after this runs route through the engine.
    /// </para>
    /// <para>
    /// <b>Public</b> so the backfill is directly testable, matching this file's existing convention for
    /// reconcilers (<see cref="ReconcileRowLevelSecurityAsync"/>, <see cref="BackfillLegacyMfaSecretsAsync"/>).
    /// A backfill that can only be reached through full startup is a backfill nobody tests.
    /// </para>
    /// <para>
    /// <b>Skips on ANY existing Leave definition, not just an Active one.</b> A tenant whose admin is
    /// part-way through authoring a Draft has expressed intent; dropping a seeded Active definition beside
    /// it would silently win the runtime lookup and route approvals through config the admin never
    /// finished. Absence of a definition is the only safe signal to act on.
    /// </para>
    /// </remarks>
    public static async Task EnsureDefaultLeaveWorkflowAsync(
        AppDbContext db, Guid tenantId, ILogger logger, CancellationToken ct)
    {
        var hasAnyLeaveWorkflow = await db.WorkflowDefinitions
            .IgnoreQueryFilters()
            .AnyAsync(w => w.TenantId == tenantId
                           && w.EntityType == WorkflowEntityType.Leave
                           && !w.IsDeleted, ct);
        if (hasAnyLeaveWorkflow)
            return;

        var definition = DefaultLeaveWorkflow.Build(tenantId, DateTime.UtcNow, "system-seed");
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Seeded the default leave-approval workflow for tenant {TenantId} (GAP-029): leave approvals now "
            + "route through the US-ADM-011 engine instead of the legacy single-level path.",
            tenantId);
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
    /// ISSUE-291 — DEC-1 continuity backfill for CUSTOM (tenant-defined) roles.
    ///
    /// DEC-1 introduced dedicated report row-scope permissions (<see cref="PermissionCatalog.Reports.ViewAll"/> /
    /// <see cref="PermissionCatalog.Reports.ViewTeam"/>) and changed the report scope resolvers so they now REQUIRE
    /// these explicit perms — previously the "All" bucket BORROWED Employee/Leave/Attendance.View.All and the "Team"
    /// bucket was purely data-derived from having a direct report. The built-in-role reconcile
    /// (<see cref="ReconcileBuiltInRolePermissionsAsync"/>) backfills built-in roles automatically, but a CUSTOM
    /// role that relied on the old borrowed-perm behaviour would silently lose its report scope. This method
    /// restores that scope once, behaviour-preservingly, from the role's ACTUAL held cross-module view perms.
    ///
    /// It re-introduces an IMPLICIT grant BY DESIGN — a deliberate, user-accepted tradeoff (upgrade continuity over
    /// a strict "no implicit grants" stance) — and pairs with the <c>docs/DEV/UPGRADE-NOTES.md</c> release note that
    /// tells admins to explicitly grant the perm to any custom role the inference could not reach.
    ///
    /// Only ADDS, only to CUSTOM roles that already hold the <see cref="PermissionCatalog.Reports.View"/> endpoint
    /// gate (a role that can't reach report endpoints gets nothing — same reasoning as DEC-1's Recruiter carve-out).
    /// Idempotent by construction (each grant is guarded by a "does not already contain" check), so it is safe to
    /// run on every startup with no separate tracking flag. Built-in roles are handled by the reconcile above and
    /// are deliberately excluded here.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> (not <c>private</c>) so the mapping is unit-testable directly via
    /// <c>InternalsVisibleTo("HRM.Tests")</c> — same pattern as <c>ConnectionRoutingInterceptor</c>.
    /// </remarks>
    internal static async Task BackfillCustomRoleReportScopeAsync(
        AppDbContext db, Guid tenantId, ILogger logger, CancellationToken ct)
    {
        var roles = await db.Roles
            .IgnoreQueryFilters()
            .Include(r => r.RolePermissions)
            .Where(r => r.TenantId == tenantId && !r.IsBuiltIn)
            .ToListAsync(ct);

        var added = 0;
        foreach (var role in roles)
        {
            var held = role.RolePermissions.Select(rp => rp.Permission).ToHashSet();

            // Gate: only roles that can actually reach report endpoints are considered (avoids inert grants).
            if (!held.Contains(PermissionCatalog.Reports.View))
                continue;

            string? grant = null;

            // "All" wins over "Team": a role that qualifies for org-wide scope gets All, never Team.
            var hasAllSignal = held.Contains(PermissionCatalog.Employee.ViewAll)
                || held.Contains(PermissionCatalog.Leave.ViewAll)
                || held.Contains(PermissionCatalog.Attendance.ViewAll);
            var hasTeamSignal = held.Contains(PermissionCatalog.Employee.ViewTeam)
                || held.Contains(PermissionCatalog.Leave.ViewTeam)
                || held.Contains(PermissionCatalog.Attendance.ViewTeam);

            if (hasAllSignal && !held.Contains(PermissionCatalog.Reports.ViewAll))
                grant = PermissionCatalog.Reports.ViewAll;
            else if (!hasAllSignal && hasTeamSignal && !held.Contains(PermissionCatalog.Reports.ViewTeam))
                grant = PermissionCatalog.Reports.ViewTeam;

            if (grant is not null)
            {
                role.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = grant });
                added++;
            }
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "ISSUE-291: backfilled {Count} custom-role report-scope permission(s) for tenant {TenantId}",
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
        // ISSUE-335: canonical product modules, NOT permission prefixes — see the note on the platform-tenant
        // seed above. The E2E tenant is the one the Playwright suite drives, so seeding the wrong vocabulary
        // here would break every E2E run the moment module gating ships.
        var enabledModules = PlanModules.All.ToList();

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
                PlanId = DefaultSeededPlanCode,
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
