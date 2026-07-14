// ============================================================================
// TAX-2: statutory exemptions persist on REAL Postgres (the new statutory_exemption child table + its columns
// are InMemory-masked by the fast gate). Applies migrations on postgres:17-alpine and asserts a rule's
// exemption children round-trip with the correct typed columns, and that the (tenant-filtered) table is
// queryable — the dormant tenant-isolation RLS policy itself is covered by RlsIsolationPostgresTests.
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
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class StatutoryExemptionPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync() => await _postgres.StartAsync();
    public async Task DisposeAsync() => await _postgres.DisposeAsync();

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

    private AppDbContext CreateContext(ITenantContext tc, ICurrentUser cu) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
            .Options, tc);

    private StatutoryRuleService BuildService(AppDbContext db, ITenantContext tc, ICurrentUser cu) =>
        new(db, tc, cu, Substitute.For<IStatutoryDeductionResolver>(),
            Substitute.For<IPayrollAuditLogger>(), NullLogger<StatutoryRuleService>.Instance);

    private (ITenantContext tc, ICurrentUser cu) Actors()
    {
        var tc = new MutableTenantContext { TenantId = _tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        return (tc, cu);
    }

    [Fact]
    public async Task CreateRuleWithExemptions_PersistsChildrenWithTypedColumns_OnPostgres()
    {
        var (tc, cu) = Actors();
        await using var db = CreateContext(tc, cu);
        await db.Database.MigrateAsync();

        var componentId = Guid.NewGuid();
        var input = new CreateStatutoryRuleInput(
            StatutoryRuleType.IncomeTax, "PAYE", "LK", "2026-2027",
            new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), true,
            new[] { new TaxSlabInput(0m, 250_000m, 0m, 0), new TaxSlabInput(250_000m, null, 6m, 1) },
            SocialSecurity: null,
            Exemptions: new[]
            {
                new ExemptionInput("Standard Deduction", ExemptionCalculationType.FlatAmount, 250_000m, null, null, IsAnnual: true, OrderIndex: 0),
                new ExemptionInput("Housing", ExemptionCalculationType.PercentOfComponent, 15m, componentId, 100_000m, IsAnnual: false, OrderIndex: 1),
            });

        var created = await BuildService(db, tc, cu).CreateAsync(input);
        created.IsSuccess.Should().BeTrue(created.Error);

        // Reload from a fresh context so we read what actually hit Postgres.
        await using var db2 = CreateContext(tc, cu);
        var rows = await db2.StatutoryExemptions.AsNoTracking()
            .Where(e => e.StatutoryRuleId == created.Value!.Id)
            .OrderBy(e => e.OrderIndex)
            .ToListAsync();

        rows.Should().HaveCount(2);

        rows[0].Name.Should().Be("Standard Deduction");
        rows[0].CalculationType.Should().Be(ExemptionCalculationType.FlatAmount);
        rows[0].Value.Should().Be(250_000m);
        rows[0].IsAnnual.Should().BeTrue();
        rows[0].ComponentId.Should().BeNull();
        rows[0].MaxAmount.Should().BeNull();

        rows[1].CalculationType.Should().Be(ExemptionCalculationType.PercentOfComponent);
        rows[1].Value.Should().Be(15m);
        rows[1].ComponentId.Should().Be(componentId);
        rows[1].MaxAmount.Should().Be(100_000m);
        rows[1].IsAnnual.Should().BeFalse();
        rows[1].TenantId.Should().Be(_tenantId); // stamped by the TenantInterceptor.
    }

    [Fact]
    public async Task UpdateRule_ReplacesExemptionChildren_OnPostgres()
    {
        var (tc, cu) = Actors();
        await using var db = CreateContext(tc, cu);
        await db.Database.MigrateAsync();

        var input = new CreateStatutoryRuleInput(
            StatutoryRuleType.IncomeTax, "PAYE", "LK", "2026-2027",
            new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), true,
            new[] { new TaxSlabInput(0m, null, 6m, 0) }, SocialSecurity: null,
            Exemptions: new[] { new ExemptionInput("Old", ExemptionCalculationType.FlatAmount, 100_000m, null, null, false, 0) });
        var created = await BuildService(db, tc, cu).CreateAsync(input);
        created.IsSuccess.Should().BeTrue(created.Error);

        await using var db2 = CreateContext(tc, cu);
        var update = new UpdateStatutoryRuleInput(
            "PAYE", "LK", "2026-2027", new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), true,
            new[] { new TaxSlabInput(0m, null, 6m, 0) }, SocialSecurity: null,
            Exemptions: new[]
            {
                new ExemptionInput("New A", ExemptionCalculationType.PercentOfGross, 5m, null, null, false, 0),
                new ExemptionInput("New B", ExemptionCalculationType.FlatAmount, 50_000m, null, null, false, 1),
            });
        (await BuildService(db2, tc, cu).UpdateAsync(created.Value!.Id, update)).IsSuccess.Should().BeTrue();

        await using var db3 = CreateContext(tc, cu);
        var rows = await db3.StatutoryExemptions.AsNoTracking()
            .Where(e => e.StatutoryRuleId == created.Value!.Id).OrderBy(e => e.OrderIndex).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Select(r => r.Name).Should().BeEquivalentTo(new[] { "New A", "New B" });
        rows.Should().NotContain(r => r.Name == "Old"); // the old child was replaced, not orphaned.
    }
}
