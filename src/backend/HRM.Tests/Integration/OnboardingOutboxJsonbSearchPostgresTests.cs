// ============================================================================
// BUG-126 regression: the onboarding overdue-notification idempotency check must
// not run string.Contains on the jsonb Payload column (Npgsql cannot translate
// it → `operator does not exist: jsonb ~~ jsonb` → the recurring job fails). The
// fixed path pulls the tiny candidate set and matches in memory. Real Postgres.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class OnboardingOutboxJsonbSearchPostgresTests : IAsyncLifetime
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

    private AppDbContext CreateContext()
    {
        var tc = new MutableTenantContext { TenantId = _tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
            .Options, tc);
    }

    [Fact]
    public async Task OverdueIdempotencyCheck_MatchesTaskIdWithoutJsonbLikeError()
    {
        var checklistInstanceId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        const string type = "onboarding.task.overdue";

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        db.OnboardingNotificationOutbox.Add(new OnboardingNotificationOutbox
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            ChecklistInstanceId = checklistInstanceId,
            NotificationType = type,
            Payload = $"{{\"taskId\":\"{taskId}\"}}",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var today = DateTime.UtcNow.Date;
        var taskIdString = taskId.ToString();

        // FIXED path: filter on structured columns, then Contains in memory.
        var candidatePayloads = await db.OnboardingNotificationOutbox
            .IgnoreQueryFilters()
            .Where(o => o.TenantId == _tenantId
                && o.ChecklistInstanceId == checklistInstanceId
                && o.NotificationType == type
                && o.CreatedAt >= today)
            .Select(o => o.Payload)
            .ToListAsync();
        candidatePayloads.Any(p => p != null && p.Contains(taskIdString))
            .Should().BeTrue("the fixed in-memory match must find the task id and must not throw");

        // MUTATION: the pre-fix pattern (Contains on the jsonb column) must throw on Postgres.
        var buggy = async () => await db.OnboardingNotificationOutbox
            .IgnoreQueryFilters()
            .AnyAsync(o => o.TenantId == _tenantId
                && o.ChecklistInstanceId == checklistInstanceId
                && o.NotificationType == type
                && o.CreatedAt >= today
                && o.Payload.Contains(taskIdString));
        await buggy.Should().ThrowAsync<Exception>("string.Contains on a jsonb column is not translatable (jsonb ~~ jsonb)");
    }
}
