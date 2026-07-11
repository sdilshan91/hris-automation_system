// ============================================================================
// ISSUE-268 — RLS-on regression proof for the fresh-scope notification writes.
//
// THE BUG: SignalRNotificationService.CreateAndDispatchAsync and
// RealNotificationDispatcher.SendEmailAsync open their OWN DI scope and SaveChanges on a NEW AppDbContext /
// pooled connection. Under RLS-on that connection is the NOBYPASSRLS hrm_app role, but the app.current_tenant
// GUC is set only on the CALLER's DbContext (by TenantTransactionBehavior / TenantJobRunner) — so the fresh
// connection has NO GUC and the strict WITH CHECK tenant_isolation policy REJECTS the INSERT (SQLSTATE 42501,
// fail-closed).
//
// THE FIX: route each fresh-scope DB write through ITenantJobRunner.RunForTenantAsync (the exact primitive the
// ~40 Hangfire jobs already use — SendEmailJob is the direct sibling). The runner sets the GUC on THAT scope's
// AppDbContext inside a retry-safe tx when Rls:Enabled, and no-ops under Rls:Enabled=false / InMemory.
//
// WHY REAL POSTGRES: RLS is a DB-engine feature the EF InMemory provider does not implement. The only honest
// proof connects as the NON-superuser, NON-BYPASSRLS hrm_app role with the policies ENABLED + FORCED (a run as
// the container's superuser 'postgres' would prove nothing — superusers always bypass RLS). This mirrors the
// harness in RlsIsolationPostgresTests.cs.
//
// The load-bearing assertions:
//   • the REAL writers, driven through the runner, PERSIST the row under RLS-on (they would throw 42501 without
//     the fix);
//   • a BARE fresh-scope INSERT (no runner, no GUC) on hrm_app DOES throw 42501 — pinning the exact mechanism
//     the fix addresses;
//   • the persisted row is tenant-scoped: it carries the right tenant_id and a second tenant's GUC cannot read it.
//
// Note on connection routing: this fixture wires AppDbContext DIRECTLY to the hrm_app connection (as
// RlsIsolationPostgresTests test #9 does) rather than through ConnectionRoutingInterceptor. The interceptor
// routes an UNRESOLVED ambient to the privileged hrm_owner role — which would BYPASS RLS and mask the 42501
// bare-insert repro. Pinning hrm_app is the spec's accepted narrower proof and keeps the mechanism honest: the
// runner-wrapped write satisfies WITH CHECK, the bare write does not.
// ============================================================================

using FluentAssertions;
using HRM.Api.Hubs;
using HRM.Api.Notifications;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Hangfire;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-NTF-268-RLS")]
[Trait("Category", "RlsIsolation")]
public sealed class NotificationRlsPostgresTests : IAsyncLifetime
{
    private const string AppRole = "hrm_app";
    private const string AppPassword = "app_pw_ntf_268";
    private const string OwnerRole = "hrm_owner";
    private const string OwnerPassword = "owner_pw_ntf_268";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private string _appConnString = null!;   // connects as hrm_app   (RLS ENFORCED)
    private string _ownerConnString = null!; // connects as hrm_owner (BYPASSRLS)

    // Drives the EF global query filter for migration/seed only. Real services below use the production TenantContext.
    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "acme";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status, string? plan = null,
            IReadOnlyCollection<string>? enabledModules = null, string? logoUrl = null, string? primaryColor = null)
            => TenantId = tenantId;
        public void SetSystemContext() { }
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var superCs = _postgres.GetConnectionString();
        _appConnString = WithRole(superCs, AppRole, AppPassword);
        _ownerConnString = WithRole(superCs, OwnerRole, OwnerPassword);

        // (1) Provision the two production roles as the superuser (mirrors roles.sql).
        await ExecAsync(superCs,
            $"""
             DO $r$
             BEGIN
                 IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                     CREATE ROLE {AppRole} LOGIN PASSWORD '{AppPassword}';
                 END IF;
                 IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{OwnerRole}') THEN
                     CREATE ROLE {OwnerRole} LOGIN PASSWORD '{OwnerPassword}' BYPASSRLS;
                 END IF;
             END
             $r$;
             ALTER ROLE {AppRole} NOBYPASSRLS;
             """);

        // (2) Apply migrations (as the superuser/owner) so the schema + DORMANT policies exist.
        await using (var migrate = OwnerAwareDb(superCs, new MutableTenantContext()))
        {
            await migrate.Database.MigrateAsync();
        }

        // Grants for the app + owner roles.
        await ExecAsync(superCs,
            $"""
             GRANT USAGE ON SCHEMA public TO {AppRole}, {OwnerRole};
             GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRole}, {OwnerRole};
             GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {AppRole}, {OwnerRole};
             """);

        // (3) Simulate the increment-3 reconciler: ENABLE + FORCE RLS on every tenant_id table (excl. users/tenants).
        //     This includes `notification` + `notification_delivery` (both carry tenant_id) — the tables under test.
        await ExecAsync(superCs,
            """
            DO $e$
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
            $e$;
            """);

        // Sanity: the two tables under test really are policied (else the whole proof is vacuous).
        (await IsPoliciedAsync("notification")).Should().BeTrue("the notification table must carry the tenant_isolation policy");
        (await IsPoliciedAsync("notification_delivery")).Should().BeTrue("the notification_delivery table must carry the tenant_isolation policy");

        // (4) Seed the two tenants as the superuser (bypasses RLS + WITH CHECK), mirroring the privileged seed path.
        await using var db = OwnerAwareDb(superCs, new MutableTenantContext());
        db.Tenants.Add(new Tenant { Id = _tenantA, Subdomain = "tenant-a", Name = "Tenant A" });
        db.Tenants.Add(new Tenant { Id = _tenantB, Subdomain = "tenant-b", Name = "Tenant B" });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ─────────────────────────────────────────────────────────────────────────
    // 1. PIN THE MECHANISM — a BARE fresh-scope Notification INSERT on hrm_app with NO GUC is rejected by the
    //    strict WITH CHECK tenant_isolation policy (SQLSTATE 42501). This is exactly what the fresh-scope write
    //    did BEFORE the fix.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task BareFreshScopeInsert_NoGuc_OnAppRole_IsRejected_42501()
    {
        await using var db = OwnerAwareDb(_appConnString, new MutableTenantContext { TenantId = _tenantA });
        db.Notifications.Add(NewNotification(_tenantA));

        var act = async () => await db.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege,
                "an RLS WITH CHECK violation on the GUC-less hrm_app connection surfaces as SQLSTATE 42501");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. THE FIX (in-app leg) — the REAL SignalRNotificationService.CreateAndDispatchAsync, driven under RLS-on,
    //    PERSISTS the notification row (it would throw 42501 without the runner). Then the row is tenant-scoped:
    //    it carries tenant A's id and tenant B's GUC cannot read it.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task SignalRNotificationService_UnderRlsOn_PersistsRow_TenantScoped()
    {
        var scopeFactory = BuildRlsScopeFactory();
        var service = new SignalRNotificationService(
            scopeFactory, BuildNoopHub(), NullLogger<SignalRNotificationService>.Instance);

        var recipient = Guid.NewGuid();
        var id = await service.CreateAndDispatchAsync(
            _tenantA, recipient, "leave_approved", "Leave approved", "Your leave was approved");

        // Persisted (the whole point — a 42501 here means the runner did not set the GUC).
        (await RawCountAsync(_appConnString, _tenantA, "notification", $"id = '{id}'")).Should().Be(1);

        // Tenant-scoped: the row belongs to A, and B's GUC cannot see it.
        (await RawTenantIdAsync(id)).Should().Be(_tenantA);
        (await RawCountAsync(_appConnString, _tenantB, "notification", $"id = '{id}'")).Should().Be(0,
            "tenant B's GUC must not read tenant A's notification");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. THE FIX (email leg) — the REAL RealNotificationDispatcher.SendEmailAsync, driven under RLS-on, PERSISTS
    //    the NotificationDelivery row through the runner (it would throw 42501 without the fix), tenant-scoped.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RealNotificationDispatcher_UnderRlsOn_PersistsDelivery_TenantScoped()
    {
        var templates = Substitute.For<IEmailTemplateService>();
        templates.ResolveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedEmailTemplate>.Success(
                new ResolvedEmailTemplate("evt", "en", "Subj", "<b>Body</b>", "Body", false, 1, null)));
        templates.Render(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object?>>())
            .Returns(new RenderedEmail("Subj", "<b>Body</b>", "Body"));

        var scopeFactory = BuildRlsScopeFactory(templates);
        var dispatcher = new RealNotificationDispatcher(
            scopeFactory,
            Substitute.For<INotificationService>(),          // the in-app leg is not exercised here
            Substitute.For<IBackgroundJobClient>(),          // Enqueue<SendEmailJob> lowers to Create(job,state)
            NullLogger<RealNotificationDispatcher>.Instance);

        // Raw-email recipient (no user id) ⇒ the per-user preference gate is skipped; the template resolves via the
        // substitute; a Queued NotificationDelivery row is written through the runner and the send job enqueued.
        var request = new NotificationRequest(
            _tenantA, "test.event", "{}", RecipientUserId: null, RecipientEmail: "raw@example.com");

        await dispatcher.SendEmailAsync(request);

        // A delivery row persisted for tenant A (a 42501 inside WriteDeliveryAsync would have thrown out of the call).
        (await RawCountAsync(_appConnString, _tenantA, "notification_delivery", "recipient_email = 'raw@example.com'"))
            .Should().Be(1);
        // Tenant-scoped: B's GUC cannot see it.
        (await RawCountAsync(_appConnString, _tenantB, "notification_delivery", "recipient_email = 'raw@example.com'"))
            .Should().Be(0, "tenant B's GUC must not read tenant A's delivery row");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. THE FIX (session-activity leg — ISSUE-268 Fix 3) — SessionActivityMiddleware's fire-and-forget task does
    //    an IgnoreQueryFilters() UPDATE on refresh_tokens via AuthService.UpdateSessionActivityAsync. Its RLS
    //    failure mode is WORSE than the notification INSERTs: IgnoreQueryFilters does NOT bypass DB RLS, so on a
    //    GUC-less hrm_app connection the SELECT-then-UPDATE matches 0 rows and SILENTLY no-ops (no 42501 to catch).
    //    This arm pins that: without the runner the row is untouched; through the runner (GUC set) it is updated.
    //    Mirrors the exact production logic in AuthService.UpdateSessionActivityAsync (SELECT active token by id,
    //    stamp LastActiveAt, SaveChanges) — driving the same IgnoreQueryFilters() UPDATE per the coordinator's note.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task SessionActivityUpdate_UnderRlsOn_RequiresRunner_ToHitRows()
    {
        // Seed a user (refresh_tokens has an FK to users) + an active refresh_tokens row for tenant A via the
        // BYPASSRLS owner (LastActiveAt starts null).
        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using (var owner = OwnerAwareDb(_ownerConnString, new MutableTenantContext { TenantId = _tenantA }))
        {
            owner.Users.Add(new User { Id = userId, Email = $"{userId}@example.com" });
            owner.RefreshTokens.Add(new RefreshToken
            {
                Id = sessionId,
                TenantId = _tenantA,
                UserId = userId,
                TokenHash = $"hash-{sessionId}",
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                LastActiveAt = null,
            });
            await owner.SaveChangesAsync();
        }

        // BARE arm (mechanism): a GUC-less hrm_app scope. The RLS USING clause hides the row from the SELECT, so
        // the update is a silent no-op — no exception, LastActiveAt stays null.
        await using (var bare = OwnerAwareDb(_appConnString, new MutableTenantContext { TenantId = _tenantA }))
        {
            // The row is invisible to the GUC-less app session (fail-closed read) …
            (await bare.RefreshTokens.IgnoreQueryFilters()
                    .AnyAsync(rt => rt.Id == sessionId && rt.RevokedAt == null))
                .Should().BeFalse("no GUC on hrm_app ⇒ RLS hides the row even with IgnoreQueryFilters()");
            await UpdateSessionActivityAsync(bare, sessionId);
        }
        (await RawLastActiveAsync(sessionId)).Should().BeNull(
            "without the runner the fresh-scope UPDATE matches 0 rows and silently no-ops under RLS-on");

        // FIXED arm (proof): the same update through ITenantJobRunner sets the GUC, so the row is visible + updated.
        using (var scope = BuildRlsScopeFactory().CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
            await runner.RunForTenantAsync(_tenantA, $"tenant-{_tenantA}",
                ct => UpdateSessionActivityAsync(db, sessionId, ct));
        }
        (await RawLastActiveAsync(sessionId)).Should().NotBeNull(
            "the runner set the GUC ⇒ the row is visible ⇒ LastActiveAt is stamped (1 row affected)");
    }

    // Faithful replica of AuthService.UpdateSessionActivityAsync (SELECT active token by id, stamp, SaveChanges).
    private static async Task UpdateSessionActivityAsync(AppDbContext db, Guid sessionId, CancellationToken ct = default)
    {
        var token = await db.RefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(rt => rt.Id == sessionId && rt.RevokedAt == null, ct);
        if (token is not null)
        {
            token.LastActiveAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    // ── real-service DI graph (RLS-on, hrm_app) ────────────────────────────────

    // A DI provider whose scopes resolve the SAME-scope AppDbContext (hrm_app) + production TenantContext +
    // TenantJobRunner, exactly as the fixed writers do (they resolve ITenantJobRunner + AppDbContext from their
    // own fresh scope). Rls:Enabled=true ⇒ the runner sets the app.current_tenant GUC in a retry-safe tx.
    private IServiceScopeFactory BuildRlsScopeFactory(IEmailTemplateService? templates = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Rls:Enabled"] = "true" })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITenantJobRunner, TenantJobRunner>();
        // AppDbContext hardwired to hrm_app (RLS enforced); shares the scope's ITenantContext for the query filter.
        services.AddScoped(sp => OwnerAwareDb(_appConnString, sp.GetRequiredService<ITenantContext>()));
        if (templates is not null)
            services.AddSingleton(templates);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static IHubContext<NotificationHub> BuildNoopHub()
    {
        var clientProxy = Substitute.For<IClientProxy>();
        var clients = Substitute.For<IHubClients>();
        clients.Group(Arg.Any<string>()).Returns(clientProxy);
        var hub = Substitute.For<IHubContext<NotificationHub>>();
        hub.Clients.Returns(clients);
        return hub;
    }

    private static Notification NewNotification(Guid tenantId) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = tenantId,
        UserId = Guid.NewGuid(),
        Type = "test.type",
        Title = "Title",
        Message = "Message",
        IsRead = false,
        CreatedAt = DateTime.UtcNow,
    };

    // ── harness helpers (mirror RlsIsolationPostgresTests) ─────────────────────

    private static string WithRole(string baseConnString, string user, string password) =>
        new NpgsqlConnectionStringBuilder(baseConnString) { Username = user, Password = password }.ToString();

    private static async Task ExecAsync(string connString, string sql)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private AppDbContext OwnerAwareDb(string connString, ITenantContext tc) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connString, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options, tc);

    private async Task<bool> IsPoliciedAsync(string table)
    {
        await using var conn = new NpgsqlConnection(_ownerConnString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM pg_policies WHERE schemaname = 'public' AND policyname = 'tenant_isolation' AND tablename = @t",
            conn);
        cmd.Parameters.AddWithValue("t", table);
        return (long)(await cmd.ExecuteScalarAsync())! > 0;
    }

    // Raw count on hrm_app inside a GUC-set transaction (so RLS is in force), with an extra predicate.
    private static async Task<long> RawCountAsync(string connString, Guid guc, string table, string predicate)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var set = new NpgsqlCommand("SELECT set_config('app.current_tenant', @t, true)", conn, tx))
        {
            set.Parameters.AddWithValue("t", guc.ToString());
            await set.ExecuteNonQueryAsync();
        }
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE {predicate}", conn, tx);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        await tx.CommitAsync();
        return count;
    }

    // Read a notification's tenant_id via the privileged (BYPASSRLS) owner role, to confirm the stamped tenant.
    private async Task<Guid> RawTenantIdAsync(Guid notificationId)
    {
        await using var conn = new NpgsqlConnection(_ownerConnString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT tenant_id FROM notification WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", notificationId);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    // Read a refresh token's last_active_at via the privileged (BYPASSRLS) owner role, unaffected by RLS.
    private async Task<DateTime?> RawLastActiveAsync(Guid sessionId)
    {
        await using var conn = new NpgsqlConnection(_ownerConnString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT last_active_at FROM refresh_tokens WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", sessionId);
        return await cmd.ExecuteScalarAsync() is DateTime dt ? dt : null;
    }
}
