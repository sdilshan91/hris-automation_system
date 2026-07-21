// ============================================================================
// DF-1 migration data-conversion proof — SOURCE-COUPLED (runs the real migration).
//
// DateOnlyRemodelMigrationConversionPostgresTests proves the conversion EXPRESSION
// is correct, but hardcodes a copy of it — dropping `AT TIME ZONE 'UTC'` from the
// actual migration would leave that test green. This test closes that gap: it seeds
// a real midnight-UTC `timestamptz` row into `asset.issue_date` at the pre-remodel
// schema, then runs the ACTUAL `20260721125821_Remodel_DateOnly_Columns` Up() over it
// and asserts the calendar date survived.
//
// The container's server timezone is deliberately NON-UTC (America/New_York), so the
// migration's ALTER runs under a session where a bare `col::date` cast would truncate
// midnight-UTC to the PREVIOUS day. That is what makes this mutation-meaningful: remove
// the `AT TIME ZONE 'UTC'` clause from the migration and this test goes RED (2026-07-09).
// A guard first asserts the session really is non-UTC, so the test can never be silently
// toothless if the container ignored the TZ env.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class DateOnlyRemodelOnDataMigrationPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:17-alpine").WithEnvironment("TZ", "America/New_York").Build();

    // EF migration IDs (see Persistence/Migrations/).
    private const string BeforeRemodel = "20260720163712_Payroll_ApprovalDelegation";
    private const string Remodel = "20260721125821_Remodel_DateOnly_Columns";

    public async Task InitializeAsync() => await _postgres.StartAsync();
    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private AppDbContext CreateContext()
    {
        var tc = Substitute.For<ITenantContext>();
        tc.TenantId.Returns(Guid.NewGuid()); // unused: this test uses the migrator + raw SQL, not tenant-filtered queries
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options, tc);
    }

    [Fact]
    [Trait("TC", "TC-ONB-004-14")]
    public async Task Up_migration_converts_an_existing_midnight_utc_row_to_the_correct_date()
    {
        await using var db = CreateContext();
        var migrator = db.Database.GetService<IMigrator>();

        // Guard: the whole point is a non-UTC session. If the container ignored TZ, fail loudly rather
        // than run a toothless (bare-cast-would-also-pass) assertion.
        await using (var guardConn = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await guardConn.OpenAsync();
            await using var tzCmd = new NpgsqlCommand("SELECT current_setting('TimeZone');", guardConn);
            var sessionTz = (string)(await tzCmd.ExecuteScalarAsync())!;
            sessionTz.Should().NotBe("UTC",
                "the migration must run under a non-UTC session for this proof to be mutation-meaningful");
        }

        // 1. Migrate to just BEFORE the remodel — asset.issue_date is still `timestamp with time zone` here.
        await migrator.MigrateAsync(BeforeRemodel);

        // 2. Insert a row the OLD write-path way: a midnight-UTC instant stored in the timestamptz column.
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO asset (id, tenant_id, asset_tag, asset_type, condition, status, created_at, issue_date) " +
            "VALUES ({0}, {1}, 'LT-MIG-1', 'Laptop', {2}, {3}, now(), TIMESTAMPTZ '2026-07-10 00:00:00+00');",
            id, Guid.NewGuid(), nameof(AssetCondition.New), nameof(AssetStatus.Available));

        // 3. Apply the REAL remodel migration — its actual Up() ALTER runs under the non-UTC session.
        await migrator.MigrateAsync(Remodel);

        // 4. The stored UTC calendar date must survive (2026-07-10), not off-by-one to 2026-07-09.
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var read = new NpgsqlCommand("SELECT issue_date FROM asset WHERE id = @id;", conn);
        read.Parameters.AddWithValue("id", id);
        var stored = (DateOnly)(await read.ExecuteScalarAsync())!;

        stored.Should().Be(new DateOnly(2026, 7, 10),
            "the real Up() migration must preserve the midnight-UTC calendar date; the bare cast a mutation " +
            "would leave (clause removed) truncates it to 2026-07-09 under the non-UTC server session");
    }
}
