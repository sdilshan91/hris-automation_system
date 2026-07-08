// ============================================================================
// US-ADM-004 (AC-4 / FR-3 / NFR-4) — TenantDataDeletionService on REAL PostgreSQL.
//
// The sibling TenantDataDeletionIntegrationTests runs on the EF InMemory provider, which
// structurally CANNOT exercise the two things that only matter on a relational store:
//
//   1. The provider-aware branch. On InMemory Database.IsRelational() is false, so the
//      service takes the load + RemoveRange fallback. Only Postgres takes the
//      ExecuteDelete()-inside-a-transaction branch (TenantDataDeletionService lines 60-65,
//      144-162, 168-171) that production actually runs.
//   2. FK-ordered deletion. InMemory has NO foreign-key constraints, so the child-first
//      topological ordering (GetTenantScopedTypesChildFirst) is never actually enforced —
//      a wrong order passes on InMemory but would throw on Postgres. Here we seed a genuine
//      multi-table FK chain, so a green run PROVES the ordering: if the service deleted a
//      principal before its dependents, Postgres would raise a foreign-key violation and the
//      whole (transactional) operation would roll back, failing the assertions below.
//
// This is the BUG-068 "InMemory-masks-Postgres" class the phase-5 work targets.
//
// SEEDED FK CHAIN (all genuine FK edges from the EF model):
//   Department  ─(Employee.DepartmentId, ON DELETE RESTRICT)→  must be deleted AFTER Employee
//   JobTitle    ─(Employee.JobTitleId,   ON DELETE RESTRICT)→  must be deleted AFTER Employee
//   Employee    ─(EmployeeDocument.EmployeeId, ON DELETE CASCADE)→ deleted before/with Employee
//   EmployeeDocument (leaf dependent)
// The two RESTRICT edges are the load-bearing ones: they make Postgres throw if Employee is
// not deleted strictly before Department/JobTitle. That is exactly what the child-first order
// must guarantee and what InMemory cannot verify.
//
// NOTE on execution strategy: production registers the DbContext with EnableRetryOnFailure
// (DependencyInjection.cs:41). This harness configures the context the SAME way (retry enabled)
// so the deletion runs under the real Npgsql retrying strategy — production-faithful. This would
// have thrown "does not support user-initiated transactions" (the BUG-068 class, BUG-252) before
// TenantDataDeletionService wrapped its manual BeginTransactionAsync in
// CreateExecutionStrategy().ExecuteAsync(...); these tests now also serve as the BUG-252 regression
// (a green run under retry proves the execution-strategy wrapping holds), on top of asserting the
// transactional child-first FK-ordering that InMemory cannot verify.
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

/// <summary>
/// US-ADM-004: proves the tenant data-deletion service hard-deletes a terminating tenant's rows
/// child-first (respecting real Postgres FK constraints) inside a transaction, retains the tenant
/// row as Terminated with PII redacted + the lifecycle/audit rows, and leaves other tenants intact.
/// </summary>
public sealed class TenantDataDeletionPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    // Tenant A — terminating, will be deleted. Tenant B — active, must be untouched (isolation).
    private readonly Guid _tenantA = BaseEntity.NewUuidV7();
    private readonly Guid _tenantB = BaseEntity.NewUuidV7();

    // Seeded row ids for tenant A (assert they are gone).
    private readonly Guid _deptA = BaseEntity.NewUuidV7();
    private readonly Guid _jobTitleA = BaseEntity.NewUuidV7();
    private readonly Guid _employeeA = BaseEntity.NewUuidV7();
    private readonly Guid _documentA = BaseEntity.NewUuidV7();

    // Seeded row ids for tenant B (assert they remain).
    private readonly Guid _deptB = BaseEntity.NewUuidV7();
    private readonly Guid _jobTitleB = BaseEntity.NewUuidV7();
    private readonly Guid _employeeB = BaseEntity.NewUuidV7();
    private readonly Guid _documentB = BaseEntity.NewUuidV7();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        SeedTenantChain(db, _tenantA, "acme", TenantStatus.Terminating,
            _deptA, _jobTitleA, _employeeA, _documentA);
        SeedTenantChain(db, _tenantB, "globex", TenantStatus.Active,
            _deptB, _jobTitleB, _employeeB, _documentB);

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ── Test 1: FK-ordered hard delete + tenant redaction + cross-tenant isolation ────────

    [Fact]
    public async Task Delete_TerminatingTenant_HardDeletesChildFirst_RedactsTenant_IsolatesOthers()
    {
        // Act: run the real relational path — manual transaction + child-first ExecuteDelete.
        var result = await Service().DeleteTenantDataAsync(_tenantA);

        // A wrong child-first order would have raised a Postgres FK violation (RESTRICT on
        // Employee→Department / Employee→JobTitle) and rolled the transaction back, so success
        // here already proves the ordering is correct on a real relational store.
        result.IsSuccess.Should().BeTrue(result.Error);

        await using var db = CreateContext();

        // Tenant A's per-tenant rows are ALL gone across the FK chain.
        (await db.EmployeeDocuments.IgnoreQueryFilters().CountAsync(d => d.TenantId == _tenantA)).Should().Be(0);
        (await db.Employees.IgnoreQueryFilters().CountAsync(e => e.TenantId == _tenantA)).Should().Be(0);
        (await db.JobTitles.IgnoreQueryFilters().CountAsync(j => j.TenantId == _tenantA)).Should().Be(0);
        (await db.Departments.IgnoreQueryFilters().CountAsync(dep => dep.TenantId == _tenantA)).Should().Be(0);

        // Tenant A row retained, marked Terminated, PII redacted (AC-4).
        var a = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == _tenantA);
        a.Status.Should().Be(TenantStatus.Terminated);
        a.Name.Should().Be("[redacted]");
        a.ContactEmail.Should().BeNull();
        a.BillingEmail.Should().BeNull();

        // Lifecycle + audit rows written and retained.
        (await db.TenantLifecycleEvents.IgnoreQueryFilters()
            .AnyAsync(e => e.TenantId == _tenantA && e.EventType == "terminated")).Should().BeTrue();
        (await db.AuditLogs.IgnoreQueryFilters()
            .AnyAsync(l => l.TenantId == _tenantA && l.Action == "Tenant.Terminated")).Should().BeTrue();

        // ISOLATION: tenant B is completely untouched across the whole chain.
        (await db.EmployeeDocuments.IgnoreQueryFilters().CountAsync(d => d.TenantId == _tenantB)).Should().Be(1);
        (await db.Employees.IgnoreQueryFilters().CountAsync(e => e.TenantId == _tenantB)).Should().Be(1);
        (await db.JobTitles.IgnoreQueryFilters().CountAsync(j => j.TenantId == _tenantB)).Should().Be(1);
        (await db.Departments.IgnoreQueryFilters().CountAsync(dep => dep.TenantId == _tenantB)).Should().Be(1);
        var b = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == _tenantB);
        b.Status.Should().Be(TenantStatus.Active);
        b.Name.Should().Be("globex");
    }

    // ── Test 2: guard — a non-Terminating tenant is a no-op (nothing deleted) ──────────────

    [Fact]
    public async Task Delete_NonTerminatingTenant_IsNoOp_LeavesDataIntact()
    {
        // Tenant B is Active, not Terminating — the service must refuse and touch nothing.
        var result = await Service().DeleteTenantDataAsync(_tenantB);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("not_terminating");

        await using var db = CreateContext();
        (await db.Employees.IgnoreQueryFilters().CountAsync(e => e.TenantId == _tenantB)).Should().Be(1);
        (await db.EmployeeDocuments.IgnoreQueryFilters().CountAsync(d => d.TenantId == _tenantB)).Should().Be(1);
        (await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == _tenantB)).Status.Should().Be(TenantStatus.Active);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// System-context AppDbContext on the container. The deletion service is a system-context operation
    /// (IsResolved == false ⇒ the global query filters resolve to <c>!IsDeleted</c> only, so it can see and
    /// delete every tenant's rows; each delete is still tenant-scoped in the predicate). Retry is enabled
    /// to match production (DependencyInjection.cs:41) — see the file header note (BUG-252 regression).
    /// </summary>
    private AppDbContext CreateContext()
    {
        var tenantContext = new SystemTenantContext();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(false);

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

    private TenantDataDeletionService Service()
        => new(CreateContext(), NullLogger<TenantDataDeletionService>.Instance);

    /// <summary>
    /// Seeds one tenant plus a genuine Department → JobTitle → Employee → EmployeeDocument FK chain.
    /// TenantId is set explicitly (the system-context interceptor does not stamp it).
    /// </summary>
    private static void SeedTenantChain(
        AppDbContext db, Guid tenantId, string subdomain, TenantStatus status,
        Guid deptId, Guid jobTitleId, Guid employeeId, Guid documentId)
    {
        var now = DateTime.UtcNow;

        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Subdomain = subdomain, Name = subdomain, Status = status,
            PlanId = "starter", ContactEmail = $"contact@{subdomain}.test",
            BillingEmail = $"billing@{subdomain}.test", CreatedAt = now,
        });

        db.Departments.Add(new Department
        {
            Id = deptId, TenantId = tenantId, Name = "Engineering", Code = "ENG",
            IsActive = true, CreatedAt = now, CreatedBy = "seed",
        });

        db.JobTitles.Add(new JobTitle
        {
            Id = jobTitleId, TenantId = tenantId, TitleName = "Senior Engineer",
            IsActive = true, CreatedAt = now, CreatedBy = "seed",
        });

        db.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNo = "EMP-0001",
            FirstName = "Ada", LastName = "Lovelace", Email = $"ada@{subdomain}.test",
            DateOfJoining = now, DepartmentId = deptId, JobTitleId = jobTitleId,
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active,
            IsActive = true, CreatedAt = now, CreatedBy = "seed",
        });

        db.EmployeeDocuments.Add(new EmployeeDocument
        {
            Id = documentId, TenantId = tenantId, EmployeeId = employeeId,
            FileName = "contract.pdf",
            StorageKey = $"{tenantId}/core-hr/{employeeId}/2026/07/contract.pdf",
            FileSizeBytes = 1024, MimeType = "application/pdf",
            Category = DocumentCategory.Contract, UploadedBy = Guid.NewGuid(),
            CreatedAt = now, CreatedBy = "seed",
        });
    }

    /// <summary>System context: unresolved tenant ⇒ query filters see every tenant's rows (mirrors the Hangfire
    /// deletion job's system scope). Same shape as the InMemory suite's SystemTenantContext.</summary>
    private sealed class SystemTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public string Subdomain => "admin";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => true;
        public bool IsResolved => false;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }
}
