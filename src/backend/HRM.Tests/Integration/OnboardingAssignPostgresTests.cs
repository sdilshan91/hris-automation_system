// ============================================================================
// BUG-289: onboarding checklist ASSIGN persists on real Postgres.
//
// HARNESS = real Postgres via Testcontainers. This is load-bearing: AssignAsync
// computes start/due dates via `.Date` (Kind=Unspecified) and writes them to the
// timestamptz start_date/due_date columns. The InMemory provider ignores Kind and
// hid this; Npgsql REJECTS Kind=Unspecified for `timestamp with time zone`, so the
// assign SaveChanges used to throw. The UtcMidnight() fix makes the write valid.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Onboarding.DTOs;
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

public sealed class OnboardingAssignPostgresTests : IAsyncLifetime
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

    private OnboardingChecklistService CreateService(AppDbContext db)
    {
        var tc = new MutableTenantContext { TenantId = _tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.Email.Returns("hr@acme.com");
        var scanner = Substitute.For<IVirusScanner>();
        scanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(VirusScanResult.Clean());
        return new OnboardingChecklistService(
            db, tc, cu, Substitute.For<IFileStorage>(), scanner, NullLogger<OnboardingChecklistService>.Instance);
    }

    [Fact]
    public async Task Assign_persists_on_real_postgres_bug289()
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
                FirstName = "Nora", LastName = "Newhire", Email = "nora@acme.com",
                DepartmentId = deptId, JobTitleId = jobTitleId,
                // A FUTURE joining date (the realistic new-hire case) so startDate comes from DateOfJoining —
                // this exercises the primary write site (instance.StartDate + task due dates from it).
                DateOfJoining = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(10).Date, DateTimeKind.Utc),
            });
            seed.OnboardingChecklistTemplates.Add(new OnboardingChecklistTemplate
            {
                Id = _templateId, TenantId = _tenantId, TemplateName = "Standard Onboarding", IsActive = true,
                ApplicableDepartments = [], ApplicableJobTitles = [],
                Tasks =
                [
                    new OnboardingTemplateTask
                    {
                        Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, TemplateId = _templateId,
                        Title = "Sign contract", Category = "Docs", ResponsibleRole = OnboardingResponsibleRole.HR,
                        DueOffsetDays = 3, IsMandatory = true, SortOrder = 0,
                    },
                ],
            });
            await seed.SaveChangesAsync();
        }

        // The assign write path (instance.StartDate + task due dates → timestamptz) must NOT throw on Postgres.
        await using var db = CreateContext();
        var result = await CreateService(db).AssignAsync(new AssignChecklistInput(
            _employeeId, _templateId, null, ChecklistAssignmentMode.Replace,
            Array.Empty<AdHocTaskInput>(), null));

        result.IsSuccess.Should().BeTrue(result.Error);

        await using var verify = CreateContext();
        var instance = await verify.OnboardingChecklistInstances
            .Include(c => c.Tasks)
            .SingleAsync(c => c.EmployeeId == _employeeId && c.Status == OnboardingChecklistStatus.Active);
        instance.Tasks.Should().ContainSingle();
        // The start date persisted (and round-trips) at UTC midnight.
        instance.StartDate.Kind.Should().Be(DateTimeKind.Utc);
    }
}
