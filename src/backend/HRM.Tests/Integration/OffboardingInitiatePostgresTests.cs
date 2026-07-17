// ============================================================================
// BUG-289 class: offboarding INITIATE persists on real Postgres.
//
// InitiateAsync writes LastWorkingDay (from input.LastWorkingDay.Date) and each
// task's DueDate (from ClampDueDate(...)) into the timestamptz last_working_day /
// due_date columns. Plain `.Date` is Kind=Unspecified, which Npgsql rejects — so
// this used to throw on real Postgres (the InMemory suite hid it). This is the
// compound case (two write sites + ClampDueDate). Real Postgres via Testcontainers.
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
    public async Task Initiate_persists_last_working_day_and_task_due_dates_on_postgres_bug289()
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

        // A FUTURE last working day → LastWorkingDay + the derived (clamped) task due dates all write to
        // timestamptz columns; the write path must NOT throw on Postgres. Kind MUST be Unspecified (as a date
        // arrives from model binding): `.Date` PRESERVES Kind, so a Utc seed would let the mutant survive.
        var lwd = new DateTime(2026, 12, 1);
        await using var db = CreateContext();
        var result = await CreateService(db).InitiateAsync(new InitiateOffboardingInput(
            _employeeId, lwd, null, OffboardingReason.Resignation, null));

        result.IsSuccess.Should().BeTrue(result.Error);

        await using var verify = CreateContext();
        var instance = await verify.OffboardingInstances
            .Include(o => o.Tasks)
            .SingleAsync(o => o.EmployeeId == _employeeId);
        instance.LastWorkingDay.Kind.Should().Be(DateTimeKind.Utc);
        instance.Tasks.Should().NotBeEmpty();                       // default clearance tasks
        instance.Tasks.Should().OnlyContain(t => t.DueDate.Kind == DateTimeKind.Utc);
    }
}
