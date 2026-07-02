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
}
