// ============================================================================
// DF-1 (was BUG-289 class): offboarding INITIATE persists on real Postgres.
//
// InitiateAsync writes LastWorkingDay (from input.LastWorkingDay) and each task's
// DueDate (from ClampDueDate(...)) into real `date` last_working_day / due_date
// columns mapped to DateOnly. This proves the NEW contract on real Postgres: the
// exact calendar dates round-trip through the `date` columns with no off-by-one
// (regardless of DB session timezone) — the failure the timestamptz remodel kills.
// Real Postgres via Testcontainers.
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

public sealed class OffboardingInitiatePostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

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

    private OffboardingService CreateService(AppDbContext db)
    {
        var tc = new MutableTenantContext { TenantId = _tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.Email.Returns("hr@acme.com");
        cu.UserId.Returns(Guid.NewGuid());
        return new OffboardingService(
            db, tc, cu, Substitute.For<IAuthService>(), Substitute.For<ISessionRevoker>(),
            Substitute.For<IPayrollFnFIntegration>(), NullLogger<OffboardingService>.Instance);
    }

    [Fact]
    [Trait("TC", "TC-ONB-005-13")]
    public async Task Initiate_persists_last_working_day_and_task_due_dates_on_postgres_df1()
    {
        await using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            var deptId = Guid.NewGuid();
            var jobTitleId = Guid.NewGuid();
            seed.Departments.Add(new Department { Id = deptId, TenantId = _tenantId, Name = "Eng", Code = "ENG", IsActive = true });
            seed.JobTitles.Add(new JobTitle { Id = jobTitleId, TenantId = _tenantId, TitleName = "SWE", IsActive = true });
            seed.Employees.Add(new Employee
            {
                Id = _employeeId, TenantId = _tenantId, EmployeeNo = "EMP-0001",
                FirstName = "Nora", LastName = "Leaver", Email = "nora@acme.com",
                DepartmentId = deptId, JobTitleId = jobTitleId,
                DateOfJoining = DateTime.SpecifyKind(DateTime.UtcNow.AddYears(-2).Date, DateTimeKind.Utc),
                // Offboarding may only be initiated for a Terminated/Suspended employee.
                Status = EmployeeStatus.Terminated,
            });
            await seed.SaveChangesAsync();
        }

        // A FUTURE last working day → LastWorkingDay + the derived (clamped) task due dates all write to the
        // real `date` columns; the exact calendar dates must round-trip on Postgres with no off-by-one.
        var lwd = new DateOnly(2026, 12, 1);
        await using var db = CreateContext();
        var result = await CreateService(db).InitiateAsync(new InitiateOffboardingInput(
            _employeeId, lwd, null, OffboardingReason.Resignation, null));

        result.IsSuccess.Should().BeTrue(result.Error);

        await using var verify = CreateContext();
        var instance = await verify.OffboardingInstances
            .Include(o => o.Tasks)
            .SingleAsync(o => o.EmployeeId == _employeeId);
        // DF-1 contract: the last working day round-trips exactly through the `date` column.
        instance.LastWorkingDay.Should().Be(lwd);
        instance.Tasks.Should().NotBeEmpty();                       // default clearance tasks
        // FR-2: due_date = LWD - offset_days. "Return IT assets" has offset 0 → due == LWD (round-trips exactly).
        instance.Tasks.Single(t => t.Title == "Return IT assets").DueDate.Should().Be(lwd);
        // "Knowledge transfer and handover" has offset 3 → due == LWD - 3 days.
        instance.Tasks.Single(t => t.Title == "Knowledge transfer and handover").DueDate.Should().Be(lwd.AddDays(-3));
    }

    /// <summary>
    /// B4 / AC-5 on REAL Postgres: the completion gate projected onto the instance DTO
    /// (<c>CanComplete</c> + <c>PendingMandatoryItems</c>) behaves the same as it does on EF InMemory.
    ///
    /// <para>
    /// The gate itself is pure LINQ over an already-materialised list, so it carries no provider risk. What
    /// does is the bit underneath it: a soft-deleted task must be excluded by the global query filter
    /// (<c>AppDbContext</c>'s <c>HasQueryFilter(t =&gt; !t.IsDeleted)</c>) reaching through the
    /// <c>Include(o =&gt; o.Tasks)</c> on Npgsql — filter-through-Include is exactly the kind of behaviour
    /// InMemory can agree with by accident. If it did not hold, a removed mandatory task would block
    /// completion forever with no way to satisfy it.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ONB-005-13")]
    public async Task Completion_gate_projection_matches_enforcement_on_postgres_b4()
    {
        await using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            var deptId = Guid.NewGuid();
            var jobTitleId = Guid.NewGuid();
            seed.Departments.Add(new Department { Id = deptId, TenantId = _tenantId, Name = "Eng", Code = "ENG", IsActive = true });
            seed.JobTitles.Add(new JobTitle { Id = jobTitleId, TenantId = _tenantId, TitleName = "SWE", IsActive = true });
            seed.Employees.Add(new Employee
            {
                Id = _employeeId, TenantId = _tenantId, EmployeeNo = "EMP-0002",
                FirstName = "Otto", LastName = "Exit", Email = "otto@acme.com",
                DepartmentId = deptId, JobTitleId = jobTitleId,
                DateOfJoining = DateTime.SpecifyKind(DateTime.UtcNow.AddYears(-2).Date, DateTimeKind.Utc),
                Status = EmployeeStatus.Terminated,
            });
            await seed.SaveChangesAsync();
        }

        Guid instanceId;
        await using (var db = CreateContext())
        {
            var initiated = await CreateService(db).InitiateAsync(new InitiateOffboardingInput(
                _employeeId, new DateOnly(2026, 12, 1), null, OffboardingReason.Resignation, null));
            initiated.IsSuccess.Should().BeTrue(initiated.Error);
            instanceId = initiated.Value!.Id;
        }

        // Nothing cleared yet: the projection must name the blockers, and the enforcement must refuse.
        await using (var db = CreateContext())
        {
            var projected = (await CreateService(db).GetByIdAsync(instanceId)).Value!;
            projected.CanComplete.Should().BeFalse();
            projected.PendingMandatoryItems.Should().NotBeEmpty();
        }

        await using (var db = CreateContext())
        {
            var attempt = (await CreateService(db).CompleteAsync(instanceId)).Value!;
            attempt.Completed.Should().BeFalse("the projection said it would be refused");
            attempt.PendingItems.Select(i => i.TaskId).Should().BeEquivalentTo(
                (await CreateService(CreateContext()).GetByIdAsync(instanceId)).Value!
                    .PendingMandatoryItems.Select(i => i.TaskId),
                "the dashboard renders one list and the endpoint enforces the other; on real Postgres too, "
                + "they must be the same list");
        }

        // Soft-delete every mandatory task. The global query filter must drop them THROUGH the Include, so
        // nothing blocks any more.
        await using (var db = CreateContext())
        {
            var tasks = await db.OffboardingTaskInstances
                .Where(t => t.OffboardingInstanceId == instanceId && t.IsMandatory)
                .ToListAsync();
            tasks.Should().NotBeEmpty("the arm is vacuous if there were no mandatory tasks to remove");
            foreach (var t in tasks)
            {
                t.IsDeleted = true;
            }
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext())
        {
            var projected = (await CreateService(db).GetByIdAsync(instanceId)).Value!;
            projected.PendingMandatoryItems.Should().BeEmpty(
                "a soft-deleted task cannot be completed, so blocking on it strands the offboarding");
            projected.CanComplete.Should().BeTrue();
        }
    }
}
