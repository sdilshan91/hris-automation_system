// ============================================================================
// BUG-048 (HIGH, Attendance, US-ATT-005) regression — TC-ATT-052.
//
// ShiftService.CreateAsync/UpdateAsync check the duplicate-name guard against the
// RAW request name (AnyAsync(s => s.Name == request.Name)) but persist the TRIMMED
// name (request.Name.Trim()). So creating "Day Shift " (trailing space) when
// "Day Shift" already exists slips the app-level guard, then violates the partial
// unique index `ix_shift_tenant_name_unique` (tenant_id, name WHERE is_deleted=false)
// → DbUpdateException / Postgres 23505 → HTTP 500 instead of a clean 409 duplicate_name.
// The fix trims BEFORE the check so the whitespace variant is caught as a duplicate.
//
// WHY POSTGRES (not InMemory): the EF Core InMemory provider does NOT enforce unique
// indexes, so pre-fix the whitespace variant would save as a 2nd row and return 201 —
// the 500 could never reproduce and the test would be theater. This uses the project's
// Testcontainers/Postgres harness (mirrors AuditLogSearchPostgresTests /
// StatutoryRuleUpdatePostgresTests) so the real partial unique index fires pre-fix.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Attendance.DTOs;
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

public sealed class ShiftNameTrimDuplicatePostgresTests : IAsyncLifetime
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

    private ShiftService BuildService(AppDbContext db, ITenantContext tc, ICurrentUser cu) =>
        new(db, tc, cu, NullLogger<ShiftService>.Instance);

    private static ShiftRequest SingleShift(string name) => new()
    {
        Name = name,
        Type = ShiftType.Single,
        StartTime = "09:00",
        EndTime = "17:00",
        BreakDurationMinutes = 60,
        GracePeriodMinutes = 10,
        WorkingDays = new[] { 1, 2, 3, 4, 5 },
    };

    private static (ITenantContext tc, ICurrentUser cu) Principals(Guid tenantId)
    {
        var tc = new MutableTenantContext { TenantId = tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        cu.Email.Returns("hr@acme.test");
        return (tc, cu);
    }

    /// <summary>
    /// Confirms the partial unique index that makes this bug a 500 (not just a silent 2nd row)
    /// actually exists after MigrateAsync — otherwise the pre-fix reproduction would be vacuous.
    /// </summary>
    private static async Task AssertUniqueNameIndexExistsAsync(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'ix_shift_tenant_name_unique';";
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            count.Should().Be(1, "the partial unique index (tenant_id, name) must exist for the "
                + "pre-fix 500 to be reproducible on Postgres");
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    // ── Create trigger (BUG-048) ───────────────────────────────────────
    [Fact]
    public async Task CreateShift_WhitespaceDuplicateName_ReturnsDuplicateNot500()
    {
        var (tc, cu) = Principals(_tenantId);

        await using var db = CreateContext(tc, cu);
        await db.Database.MigrateAsync();
        await AssertUniqueNameIndexExistsAsync(db);

        // Baseline: "Day Shift" exists.
        var first = await BuildService(db, tc, cu).CreateAsync(SingleShift("Day Shift"));
        first.IsSuccess.Should().BeTrue(first.Error);

        // Fresh context, as a real second request would use.
        await using var db2 = CreateContext(tc, cu);

        // BUG-048: the raw-name guard misses the trimmed duplicate, so pre-fix this reaches
        // SaveChanges and throws DbUpdateException (Postgres 23505) → the API returns 500.
        // Post-fix: trims before the check → clean 409 duplicate_name, no second row.
        var dup = await BuildService(db2, tc, cu).CreateAsync(SingleShift("Day Shift "));

        dup.IsFailure.Should().BeTrue("the trimmed whitespace variant is a duplicate, not a 500");
        dup.StatusCode.Should().Be(409);
        dup.ErrorCode.Should().Be("duplicate_name");

        // No second (or ghost) row was written — exactly one live "Day Shift".
        await using var verify = CreateContext(tc, cu);
        var liveDayShifts = await verify.Shifts.CountAsync(s => s.Name == "Day Shift");
        liveDayShifts.Should().Be(1);
    }

    // ── Update trigger (same defect class, BUG-048) ────────────────────
    [Fact]
    public async Task UpdateShift_WhitespaceDuplicateName_ReturnsDuplicateNot500()
    {
        var (tc, cu) = Principals(_tenantId);

        await using var db = CreateContext(tc, cu);
        await db.Database.MigrateAsync();
        await AssertUniqueNameIndexExistsAsync(db);

        var svc = BuildService(db, tc, cu);
        (await svc.CreateAsync(SingleShift("Day Shift"))).IsSuccess.Should().BeTrue();
        var night = await svc.CreateAsync(SingleShift("Night Shift"));
        night.IsSuccess.Should().BeTrue(night.Error);

        // Rename "Night Shift" → "Day Shift " (trailing space): trimmed it collides with the
        // existing "Day Shift". Pre-fix the raw-name guard misses it → SaveChanges throws 23505
        // → 500. Post-fix: 409 duplicate_name.
        await using var db2 = CreateContext(tc, cu);
        var update = await BuildService(db2, tc, cu)
            .UpdateAsync(night.Value!.Id, SingleShift("Day Shift "));

        update.IsFailure.Should().BeTrue("renaming to a trimmed-existing name is a duplicate, not a 500");
        update.StatusCode.Should().Be(409);
        update.ErrorCode.Should().Be("duplicate_name");

        // The rename was rejected: "Night Shift" is untouched and still exactly one "Day Shift".
        await using var verify = CreateContext(tc, cu);
        (await verify.Shifts.CountAsync(s => s.Name == "Day Shift")).Should().Be(1);
        (await verify.Shifts.CountAsync(s => s.Name == "Night Shift")).Should().Be(1);
    }

    // ── Positive control: a genuinely distinct trimmed name still creates ──
    [Fact]
    public async Task CreateShift_DistinctTrimmedName_StillCreates()
    {
        var (tc, cu) = Principals(_tenantId);

        await using var db = CreateContext(tc, cu);
        await db.Database.MigrateAsync();

        var first = await BuildService(db, tc, cu).CreateAsync(SingleShift("Day Shift"));
        first.IsSuccess.Should().BeTrue(first.Error);

        await using var db2 = CreateContext(tc, cu);
        // "  Evening Shift " trims to a name that does NOT collide → 201, stored trimmed.
        var second = await BuildService(db2, tc, cu).CreateAsync(SingleShift("  Evening Shift "));

        second.IsSuccess.Should().BeTrue(second.Error);
        second.Value!.Name.Should().Be("Evening Shift");

        await using var verify = CreateContext(tc, cu);
        (await verify.Shifts.CountAsync()).Should().Be(2);
    }
}
