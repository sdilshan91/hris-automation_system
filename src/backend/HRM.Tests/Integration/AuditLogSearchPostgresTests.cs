// ============================================================================
// BUG-007 regression: audit-log keyword search must run on real Postgres. The
// Before/After columns are jsonb; `string.Contains` on them is not translatable
// by Npgsql and 500s (it only "worked" on the InMemory tests). Search must use
// the translatable text/structured columns and return results without throwing.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.AuditLog.DTOs;
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

public sealed class AuditLogSearchPostgresTests : IAsyncLifetime
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

    private AppDbContext CreateContext(ITenantContext tenantContext, ICurrentUser currentUser)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tenantContext), new AuditInterceptor(currentUser))
            .Options;
        return new AppDbContext(options, tenantContext);
    }

    [Fact]
    public async Task Search_OnRealPostgres_MatchesTextColumns_WithoutJsonbTranslationError()
    {
        var tenantContext = new MutableTenantContext { TenantId = _tenantId };
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(Guid.NewGuid());

        await using var db = CreateContext(tenantContext, currentUser);
        await db.Database.MigrateAsync();

        db.AuditLogs.AddRange(
            new AuditLog
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EventType = "Role.Created",
                Action = "Role.Created", ResourceType = "Role", ResourceId = Guid.NewGuid().ToString(),
                Detail = "Role 'Approver' created with 3 permissions.",
                Before = null, After = "{\"name\":\"Approver\"}",
            },
            new AuditLog
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EventType = "Employee.Updated",
                Action = "Employee.Updated", ResourceType = "Employee", ResourceId = Guid.NewGuid().ToString(),
                Detail = "Employee contact updated.",
                Before = "{\"phone\":\"1\"}", After = "{\"phone\":\"2\"}",
            });
        await db.SaveChangesAsync();

        var service = new AuditLogService(db, tenantContext, currentUser,
            NullLogger<AuditLogService>.Instance, httpContextAccessor: null);

        var result = await service.ListAsync(
            new AuditLogFilter(null, null, null, null, null, "Approver"), page: 1, pageSize: 20);

        // Pre-fix: this threw (jsonb Before/After .Contains not translatable). Post-fix: matches by Detail
        // (surfaced as Summary), returning the Role.Created row and excluding the unrelated Employee row.
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().Contain(i => i.Action == "Role.Created");
        result.Value.Items.Should().NotContain(i => i.Action == "Employee.Updated");
    }

    // ========================================================================
    // BUG-241 (MED, US-NTF-005 FR-2): keyword search MUST cover the before/after
    // JSONB content on real Postgres. The BUG-007 fix dropped Before/After from
    // the search predicate to dodge the Npgsql `jsonb.Contains` 500 — which broke
    // FR-2 (full-text keyword search across before/after). The provider-aware fix
    // searches Before/After on Postgres via a `jsonb::text ILIKE` cast.
    //
    // These are Testcontainers/Postgres tests on purpose: the Before/After columns
    // are real `jsonb` here (see AuditLogConfiguration.HasColumnType("jsonb")), so
    // the jsonb-content match is exercised for real — the InMemory unit tests store
    // those columns as plain text and therefore cannot represent this. The seeded
    // keywords appear ONLY inside the JSONB payloads (never in Detail/Action/
    // EventType/ResourceType/ResourceId), so a hit can ONLY come from searching the
    // jsonb content — proving the BUG-241 requirement specifically.
    //
    // Pre-fix (HEAD): the search predicate excludes Before/After, so a jsonb-only
    // term returns 0 rows → the positive assertions below fail (ContainSingle sees
    // 0). Post-fix: the jsonb::text ILIKE cast returns the row cleanly (no 500).
    // ========================================================================

    private async Task<AuditLogService> SeedBug241RowsAsync(AppDbContext db, ITenantContext tenantContext, ICurrentUser currentUser)
    {
        // Row A: keyword "Wolverine" lives ONLY inside the `after` jsonb.
        // Row B: keyword "Magneto"  lives ONLY inside the `before` jsonb.
        // Row C: unrelated control row — neither keyword appears anywhere.
        // Detail/Action/ResourceType are deliberately generic so the ONLY place the
        // keywords occur is the jsonb payload.
        db.AuditLogs.AddRange(
            new AuditLog
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EventType = "Employee.Updated",
                Action = "Employee.Updated", ResourceType = "Employee", ResourceId = Guid.NewGuid().ToString(),
                Detail = "Employee record updated.",
                Before = "{\"codename\":\"Logan\"}", After = "{\"codename\":\"Wolverine\"}",
            },
            new AuditLog
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EventType = "Employee.Updated",
                Action = "Employee.Updated", ResourceType = "Employee", ResourceId = Guid.NewGuid().ToString(),
                Detail = "Employee record updated.",
                Before = "{\"codename\":\"Magneto\"}", After = "{\"codename\":\"Erik\"}",
            },
            new AuditLog
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EventType = "Employee.Updated",
                Action = "Employee.Updated", ResourceType = "Employee", ResourceId = Guid.NewGuid().ToString(),
                Detail = "Employee record updated.",
                Before = "{\"codename\":\"Scott\"}", After = "{\"codename\":\"Cyclops\"}",
            });
        await db.SaveChangesAsync();

        return new AuditLogService(db, tenantContext, currentUser,
            NullLogger<AuditLogService>.Instance, httpContextAccessor: null);
    }

    private (MutableTenantContext ctx, ICurrentUser user) BuildActors()
    {
        var tenantContext = new MutableTenantContext { TenantId = _tenantId };
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(Guid.NewGuid());
        return (tenantContext, currentUser);
    }

    [Fact]
    public async Task Search_MatchesTermInsideAfterJsonb_Postgres_BUG241()
    {
        var (tenantContext, currentUser) = BuildActors();
        await using var db = CreateContext(tenantContext, currentUser);
        await db.Database.MigrateAsync();
        var service = await SeedBug241RowsAsync(db, tenantContext, currentUser);

        // "Wolverine" exists ONLY inside the `after` jsonb of Row A. Pre-fix (before/after
        // excluded from the predicate) this returns 0 rows → fails; post-fix the jsonb::text
        // ILIKE cast returns exactly that row, and the call does NOT 500.
        var result = await service.ListAsync(
            new AuditLogFilter(null, null, null, null, null, "Wolverine"), page: 1, pageSize: 20);

        result.IsSuccess.Should().BeTrue(result.Error); // proves the Postgres path did not throw/500
        result.Value!.Items.Should().ContainSingle()
            .Which.Summary.Should().Contain("Wolverine");
    }

    [Fact]
    public async Task Search_MatchesTermInsideBeforeJsonb_Postgres_BUG241()
    {
        var (tenantContext, currentUser) = BuildActors();
        await using var db = CreateContext(tenantContext, currentUser);
        await db.Database.MigrateAsync();
        var service = await SeedBug241RowsAsync(db, tenantContext, currentUser);

        // "Magneto" exists ONLY inside the `before` jsonb of Row B. Pre-fix this returns 0
        // (before excluded); post-fix the before-side jsonb::text ILIKE cast finds it.
        var result = await service.ListAsync(
            new AuditLogFilter(null, null, null, null, null, "Magneto"), page: 1, pageSize: 20);

        result.IsSuccess.Should().BeTrue(result.Error);
        // The returned row is Row B specifically: its after-summary is "Erik" (Row A→Wolverine,
        // Row C→Cyclops), so the before-side jsonb match resolved to the correct single row.
        result.Value!.Items.Should().ContainSingle()
            .Which.Summary.Should().Contain("Erik");
    }

    [Fact]
    public async Task Search_TermInNoField_ReturnsEmpty_Postgres_BUG241()
    {
        var (tenantContext, currentUser) = BuildActors();
        await using var db = CreateContext(tenantContext, currentUser);
        await db.Database.MigrateAsync();
        var service = await SeedBug241RowsAsync(db, tenantContext, currentUser);

        // "Nightcrawler" appears in NO column (jsonb or text). Must return an empty set cleanly
        // — the jsonb::text ILIKE predicate must not 500, just match nothing.
        var result = await service.ListAsync(
            new AuditLogFilter(null, null, null, null, null, "Nightcrawler"), page: 1, pageSize: 20);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
    }
}
