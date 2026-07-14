// ============================================================================
// Multi-country tax foundation — REAL-Postgres proof of the corrected 5-column
// unique index on statutory_rule:
//     (tenant_id, rule_type, country_code, fiscal_year, effective_from) WHERE is_deleted = false
//
// The InMemory provider used by the fast gate ignores indexes, so these behaviours are
// UNPROVEN there. This class applies the migrations on a real postgres:17-alpine and asserts:
//   (a) intra-FY VERSIONING (same tenant/type/country/FY, different effective_from) both persist,
//   (b) a TRUE duplicate (same 5-tuple) is rejected by the DB unique index (23505),
//   (c) two DIFFERENT-country rules sharing the 4-tuple both persist (the point of multi-country),
//   (d) a SOFT-DELETED rule does not block re-creating the same version (partial WHERE is_deleted=false).
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

public sealed class StatutoryRuleMultiCountryPostgresTests : IAsyncLifetime
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

    private static CreateStatutoryRuleInput IncomeTax(string country, DateOnly from, DateOnly? to) => new(
        StatutoryRuleType.IncomeTax, $"PAYE {country}", country, "2026-2027", from, to, true,
        new[]
        {
            new TaxSlabInput(0m, 100000m, 0m, 0),
            new TaxSlabInput(100000m, null, 10m, 1),
        },
        SocialSecurity: null);

    /// <summary>A raw entity for the paths that must BYPASS the service's app-level overlap pre-check,
    /// so the DB index itself is what accepts or rejects the row.</summary>
    private StatutoryRule RawIncomeTax(string country, DateOnly from, bool isDeleted) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = _tenantId,
        RuleType = StatutoryRuleType.IncomeTax,
        RuleName = $"PAYE {country}",
        CountryCode = country,
        FiscalYear = "2026-2027",
        EffectiveFrom = from,
        EffectiveTo = null,
        IsActive = true,
        IsDeleted = isDeleted,
    };

    // (a) Intra-FY versioning: same (tenant, type, country, FY) but different effective_from → BOTH persist.
    [Fact]
    public async Task Versioning_TwoNonOverlappingEffectiveFrom_SameCountry_BothPersist()
    {
        var (tc, cu) = Actors();
        await using var db = CreateContext(tc, cu);
        await db.Database.MigrateAsync();

        var first = await BuildService(db, tc, cu).CreateAsync(
            IncomeTax("LK", new DateOnly(2026, 4, 1), new DateOnly(2026, 8, 31)));
        first.IsSuccess.Should().BeTrue(first.Error);

        await using var db2 = CreateContext(tc, cu);
        var second = await BuildService(db2, tc, cu).CreateAsync(
            IncomeTax("LK", new DateOnly(2026, 9, 1), null));
        second.IsSuccess.Should().BeTrue(second.Error);

        await using var db3 = CreateContext(tc, cu);
        (await db3.StatutoryRules.CountAsync(r => r.CountryCode == "LK")).Should().Be(2);
    }

    // (b) True duplicate: same 5-tuple (incl. effective_from). Insert the 2nd RAW (bypassing the app check)
    //     → the DB unique index rejects it with 23505.
    [Fact]
    public async Task TrueDuplicate_SameFiveTuple_ThrowsUniqueViolation_OnPostgres()
    {
        var (tc, cu) = Actors();
        await using var db = CreateContext(tc, cu);
        await db.Database.MigrateAsync();
        db.StatutoryRules.Add(RawIncomeTax("LK", new DateOnly(2026, 4, 1), isDeleted: false));
        await db.SaveChangesAsync();

        await using var db2 = CreateContext(tc, cu);
        db2.StatutoryRules.Add(RawIncomeTax("LK", new DateOnly(2026, 4, 1), isDeleted: false));

        var act = async () => await db2.SaveChangesAsync();
        (await act.Should().ThrowAsync<DbUpdateException>())
            .Which.GetBaseException().Should().BeOfType<Npgsql.PostgresException>()
            .Which.SqlState.Should().Be("23505");
    }

    // (c) Different countries sharing the 4-tuple (tenant, type, FY, effective_from) → BOTH persist.
    [Fact]
    public async Task DifferentCountry_SameFourTuple_BothPersist()
    {
        var (tc, cu) = Actors();
        await using var db = CreateContext(tc, cu);
        await db.Database.MigrateAsync();

        var lk = await BuildService(db, tc, cu).CreateAsync(IncomeTax("LK", new DateOnly(2026, 4, 1), null));
        lk.IsSuccess.Should().BeTrue(lk.Error);

        await using var db2 = CreateContext(tc, cu);
        var inr = await BuildService(db2, tc, cu).CreateAsync(IncomeTax("IN", new DateOnly(2026, 4, 1), null));
        inr.IsSuccess.Should().BeTrue(inr.Error);

        await using var db3 = CreateContext(tc, cu);
        (await db3.StatutoryRules.CountAsync()).Should().Be(2);
    }

    // (d) A soft-deleted rule (is_deleted=true) is excluded by the partial index → the SAME version can be
    //     re-created. Proves the WHERE is_deleted = false filter is honoured on real Postgres.
    [Fact]
    public async Task SoftDeleted_DoesNotBlockRecreatingSameVersion()
    {
        var (tc, cu) = Actors();
        await using var db = CreateContext(tc, cu);
        await db.Database.MigrateAsync();
        db.StatutoryRules.Add(RawIncomeTax("LK", new DateOnly(2026, 4, 1), isDeleted: true));
        await db.SaveChangesAsync();

        await using var db2 = CreateContext(tc, cu);
        db2.StatutoryRules.Add(RawIncomeTax("LK", new DateOnly(2026, 4, 1), isDeleted: false));
        var act = async () => await db2.SaveChangesAsync();
        await act.Should().NotThrowAsync();
    }
}
