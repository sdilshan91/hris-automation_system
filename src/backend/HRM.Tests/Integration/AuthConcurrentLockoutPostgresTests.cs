// ============================================================================
// BUG-045 (HIGH, US-AUTH-010) regression — brute-force lockout bypass via
// concurrency.
//
// AuthService.LoginAsync increments failed_login_count with an in-memory
// read-modify-write:
//
//     user.FailedLoginCount++;              // read + increment in memory
//     ...
//     await _dbContext.SaveChangesAsync();  // write
//
// Under concurrency (N simultaneous wrong-password logins, each on its own
// DbContext/transaction) every request reads the SAME starting count before any
// commits, so the increments clobber each other ("lost update"). The counter
// lands at 1-2 instead of N, the 5-attempt threshold is never crossed, and the
// account is NEVER locked — the brute-force lockout is bypassable by simply
// parallelising the guesses.
//
// The fix makes the increment atomic at the DB (e.g. ExecuteUpdate
// `failed_login_count = failed_login_count + 1`), so all N increments are
// counted and the account locks.
//
// This race ONLY reproduces against real PostgreSQL with independent
// transactions per request. An EF InMemory provider serialises writes and
// would NOT reproduce the lost update — it would pass even against the buggy
// code (test theater). Hence this test spins up a real Postgres via
// Testcontainers and drives the real AuthService.LoginAsync on a separate
// DbContext per concurrent call, mirroring per-request scoping.
//
// Related test cases: TC-AUTH-098 (steps 4-5), TC-AUTH-026 (step 11).
// ============================================================================

using FluentAssertions;
using Hangfire;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Identity;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

/// <summary>
/// BUG-045 regression: the failed-login counter must increment atomically so the
/// brute-force lockout cannot be bypassed by firing login attempts concurrently.
/// </summary>
public sealed class AuthConcurrentLockoutPostgresTests : IAsyncLifetime
{
    private const int ConcurrentAttempts = 10; // N — well above the 5-attempt threshold
    private const int MaxFailedAttempts = 5;   // default lockout policy (5 attempts / 15 min)
    private const int LockoutDurationMinutes = 15;

    private const string Email = "alice@acme.com";
    private const string CorrectPassword = "C0rrectP@ss!";
    private const string WrongPassword = "Wr0ngP@ss!";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    // Shared singleton-scope collaborators (thread-safe; mirror app DI lifetimes).
    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "hrm-api-test",
            ["Jwt:Audience"] = "hrm-client-test",
            ["Platform:BaseDomain"] = "yourhrm.test",
        })
        .Build();

    private JwtService _jwtService = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _jwtService = new JwtService(_configuration);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    /// <summary>
    /// BUG-045: 10 concurrent wrong-password logins against a clean account must
    /// count all 10 increments and lock the account. Pre-fix (in-memory
    /// read-modify-write) the counter lands at 1-2 and the account is not locked,
    /// so the assertions below fail. Post-fix (atomic DB increment) they pass.
    /// </summary>
    [Fact]
    public async Task LoginAsync_ConcurrentWrongPasswords_AllIncrementsCounted_AccountLocks()
    {
        // Arrange: clean user (count=0, not locked) in a tenant with the default 5/15 policy.
        await SeedCleanUserAndTenantAsync();

        // Act: fire N wrong-password logins in parallel, each on its OWN DbContext/scope
        // (real per-request isolation) so the DB-level race is genuinely exercised. A gate
        // releases every task at once to maximise the contention window.
        using var gate = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, ConcurrentAttempts).Select(_ => Task.Run(async () =>
        {
            await using var db = CreateContext();
            var service = CreateAuthService(db);
            gate.Wait();
            await service.LoginAsync(Email, WrongPassword, null, "127.0.0.1", "xUnit", default);
        })).ToArray();

        // Give every task time to build its context and reach the gate, then release together.
        await Task.Delay(250);
        gate.Set();
        await Task.WhenAll(tasks);

        // Assert: read the user row back on a fresh context.
        await using var verifyDb = CreateContext();
        var user = await verifyDb.Users.AsNoTracking().FirstAsync(u => u.Id == _userId);

        // No lost increments — every one of the N concurrent failures is counted.
        user.FailedLoginCount.Should().Be(ConcurrentAttempts,
            "the failed-login counter must increment atomically at the DB; a lost-update race " +
            "(in-memory read-modify-write) would leave it at 1-2 and bypass the brute-force lockout (BUG-045)");

        // Account is locked (N > threshold) — the whole point of the counter.
        user.LockedUntil.Should().NotBeNull(
            "reaching the 5-attempt threshold must lock the account even when the attempts are concurrent (BUG-045)");
        user.LockedUntil!.Value.Should().BeAfter(DateTime.UtcNow);
    }

    // ---- Helpers ----------------------------------------------------------

    private AppDbContext CreateContext()
    {
        var tenantContext = new MutableTenantContext { TenantId = _tenantId };
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(false);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tenantContext), new AuditInterceptor(currentUser))
            .Options;
        return new AppDbContext(options, tenantContext);
    }

    private AuthService CreateAuthService(AppDbContext db)
    {
        var tenantContext = new MutableTenantContext { TenantId = _tenantId };
        return new AuthService(
            db,
            _jwtService,
            tenantContext,
            Substitute.For<ITotpService>(),
            _configuration,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<AuthService>.Instance,
            Substitute.For<IBackgroundJobClient>());
    }

    private async Task SeedCleanUserAndTenantAsync()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Acme",
            Subdomain = "acme",
            Status = TenantStatus.Active,
            MaxFailedAttempts = MaxFailedAttempts,
            LockoutDurationMinutes = LockoutDurationMinutes,
            ProgressiveLockoutEnabled = false,
            CreatedAt = DateTime.UtcNow,
        });

        db.Users.Add(new User
        {
            Id = _userId,
            Email = Email,
            DisplayName = "Alice",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(CorrectPassword, workFactor: 4),
            IsActive = true,
            FailedLoginCount = 0,
            LockedUntil = null,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
    }

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
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) => TenantId = tenantId;
        public void SetSystemContext() { }
    }
}
