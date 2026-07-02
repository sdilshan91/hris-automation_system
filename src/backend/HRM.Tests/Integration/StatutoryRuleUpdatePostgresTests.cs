// ============================================================================
// BUG-073 reproduction/regression: updating a statutory rule with an (identical)
// body must succeed on real Postgres. The finding reported a 500 (attributed to a
// RowVersion mismatch), but StatutoryRule has no concurrency token — so this test
// reproduces the real failure on Postgres before/after the fix.
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

public sealed class StatutoryRuleUpdatePostgresTests : IAsyncLifetime
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

    private static CreateStatutoryRuleInput IncomeTaxRule() => new(
        StatutoryRuleType.IncomeTax, "PAYE 2026", "LK", "2026",
        new DateOnly(2026, 1, 1), null, true,
        new[]
        {
            new TaxSlabInput(0m, 100000m, 0m, 0),
            new TaxSlabInput(100000m, null, 10m, 1),
        },
        SocialSecurity: null);

    private static UpdateStatutoryRuleInput SameBody(CreateStatutoryRuleInput c) => new(
        c.RuleName, c.CountryCode, c.FiscalYear, c.EffectiveFrom, c.EffectiveTo, c.IsActive, c.TaxSlabs, c.SocialSecurity);

    [Fact]
    public async Task Update_WithIdenticalBody_Succeeds_OnPostgres()
    {
        var tc = new MutableTenantContext { TenantId = _tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());

        await using var db = CreateContext(tc, cu);
        await db.Database.MigrateAsync();

        var create = IncomeTaxRule();
        var created = await BuildService(db, tc, cu).CreateAsync(create);
        created.IsSuccess.Should().BeTrue(created.Error);

        // Fresh service/context for the update (as in a real second request).
        await using var db2 = CreateContext(tc, cu);
        var updated = await BuildService(db2, tc, cu).UpdateAsync(created.Value!.Id, SameBody(create));

        // Pre-fix this reportedly 500'd; the test reproduces the real Postgres behavior.
        updated.IsSuccess.Should().BeTrue(updated.Error);
    }
}
