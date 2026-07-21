// ============================================================================
// DF-59: the DbCountPortalLinkIpRateLimiter fallback (Redis-off path) counts tokens filtered by
// (tenant_id, request_ip, created_at). This proves the supporting composite index is actually created
// by the real migration on real Postgres — bound to the migration/config source, so removing the
// `HasIndex` (or the migration) turns this test RED. postgres:17-alpine via Testcontainers.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class ApplicantPortalTokenIndexPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var tc = Substitute.For<ITenantContext>();
        tc.TenantId.Returns(Guid.NewGuid());
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options, tc);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    [Trait("TC", "TC-REC-008-16")]
    public async Task Rate_limit_composite_index_exists_on_tenant_ip_createdat_after_migration()
    {
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();

        // Read every index definition on the table; assert one covers (tenant_id, request_ip, created_at)
        // in that order. Column-based (not name-based) so an EF index rename wouldn't false-fail — but it
        // still fails if the index is absent (HasIndex removed) or the columns/order change.
        await using var cmd = new NpgsqlCommand(
            "SELECT indexdef FROM pg_indexes WHERE tablename = 'applicant_portal_token';", conn);

        var defs = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                defs.Add(reader.GetString(0));

        defs.Should().Contain(
            d => d.Contains("(tenant_id, request_ip, created_at)"),
            "the DF-59 composite index backing the fallback rate-limit COUNT must exist after migration; " +
            $"found index defs: {string.Join(" | ", defs)}");
    }
}
