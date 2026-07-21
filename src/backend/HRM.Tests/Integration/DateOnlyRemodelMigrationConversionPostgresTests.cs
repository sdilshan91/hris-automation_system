// ============================================================================
// DF-1 migration data-conversion proof (20260721125821_Remodel_DateOnly_Columns).
//
// The Testcontainers suites apply migrations to a FRESH, EMPTY database, so the
// `ALTER ... TYPE date USING (col AT TIME ZONE 'UTC')::date` runs on zero rows —
// that proves the SQL *parses*, but NOT that it converts EXISTING data correctly.
//
// The pre-migration columns held date-only values persisted as midnight-UTC
// `timestamptz` instants. A bare `col::date` casts at the SESSION timezone, so a
// non-UTC session truncates midnight-UTC to the PREVIOUS calendar day (an
// off-by-one — the exact BUG-289/290 failure class). This test proves, on real
// Postgres under a deliberately non-UTC session, that:
//   (a) the migration's `(col AT TIME ZONE 'UTC')::date` preserves the UTC date, and
//   (b) the `AT TIME ZONE 'UTC'` clause is LOAD-BEARING — the bare cast off-by-ones.
// (b) is what makes this arm mutation-meaningful: delete the clause and (a) fails.
// ============================================================================

using FluentAssertions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class DateOnlyRemodelMigrationConversionPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();
    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private async Task<NpgsqlConnection> OpenAsync(string sessionTimeZone)
    {
        var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using (var tz = new NpgsqlCommand($"SET TIME ZONE '{sessionTimeZone}';", conn))
            await tz.ExecuteNonQueryAsync();
        return conn;
    }

    private static async Task<DateOnly> ScalarDateAsync(NpgsqlConnection conn, string sql)
    {
        // Npgsql maps a Postgres `date` to DateOnly natively — which is exactly why DF-1's `date`
        // columns bind cleanly to the DateOnly properties.
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (DateOnly)(await cmd.ExecuteScalarAsync())!;
    }

    // A value stored the way the pre-migration write-path stored it: midnight UTC.
    private const string MidnightUtc = "TIMESTAMPTZ '2026-07-10 00:00:00+00'";

    [Theory]
    [InlineData("UTC")]
    [InlineData("America/New_York")]      // UTC-5/-4 — midnight-UTC is the prior evening locally
    [InlineData("Pacific/Kiritimati")]    // UTC+14 — the opposite extreme
    public async Task Up_conversion_preserves_the_utc_calendar_date_under_any_session_tz(string tz)
    {
        await using var conn = await OpenAsync(tz);

        // This is exactly the expression the Up() migration uses.
        var converted = await ScalarDateAsync(conn, $"SELECT ({MidnightUtc} AT TIME ZONE 'UTC')::date;");

        converted.Should().Be(new DateOnly(2026, 7, 10),
            $"the migration must preserve the stored UTC calendar date under session tz '{tz}'");
    }

    [Fact]
    public async Task The_AT_TIME_ZONE_UTC_clause_is_load_bearing_the_bare_cast_off_by_ones()
    {
        await using var conn = await OpenAsync("America/New_York");

        var safe = await ScalarDateAsync(conn, $"SELECT ({MidnightUtc} AT TIME ZONE 'UTC')::date;");
        var bare = await ScalarDateAsync(conn, $"SELECT ({MidnightUtc})::date;");

        safe.Should().Be(new DateOnly(2026, 7, 10), "the UTC-pinned cast keeps the stored date");
        bare.Should().Be(new DateOnly(2026, 7, 9),
            "the bare cast truncates midnight-UTC to the previous day under a negative-offset session — " +
            "the off-by-one the migration's AT TIME ZONE 'UTC' exists to prevent");
        safe.Should().NotBe(bare, "if these were equal the AT TIME ZONE 'UTC' clause would be dead code");
    }

    [Fact]
    public async Task Down_conversion_restores_the_midnight_utc_instant()
    {
        await using var conn = await OpenAsync("America/New_York");

        // The Down() migration expression: date -> midnight-UTC timestamptz.
        await using var cmd = new NpgsqlCommand(
            "SELECT (DATE '2026-07-10'::timestamp AT TIME ZONE 'UTC') = TIMESTAMPTZ '2026-07-10 00:00:00+00';", conn);
        var restoredToMidnightUtc = (bool)(await cmd.ExecuteScalarAsync())!;

        restoredToMidnightUtc.Should().BeTrue(
            "rolling back must restore the original midnight-UTC instant, not a session-local midnight");
    }
}
