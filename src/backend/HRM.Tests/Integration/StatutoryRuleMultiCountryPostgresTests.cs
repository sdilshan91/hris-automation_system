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

    // (f) ISSUE-299 — the create-path duplicate pre-check must compare NORMALIZED-to-normalized.
    //     A dirty stored country_code ("lk ", "lk", "LK ") comes from a seed/import/psql write that bypassed the
    //     service's normalize-on-save. Pre-fix the pre-check ran `country_code = 'LK'` and the unique index is on
    //     the RAW column too, so NEITHER fired: the duplicate was created. The resolver, however, matches on
    //     upper(btrim(country_code)) (arm (e) above), so BOTH rows then matched and SelectEffectiveByType picked
    //     an arbitrary winner on an identical EffectiveFrom → a WRONG TAX RATE on real payroll. Must be 409.
    //     Real Postgres, not InMemory: the fix has to TRANSLATE to upper(btrim(...)); InMemory would happily
    //     client-evaluate Trim()/ToUpper() and green a fix that throws at runtime.
    [Theory]
    [Trait("Issue", "ISSUE-299")]
    [InlineData("lk ")]  // whitespace-dirty
    [InlineData("lk")]   // case-dirty
    [InlineData("LK ")]  // trailing space only
    public async Task Create_WhenAnUnNormalizedDuplicateIsAlreadyStored_Returns409_OnPostgres(string storedCode)
    {
        var (tc, cu) = Actors();
        await using var seed = CreateContext(tc, cu);
        await seed.Database.MigrateAsync();

        // Seeded directly through the DbContext: the service would normalize it away.
        seed.StatutoryRules.Add(RawIncomeTax(storedCode, new DateOnly(2026, 4, 1), isDeleted: false));
        await seed.SaveChangesAsync();

        await using var db = CreateContext(tc, cu);
        // Same RuleType + FiscalYear ("2026-2027") and an OVERLAPPING window (both open-ended from 2026-04-01).
        var created = await BuildService(db, tc, cu).CreateAsync(
            IncomeTax("LK", new DateOnly(2026, 4, 1), null));

        created.IsFailure.Should().BeTrue("the stored '" + storedCode + "' is the same country as 'LK'");
        created.StatusCode.Should().Be(409);
        created.ErrorCode.Should().Be("duplicate_statutory_rule");

        await using var verify = CreateContext(tc, cu);
        (await verify.StatutoryRules.CountAsync()).Should().Be(1, "no second row may be written");
    }

    // (f2) Do NOT over-tighten: a genuinely DIFFERENT country is still allowed alongside a dirty row.
    [Fact]
    [Trait("Issue", "ISSUE-299")]
    public async Task Create_DifferentCountry_StillSucceedsAlongsideADirtyRow_OnPostgres()
    {
        var (tc, cu) = Actors();
        await using var seed = CreateContext(tc, cu);
        await seed.Database.MigrateAsync();
        seed.StatutoryRules.Add(RawIncomeTax("lk ", new DateOnly(2026, 4, 1), isDeleted: false));
        await seed.SaveChangesAsync();

        await using var db = CreateContext(tc, cu);
        var created = await BuildService(db, tc, cu).CreateAsync(
            IncomeTax("IN", new DateOnly(2026, 4, 1), null));

        created.IsSuccess.Should().BeTrue(created.Error);
    }

    // (g) ISSUE-299 — the fiscal-year clone path must not PROPAGATE a dirty country_code into the target FY.
    //     Pre-fix `CountryCode = src.CountryCode` copied "lk " verbatim, minting a fresh evasive row every year.
    [Fact]
    [Trait("Issue", "ISSUE-299")]
    public async Task CloneFiscalYear_NormalizesADirtyCountryCode_OnPostgres()
    {
        var (tc, cu) = Actors();
        await using var seed = CreateContext(tc, cu);
        await seed.Database.MigrateAsync();

        var raw = RawIncomeTax("lk ", new DateOnly(2025, 4, 1), isDeleted: false);
        raw.FiscalYear = "2025-2026";
        seed.StatutoryRules.Add(raw);
        await seed.SaveChangesAsync();

        await using var db = CreateContext(tc, cu);
        var cloned = await BuildService(db, tc, cu).CloneFiscalYearAsync(
            "2025-2026", "2026-2027", new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));
        cloned.IsSuccess.Should().BeTrue(cloned.Error);

        await using var verify = CreateContext(tc, cu);
        var clone = await verify.StatutoryRules.AsNoTracking()
            .SingleAsync(r => r.FiscalYear == "2026-2027");
        clone.CountryCode.Should().Be("LK", "a dirty source must not propagate into the cloned fiscal year");
    }

    // (g2) ISSUE-299 — the clone's target-FY collision guard must also compare normalized-to-normalized:
    //      a dirty "lk " already in the TARGET year must block cloning a clean "LK" source into it.
    [Fact]
    [Trait("Issue", "ISSUE-299")]
    public async Task CloneFiscalYear_WhenTargetHoldsAnUnNormalizedSameCountryRule_Returns409_OnPostgres()
    {
        var (tc, cu) = Actors();
        await using var seed = CreateContext(tc, cu);
        await seed.Database.MigrateAsync();

        var source = RawIncomeTax("LK", new DateOnly(2025, 4, 1), isDeleted: false);
        source.FiscalYear = "2025-2026";
        var dirtyTarget = RawIncomeTax("lk ", new DateOnly(2026, 4, 1), isDeleted: false);
        dirtyTarget.FiscalYear = "2026-2027";
        seed.StatutoryRules.AddRange(source, dirtyTarget);
        await seed.SaveChangesAsync();

        await using var db = CreateContext(tc, cu);
        var cloned = await BuildService(db, tc, cu).CloneFiscalYearAsync(
            "2025-2026", "2026-2027", new DateOnly(2026, 4, 1), null);

        cloned.IsFailure.Should().BeTrue();
        cloned.StatusCode.Should().Be(409);
        cloned.ErrorCode.Should().Be("target_fiscal_year_exists");
    }

    // (e) TAX-3 normalization guard on REAL Postgres: a raw/seed/import write can store an un-normalized
    //     country_code — either case-dirty ("lk") OR whitespace-dirty ("LK ") — bypassing the service's
    //     normalize-on-save. The resolver matches on upper(btrim(country_code)) to EXACTLY mirror the cumulative
    //     pre-scan / report normalization (Trim()+ToUpper()), so a normalized "LK" lookup must STILL resolve the
    //     dirty row. The InMemory arm only client-evaluates Trim()/ToUpper(); only this proves the SQL
    //     upper(btrim(...)) match translates + resolves. The whitespace case guards the gap the case-only upper()
    //     left open (pre-scan threads prior-YTD while the resolver would otherwise match nothing → money divergence).
    [Theory]
    [InlineData("lk")]   // case-dirty
    [InlineData("LK ")]  // whitespace-dirty (trailing space)
    public async Task Resolver_NormalizedMatchesAnUnNormalizedStoredCountryCode_OnPostgres(string storedCode)
    {
        var (tc, cu) = Actors();
        await using var seed = CreateContext(tc, cu);
        await seed.Database.MigrateAsync();

        var raw = RawIncomeTax(storedCode, new DateOnly(2026, 4, 1), isDeleted: false); // bypasses the service
        raw.TaxSlabs =
        [
            new TaxSlab { Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, SlabFrom = 0m, SlabTo = 100_000m, RatePercentage = 0m, OrderIndex = 0 },
            new TaxSlab { Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, SlabFrom = 100_000m, SlabTo = null, RatePercentage = 10m, OrderIndex = 1 },
        ];
        seed.StatutoryRules.Add(raw);
        await seed.SaveChangesAsync();

        await using var db = CreateContext(tc, cu);
        var resolver = new StatutoryDeductionResolver(db, tc, NullLogger<StatutoryDeductionResolver>.Instance);
        var wage = new StatutoryWageInput(600_000m, 600_000m, 0m, 0m, null);
        var resolved = await resolver.ResolveAsync(2026, 4, wage, "2026-2027", "LK");

        resolved.IsSuccess.Should().BeTrue(resolved.Error);
        // 10% of (600k − 100k) = 50,000 → the dirty-cased "lk" row was resolved via the upper() match.
        // On the pre-fix `r.CountryCode == country` this would find nothing → Empty → IncomeTax 0.
        resolved.Value!.IncomeTax.Should().Be(50_000m);
    }
}
