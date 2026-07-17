// ============================================================================
// ISSUE-314: the filtered UNIQUE idempotency index on onboarding_checklist_instance.
//
// HARNESS = real Postgres via Testcontainers (NOT InMemory). This is deliberate and
// load-bearing: the InMemory provider does NOT enforce unique indexes, so the DB
// guarantee AssignAsync's catch-on-conflict relies on can only be exercised here.
// The index is filtered to `idempotency_key IS NOT NULL AND status = 'Active'`, so
// it rejects a second ACTIVE row with the same key (the concurrent-race guarantee)
// while leaving superseded rows and null-key rows unconstrained.
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

public sealed class OnboardingIdempotencyConcurrencyPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _templateId = Guid.NewGuid();

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
        cu.Email.Returns("hr@acme.com");
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
            .Options, tc);
    }

    private OnboardingChecklistInstance MakeInstance(string? key, OnboardingChecklistStatus status) => new()
    {
        Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeId = _employeeId, TemplateId = _templateId,
        TemplateName = "Standard Onboarding", Status = status,
        StartDate = DateTime.UtcNow, Version = 1, IdempotencyKey = key, IsDeleted = false,
    };

    [Fact]
    public async Task Filtered_unique_index_rejects_a_second_active_instance_with_the_same_key_issue314()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        // First ACTIVE instance with key "k1".
        db.OnboardingChecklistInstances.Add(MakeInstance("k1", OnboardingChecklistStatus.Active));
        await db.SaveChangesAsync();

        // A SECOND active instance with the SAME (tenant, employee, template, key) is rejected by the DB — this
        // is the guarantee the app-level SELECT-then-INSERT dedup cannot provide under a concurrent race.
        await using var db2 = CreateContext();
        db2.OnboardingChecklistInstances.Add(MakeInstance("k1", OnboardingChecklistStatus.Active));
        var duplicate = async () => await db2.SaveChangesAsync();

        var ex = (await duplicate.Should().ThrowAsync<DbUpdateException>()).Which;
        ex.InnerException.Should().BeOfType<Npgsql.PostgresException>()
            .Which.SqlState.Should().Be(Npgsql.PostgresErrorCodes.UniqueViolation, "23505 unique_violation");
    }

    [Fact]
    public async Task Superseded_instance_with_the_same_key_is_allowed_by_the_filter_issue314()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        // An ACTIVE instance plus a SUPERSEDED one that reuses the same key must coexist — the partial index is
        // filtered to `status = 'Active'`, so a superseded row never blocks a legitimate later assignment.
        db.OnboardingChecklistInstances.Add(MakeInstance("k2", OnboardingChecklistStatus.Superseded));
        db.OnboardingChecklistInstances.Add(MakeInstance("k2", OnboardingChecklistStatus.Active));
        var act = async () => await db.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Null_key_instances_are_not_constrained_issue314()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        // Two Active instances with NULL idempotency key coexist (the index is filtered to non-null keys) — an
        // assign without an Idempotency-Key is never blocked.
        db.OnboardingChecklistInstances.Add(MakeInstance(null, OnboardingChecklistStatus.Active));
        db.OnboardingChecklistInstances.Add(MakeInstance(null, OnboardingChecklistStatus.Active));
        var act = async () => await db.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }
}
