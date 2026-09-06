// ============================================================================
// ISSUE-369 regression: the create/update RESPONSE must describe the row as the
// DATABASE holds it, not the request that was sent.
//
// SalaryComponentService built the entity in memory, saved it, then projected the
// SAME tracked instance (ToDto(component)). salary_component.default_value is
// numeric(18,2), so Postgres rounds a higher-scale value on write — and the 201/200
// body still echoed the caller's own number back. The client's record of what it
// created was wrong, silently.
//
// WHY POSTGRES, NOT InMemory. The defect IS the column's scale. The EF InMemory
// provider stores a bare CLR decimal and applies no numeric(18,2) coercion, so an
// InMemory version of this file would pass identically with or without the fix —
// test theatre for exactly the bug class it claims to cover. Only a real numeric(18,2)
// column makes the in-memory entity and the persisted row disagree.
//
// PATH NOTE. The validators now reject >2dp at the command layer (see
// SalaryComponentValidatorTests), so the HTTP route can no longer reach this state.
// That is the primary defence; the service-level re-read proven here is the
// belt-and-braces for any caller that reaches the service directly (jobs, seeds,
// future commands) and for any other numeric column whose scale trims a value.
//
// Harness follows StatutoryRuleUpdatePostgresTests / HrReportPostgresTests: a shared
// PostgresContainerFixture (container + MigrateAsync once per CLASS — the per-class
// IAsyncLifetime shape costs ~20s per TEST because xUnit rebuilds the test class per
// method), fresh tenant Guid per test instance, real FKs so the Tenant row is seeded.
// UseSnakeCaseNamingConvention() is not optional — omitting it makes EF throw
// PendingModelChangesWarning against the migrated schema.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-PAY-001")]
public sealed class SalaryComponentPrecisionPostgresTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _pg;

    // Fresh per TEST (xUnit builds a new test-class instance per method), so sibling tests sharing this
    // database are isolated from each other by the tenant query filter.
    private readonly Guid _tenantId = Guid.NewGuid();

    public SalaryComponentPrecisionPostgresTests(PostgresContainerFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        await using var db = Db();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Subdomain = $"p{_tenantId:N}"[..14],
            Name = "ISSUE-369 tenant",
            Status = TenantStatus.Active,
        });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

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

    private AppDbContext Db()
    {
        var tc = new MutableTenantContext { TenantId = _tenantId };
        var cu = CurrentUser();
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_pg.ConnectionString, n =>
                {
                    n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    n.EnableRetryOnFailure(maxRetryCount: 3);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
                .Options,
            tc);
    }

    private static ICurrentUser CurrentUser()
    {
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        cu.Email.Returns("hr@acme.test");
        return cu;
    }

    private SalaryComponentService Service(AppDbContext db) =>
        new(db, new MutableTenantContext { TenantId = _tenantId }, CurrentUser(),
            Substitute.For<IPayrollAuditLogger>(), NullLogger<SalaryComponentService>.Instance);

    private static SalaryComponentInput Fixed(string code, decimal defaultValue) => new(
        Name: $"Component {code}",
        Code: code,
        Type: SalaryComponentType.Earning,
        CalculationMethod: CalculationMethod.Fixed,
        DefaultValue: defaultValue,
        FormulaExpression: null,
        IsTaxable: true,
        IsStatutory: false,
        IsActive: true,
        ProcessingOrder: 1);

    /// <summary>Re-reads default_value through a SEPARATE DbContext so no change tracker can answer for the row.</summary>
    private async Task<decimal?> PersistedDefaultValueAsync(Guid componentId)
    {
        await using var verify = Db();
        var row = await verify.SalaryComponents.AsNoTracking().FirstAsync(c => c.Id == componentId);
        return row.DefaultValue;
    }

    private static string Code() => $"C{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    // ── ISSUE-369: the response must not echo the request ────────────────────────

    [Fact]
    public async Task Create_WithMoreThanTwoDecimalPlaces_ReturnsThePersistedValue_NotTheRequestedOne()
    {
        await using var db = Db();

        var result = await Service(db).CreateAsync(Fixed(Code(), 1234.5678m));

        result.IsSuccess.Should().BeTrue();

        var persisted = await PersistedDefaultValueAsync(result.Value!.Id);
        persisted.Should().Be(1234.57m, "numeric(18,2) rounds the value on write");

        result.Value.DefaultValue.Should().Be(persisted,
            "the create response is the client's own record of what now exists — it must describe the stored row");
        result.Value.DefaultValue.Should().NotBe(1234.5678m,
            "echoing the request back told the client it had stored a value the database never held (ISSUE-369)");
    }

    [Fact]
    public async Task Update_WithMoreThanTwoDecimalPlaces_ReturnsThePersistedValue_NotTheRequestedOne()
    {
        await using var db = Db();
        var svc = Service(db);

        var code = Code();
        var created = await svc.CreateAsync(Fixed(code, 1000m));
        created.IsSuccess.Should().BeTrue();

        var result = await svc.UpdateAsync(created.Value!.Id, Fixed(code, 987.6543m));

        result.IsSuccess.Should().BeTrue();

        var persisted = await PersistedDefaultValueAsync(created.Value.Id);
        persisted.Should().Be(987.65m, "numeric(18,2) rounds the value on write");

        result.Value!.DefaultValue.Should().Be(persisted,
            "the update response must describe the stored row, not the submitted body");
        result.Value.DefaultValue.Should().NotBe(987.6543m,
            "UpdateAsync had the identical stale-projection defect as CreateAsync (ISSUE-369)");
    }

    [Fact]
    public async Task Create_WithTwoDecimalPlaces_ReturnsExactlyThePersistedValue()
    {
        await using var db = Db();

        var result = await Service(db).CreateAsync(Fixed(Code(), 1234.50m));

        result.IsSuccess.Should().BeTrue();

        // Asserted against a re-read, NOT against the request literal. Comparing to the literal is what let
        // the defect ship: that assertion is satisfied by the echo it was supposed to detect.
        var persisted = await PersistedDefaultValueAsync(result.Value!.Id);
        result.Value.DefaultValue.Should().Be(persisted);
        persisted.Should().Be(1234.50m, "a value already within the column's scale must survive the round-trip intact");
    }
}
