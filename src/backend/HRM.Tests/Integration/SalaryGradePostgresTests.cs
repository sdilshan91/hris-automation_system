// ============================================================================
// DF-48 / US-CHR-005 / ISSUE-021: SalaryGrade CRUD — real-Postgres integration tests.
//
// Proves the three things the EF InMemory provider CANNOT prove for the salary_grades
// table shipped in #389, and which are therefore invisible to any InMemory test:
//
//   1. decimal(18,2) precision/rounding. HasPrecision(18,2) → numeric(18,2) only truncates
//      on a real numeric column; InMemory keeps the full .NET decimal (50000.999m stays
//      50000.999m), so this rounding is the InMemory-masks-Postgres crux.
//   2. The (tenant_id, code) UNIQUE index (partial, is_deleted = false). InMemory enforces
//      neither unique indexes nor the partial filter, so the 23505 DB backstop — the last
//      line of defence behind the service's case-insensitive guard — is only meaningful here.
//   3. The tenant global query filter actually TRANSLATES to a SQL WHERE tenant_id = … on
//      Npgsql (tenant A's grade is invisible to a tenant-B scope).
//
// Harness copied verbatim from AttendanceSettingsCrudPostgresTests. UseSnakeCaseNamingConvention()
// is NOT optional — omitting it makes MigrateAsync throw PendingModelChangesWarning.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.SalaryGrades.DTOs;
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

public sealed class SalaryGradePostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db(Guid.NewGuid());
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId { get; init; }
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
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }

    private AppDbContext Db(Guid tenantId)
    {
        var tc = new FixedTenantContext { TenantId = tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        cu.Email.Returns("hr@acme.test");

        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_postgres.GetConnectionString(), n =>
                {
                    n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    n.EnableRetryOnFailure(maxRetryCount: 3);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
                .Options,
            tc);
    }

    private static SalaryGradeService Service(AppDbContext db, Guid tenantId)
    {
        var cu = Substitute.For<ICurrentUser>();
        cu.Email.Returns("hr@acme.test");
        cu.UserId.Returns(Guid.NewGuid());
        return new SalaryGradeService(
            db, new FixedTenantContext { TenantId = tenantId }, cu,
            NullLogger<SalaryGradeService>.Instance);
    }

    private static CreateSalaryGradeRequest NewRequest(
        string code, decimal min, decimal max, decimal? mid = null, string currency = "USD") => new()
    {
        Code = code,
        Name = $"Grade {code}",
        MinAmount = min,
        MidAmount = mid,
        MaxAmount = max,
        Currency = currency,
    };

    // ══ Arm 1 — decimal(18,2) precision: the numeric column ROUNDS ══

    /// <summary>
    /// The band amounts are numeric(18,2). Creating a grade whose MinAmount carries three decimals
    /// (50000.999) must round to 50001.00 on read-back — that rounding happens ONLY on a real numeric
    /// column. On InMemory the value would survive as 50000.999m, so this arm is the InMemory-masks-Postgres
    /// crux: it fails on InMemory and passes only against Postgres's numeric(18,2).
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-005-48")]
    public async Task CreateSalaryGrade_RoundsAmountsToNumeric18_2_OnRealPostgres()
    {
        var tenantId = Guid.NewGuid();
        Guid gradeId;

        await using (var db = Db(tenantId))
        {
            // Third decimals chosen to avoid the .xx5 half-rounding-mode debate: .999→+1, .559→.56, .004→.00.
            var result = await Service(db, tenantId).CreateAsync(
                NewRequest("G1", min: 50000.999m, max: 60000.559m, mid: 55000.004m), default);

            result.IsSuccess.Should().BeTrue(result.Error);
            gradeId = result.Value!.Id;
        }

        await using (var verify = Db(tenantId))
        {
            // Read straight off the numeric column via a fresh context (no in-memory decimal cached).
            var row = await verify.SalaryGrades.AsNoTracking().SingleAsync(g => g.Id == gradeId);

            row.MinAmount.Should().Be(50001.00m, "numeric(18,2) rounds 50000.999 → 50001.00");
            row.MaxAmount.Should().Be(60000.56m, "numeric(18,2) rounds 60000.559 → 60000.56");
            row.MidAmount.Should().Be(55000.00m, "numeric(18,2) rounds 55000.004 → 55000.00");
        }
    }

    // ══ Arm 2 — the (tenant_id, code) unique index is the DB backstop (23505) ══

    /// <summary>
    /// Two grades with the identical (TenantId, Code) in the same exact case, inserted DIRECTLY via the
    /// DbContext so the service's case-insensitive ToLower guard is bypassed, must be rejected by
    /// ix_salary_grades_tenant_id_code (Postgres 23505 → DbUpdateException). InMemory enforces no unique
    /// index, so this DB backstop behind the service check is only provable on Postgres.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-005-48")]
    public async Task DuplicateCode_SameTenant_ViolatesUniqueIndex_23505()
    {
        var tenantId = Guid.NewGuid();

        await using var db = Db(tenantId);

        db.SalaryGrades.Add(new SalaryGrade
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Code = "DUP", Name = "First",
            MinAmount = 100m, MaxAmount = 200m, Currency = "USD", IsActive = true, IsDeleted = false,
        });
        await db.SaveChangesAsync(); // first insert is fine

        db.SalaryGrades.Add(new SalaryGrade
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Code = "DUP", Name = "Second (same code)",
            MinAmount = 300m, MaxAmount = 400m, Currency = "USD", IsActive = true, IsDeleted = false,
        });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "the (tenant_id, code) partial unique index must reject a second same-case code in the same tenant");
    }

    // ══ Arm 3 — the tenant global query filter TRANSLATES to SQL ══

    /// <summary>
    /// A grade created under tenant A must be invisible to a tenant-B scope: GetAllAsync returns empty and a
    /// direct filtered query returns empty, while an IgnoreQueryFilters probe confirms the row is really there
    /// (so the emptiness is the FILTER, not a missing row). Proves the EF global tenant filter translates to a
    /// real WHERE tenant_id = … on Npgsql — InMemory applies the same expression but never proves SQL translation.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-005-48")]
    public async Task SalaryGrade_TenantQueryFilter_IsolatesAcrossTenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid gradeId;

        await using (var dbA = Db(tenantA))
        {
            var created = await Service(dbA, tenantA).CreateAsync(NewRequest("A1", 1000m, 2000m), default);
            created.IsSuccess.Should().BeTrue(created.Error);
            gradeId = created.Value!.Id;
        }

        await using (var dbB = Db(tenantB))
        {
            var all = await Service(dbB, tenantB).GetAllAsync(includeInactive: true, default);
            all.IsSuccess.Should().BeTrue();
            all.Value.Should().BeEmpty("tenant B must not see tenant A's salary grade");

            var direct = await dbB.SalaryGrades.AsNoTracking().ToListAsync();
            direct.Should().BeEmpty("the tenant filter must translate to a SQL WHERE tenant_id = B predicate");

            var byId = await dbB.SalaryGrades.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gradeId);
            byId.Should().BeNull("even a by-id lookup is scoped to tenant B");
        }

        // Sanity: the row genuinely exists — the emptiness above is the filter, not a lost insert.
        await using (var probe = Db(tenantA))
        {
            var leaked = await probe.SalaryGrades.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(g => g.Id == gradeId);
            leaked.TenantId.Should().Be(tenantA);
        }
    }

    // ══ Arm 4 (B5) — the grouped reference count, and Active round-tripping, on real Postgres ══

    /// <summary>
    /// B5 added two things the unit arms only exercise on EF InMemory: an <c>IsActive</c> write through the
    /// UPDATE path, and a GROUPED reference count in <c>GetAllAsync</c>
    /// (<c>Where(Contains) → GroupBy → Count → ToDictionaryAsync</c>).
    ///
    /// <para>
    /// The count query is the part worth a Postgres arm: <c>Contains</c> over a client list translates to
    /// <c>= ANY(@ids)</c> and the <c>GroupBy</c> to a SQL aggregate, neither of which InMemory exercises —
    /// it just runs LINQ-to-objects and agrees by accident. If the translation broke, every grade would
    /// report zero referrers and the deactivation warning would silently vanish product-wide.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-005-48")]
    public async Task ReferenceCount_AndActiveToggle_RoundTrip_OnRealPostgres()
    {
        var tenantId = Guid.NewGuid();
        Guid referencedId;
        Guid unreferencedId;

        await using (var db = Db(tenantId))
        {
            referencedId = (await Service(db, tenantId)
                .CreateAsync(NewRequest("G1", min: 1000m, max: 2000m), default)).Value!.Id;
            unreferencedId = (await Service(db, tenantId)
                .CreateAsync(NewRequest("G2", min: 3000m, max: 4000m), default)).Value!.Id;
        }

        await using (var db = Db(tenantId))
        {
            db.JobTitles.Add(new JobTitle
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TitleName = "Engineer",
                GradeId = referencedId, IsActive = true,
            });
            db.JobTitles.Add(new JobTitle
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TitleName = "Senior Engineer",
                GradeId = referencedId, IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = Db(tenantId))
        {
            var all = (await Service(db, tenantId).GetAllAsync(includeInactive: true, default)).Value!;

            all.Single(g => g.Id == referencedId).ReferencingJobTitleCount.Should().Be(2,
                "the grouped count must survive translation to SQL — a broken one silences every "
                + "deactivation warning in the product");
            all.Single(g => g.Id == unreferencedId).ReferencingJobTitleCount.Should().Be(0,
                "a grade nothing points at must report zero, not inherit its neighbour's count");
        }

        // The Active flag written through UPDATE must persist to the real boolean column, and the grade
        // must then be reachable again — reactivation was impossible before B5.
        await using (var db = Db(tenantId))
        {
            var update = await Service(db, tenantId).UpdateAsync(referencedId, new UpdateSalaryGradeRequest
            {
                Code = "G1", Name = "Grade G1", MinAmount = 1000m, MidAmount = null, MaxAmount = 2000m,
                Currency = "USD", Description = null, IsActive = false,
            }, default);
            update.IsSuccess.Should().BeTrue(update.Error);
        }

        await using (var verify = Db(tenantId))
        {
            var row = await verify.SalaryGrades.AsNoTracking().SingleAsync(g => g.Id == referencedId);
            row.IsActive.Should().BeFalse("the flag must reach the boolean column, not just the DTO");

            var activeOnly = (await Service(verify, tenantId).GetAllAsync(includeInactive: false, default)).Value!;
            activeOnly.Should().NotContain(g => g.Id == referencedId);
        }

        await using (var db = Db(tenantId))
        {
            var back = await Service(db, tenantId).UpdateAsync(referencedId, new UpdateSalaryGradeRequest
            {
                Code = "G1", Name = "Grade G1", MinAmount = 1000m, MidAmount = null, MaxAmount = 2000m,
                Currency = "USD", Description = null, IsActive = true,
            }, default);
            back.IsSuccess.Should().BeTrue(back.Error);

            var activeOnly = (await Service(db, tenantId).GetAllAsync(includeInactive: false, default)).Value!;
            activeOnly.Should().Contain(g => g.Id == referencedId,
                "reactivation was impossible before B5 — a grade deactivated by mistake was stuck forever");
        }
    }
}
