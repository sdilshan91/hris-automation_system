// ============================================================================
// ISSUE-427 — the ISSUE-364 batched department projection, on REAL POSTGRES.
//
// WHY THIS FILE EXISTS. ISSUE-364 added EmployeeCount + ManagerName to the department list, and did it
// with a BATCHED projection to avoid an N+1 (DepartmentService.GetAllAsync):
//
//     .Where(e => departmentIds.Contains(e.DepartmentId) && e.IsActive)
//     .GroupBy(e => e.DepartmentId)
//     .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
//     .ToDictionaryAsync(...)
//
// and a second `ToDictionaryAsync` over `e.FirstName + " " + e.LastName` for manager names. The only
// coverage was DepartmentServiceTests (EF InMemory), and **a real GROUP BY translation is precisely what
// InMemory cannot prove**: InMemory is a LINQ-to-Objects provider, so it client-evaluates the grouping,
// the `Contains` and the string concat. A projection that fails to translate to SQL goes GREEN there and
// throws `InvalidOperationException: could not be translated` in production. A passing InMemory test
// proved the LINQ, not the query.
//
// So these arms re-assert the ISSUE-364 behaviour against Npgsql, where the grouping must become a real
// `GROUP BY`, `Contains` a real `= ANY(...)`, and the concat a real `||` under Postgres collation. The
// InMemory arms are deliberately KEPT as fast feedback; this is an addition, not a replacement.
//
// PLUS a cross-tenant arm, which the InMemory suite could not make meaningful: the batched projection's
// tenant scoping comes entirely from AppDbContext's global query filter, and on a shared database
// sibling tenants' rows are genuinely present in the table. A leak therefore FAILS here rather than
// being masked by a pristine database.
//
// FIXTURE: shared `IClassFixture<PostgresContainerFixture>` (container + migrations once per CLASS)
// rather than per-test `IAsyncLifetime` containers (~20s per test). Every arm below is tenant-scoped,
// so no `IgnoreQueryFilters()` is needed — which is also what `SharedPostgresFixtureIsolationTests`
// (HRM.ArchitectureTests) enforces for any class sharing this fixture.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Departments.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-CHR-004-PG")]
public sealed class DepartmentPostgresTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _pg;

    // Fresh per TEST (xUnit builds a new test-class instance per method), so sibling tests are isolated
    // from each other by the tenant query filter even though they share the database.
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Dictionary<Guid, Guid> _jobTitles = [];

    public DepartmentPostgresTests(PostgresContainerFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        using var db = Db(_tenantA);

        // Real FKs are enforced here (InMemory ignored them), so tenant + job-title rows must exist
        // before any employee can be inserted.
        foreach (var (tenantId, subdomain) in new[]
                 {
                     (_tenantA, $"a{_tenantA:N}"[..12]),
                     (_tenantB, $"b{_tenantB:N}"[..12]),
                 })
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, Subdomain = subdomain, Name = subdomain,
                DefaultCountryCode = "LK", FiscalYearStartMonth = 1,
            });
            var jobId = BaseEntity.NewUuidV7();
            _jobTitles[tenantId] = jobId;
            db.JobTitles.Add(new JobTitle
            {
                Id = jobId, TenantId = tenantId, TitleName = "Engineer", IsActive = true,
            });
        }
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "test";
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

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        cu.TenantId.Returns(tenantId);
        cu.Email.Returns("admin@test.com");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_pg.ConnectionString, n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(ctx), new AuditInterceptor(cu))
            .Options;
        return new AppDbContext(options, ctx);
    }

    private (AppDbContext Db, DepartmentService Svc) Scope(Guid tenantId)
    {
        var db = Db(tenantId);
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        cu.TenantId.Returns(tenantId);
        cu.Email.Returns("admin@test.com");
        return (db, new DepartmentService(db, ctx, cu, NullLogger<DepartmentService>.Instance));
    }

    private async Task<IReadOnlyList<DepartmentDto>> GetAll(Guid tenantId)
    {
        var (db, svc) = Scope(tenantId);
        using (db)
        {
            var result = await svc.GetAllAsync();
            result.IsSuccess.Should().BeTrue(result.Error);
            return result.Value!;
        }
    }

    // ── Seeding (FK-valid: tenant + job title exist, department precedes its employees) ──────────

    private async Task<Guid> SeedDepartment(Guid tenantId, string name, string code, bool isActive = true)
    {
        using var db = Db(tenantId);
        var id = BaseEntity.NewUuidV7();
        db.Departments.Add(new Department
        {
            Id = id, TenantId = tenantId, Name = name, Code = code, IsActive = isActive,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedEmployee(
        Guid tenantId, string firstName, string lastName, Guid departmentId, bool isActive = true)
    {
        using var db = Db(tenantId);
        var id = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = id, TenantId = tenantId,
            // Postgres enforces ix_employees_tenant_id_employee_no, which InMemory ignores. `id` is a
            // UUIDv7, so its leading hex is a TIMESTAMP — two employees seeded in the same millisecond
            // share that prefix and collide. Derive the number from a v4 Guid instead (observed 23505).
            EmployeeNo = $"E{Guid.NewGuid():N}"[..12],
            FirstName = firstName, LastName = lastName,
            Email = $"{firstName}.{lastName}.{Guid.NewGuid():N}@t.com".ToLowerInvariant(),
            DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = departmentId, JobTitleId = _jobTitles[tenantId],
            EmploymentType = EmploymentType.FullTime,
            // The projection filters on IsActive; keep Status coherent with it so the row is realistic.
            Status = isActive ? EmployeeStatus.Active : EmployeeStatus.Terminated,
            IsActive = isActive,
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>
    /// Department.ManagerId is an FK to Employee, and Employee.DepartmentId is a required FK back to
    /// Department — so the manager is attached after both rows exist rather than in one insert.
    /// </summary>
    private async Task SetManager(Guid tenantId, Guid departmentId, Guid managerId)
    {
        using var db = Db(tenantId);
        var dept = await db.Departments.SingleAsync(d => d.Id == departmentId);
        dept.ManagerId = managerId;
        await db.SaveChangesAsync();
    }

    // ── ISSUE-364 arm 1: the GROUP BY must translate, and count only ACTIVE employees ─────────────

    [Fact]
    public async Task GetAll_returns_the_ACTIVE_employee_count_per_department_issue364()
    {
        var engId = await SeedDepartment(_tenantA, "Engineering", "ENG");
        var otherId = await SeedDepartment(_tenantA, "Other", "OTH");
        await SeedEmployee(_tenantA, "Ann", "One", engId);
        await SeedEmployee(_tenantA, "Bob", "Two", engId);
        await SeedEmployee(_tenantA, "Ex", "Employee", engId, isActive: false);   // must NOT count
        await SeedEmployee(_tenantA, "Other", "Dept", otherId);                   // different department

        var result = await GetAll(_tenantA);

        result.Single(d => d.Code == "ENG").EmployeeCount
            .Should().Be(2, "only ACTIVE employees of THIS department count");
        result.Single(d => d.Code == "OTH").EmployeeCount
            .Should().Be(1, "the GROUP BY key must partition by department, not collapse to a total");
    }

    /// <summary>
    /// A department with zero active employees has no GROUP BY row at all, so the dictionary lookup —
    /// not the query — is what must yield 0. On InMemory that path is identical; on Postgres it is the
    /// difference between a missing key and a returned zero.
    /// </summary>
    [Fact]
    public async Task GetAll_reports_zero_for_a_department_with_no_active_employees_issue364()
    {
        var emptyId = await SeedDepartment(_tenantA, "Empty", "EMP");
        await SeedEmployee(_tenantA, "Gone", "Away", emptyId, isActive: false);

        var result = await GetAll(_tenantA);

        result.Single(d => d.Code == "EMP").EmployeeCount.Should().Be(0);
    }

    // ── ISSUE-364 arm 2: manager display name (a real `||` concat under Npgsql) ───────────────────

    [Fact]
    public async Task GetAll_returns_the_manager_display_name_issue364()
    {
        var homeId = await SeedDepartment(_tenantA, "Home", "HOM");
        var managerId = await SeedEmployee(_tenantA, "Jane", "Smith", homeId);
        var managedId = await SeedDepartment(_tenantA, "Managed", "MGD");
        await SetManager(_tenantA, managedId, managerId);

        var result = await GetAll(_tenantA);

        result.Single(d => d.Code == "MGD").ManagerName.Should().Be("Jane Smith");
    }

    // ── ISSUE-364 arm 3: no manager -> null name, and no phantom count ────────────────────────────

    [Fact]
    public async Task GetAll_leaves_manager_name_null_when_no_manager_is_set_issue364()
    {
        await SeedDepartment(_tenantA, "Unmanaged", "UNM");

        var result = await GetAll(_tenantA);

        var unmanaged = result.Single(d => d.Code == "UNM");
        unmanaged.ManagerName.Should().BeNull();
        unmanaged.EmployeeCount.Should().Be(0);
    }

    // ── ISSUE-427 cross-tenant arm: the batched projection is tenant-scoped, not merely assumed ───

    [Fact]
    public async Task GetAll_excludes_other_tenants_departments_and_their_employee_counts()
    {
        var engA = await SeedDepartment(_tenantA, "Engineering", "ENG");
        await SeedEmployee(_tenantA, "Ann", "One", engA);
        await SeedEmployee(_tenantA, "Bob", "Two", engA);

        // Tenant B's rows are physically present in the SAME tables of the SAME database. Only the
        // global query filter keeps them out of tenant A's list and out of tenant A's counts.
        var engB = await SeedDepartment(_tenantB, "Engineering", "ENG");
        var salesB = await SeedDepartment(_tenantB, "Sales", "SLS");
        await SeedEmployee(_tenantB, "Carl", "Three", engB);
        await SeedEmployee(_tenantB, "Dina", "Four", engB);
        await SeedEmployee(_tenantB, "Erin", "Five", engB);
        var managerB = await SeedEmployee(_tenantB, "Boss", "Bee", salesB);
        await SetManager(_tenantB, salesB, managerB);

        var resultA = await GetAll(_tenantA);

        resultA.Should().NotContain(d => d.Code == "SLS", "tenant B's department must not appear");
        resultA.Should().ContainSingle(d => d.Code == "ENG", "the same code exists in both tenants");
        resultA.Single(d => d.Code == "ENG").EmployeeCount
            .Should().Be(2, "tenant B's 3 employees must not be aggregated into tenant A's count");

        // And the mirror direction: tenant B sees its own rows, not tenant A's.
        var resultB = await GetAll(_tenantB);
        resultB.Single(d => d.Code == "ENG").EmployeeCount.Should().Be(3);
        resultB.Single(d => d.Code == "SLS").ManagerName.Should().Be("Boss Bee");
    }
}
