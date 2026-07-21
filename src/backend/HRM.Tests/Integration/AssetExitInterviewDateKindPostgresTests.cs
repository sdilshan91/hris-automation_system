// ============================================================================
// DF-1 (was BUG-290 class): Asset issuance (AssetService.IssueAsync → asset.issue_date) and Exit-interview
// (ExitInterviewService.RecordAsync → exit_interview.interview_date) now use real Postgres `date` columns
// mapped to `DateOnly` — the `.Date`→timestamptz `DateTimeKind` band-aids are gone. These tests prove the
// NEW contract on REAL Postgres: a `DateOnly` value round-trips through the `date` column with the correct
// calendar date — no DateTimeKind, no time component, and NO off-by-one regardless of DB session timezone
// (the exact failure the timestamptz remodel eliminates). postgres:17-alpine via Testcontainers.
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

public sealed class AssetExitInterviewDateKindPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

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
        cu.UserId.Returns(Guid.NewGuid());
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
            .Options, tc);
    }

    private (ITenantContext tc, ICurrentUser cu) Actors()
    {
        var tc = new MutableTenantContext { TenantId = _tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.Email.Returns("hr@acme.com");
        cu.UserId.Returns(Guid.NewGuid());
        return (tc, cu);
    }

    private async Task<Guid> SeedEmployee()
    {
        var employeeId = Guid.NewGuid();
        await using var seed = CreateContext();
        var deptId = Guid.NewGuid();
        var jobTitleId = Guid.NewGuid();
        seed.Departments.Add(new Department { Id = deptId, TenantId = _tenantId, Name = "Eng", Code = "ENG", IsActive = true });
        seed.JobTitles.Add(new JobTitle { Id = jobTitleId, TenantId = _tenantId, TitleName = "SWE", IsActive = true });
        seed.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = _tenantId, EmployeeNo = "EMP-0001",
            FirstName = "Ada", LastName = "Asset", Email = "ada@acme.com",
            DepartmentId = deptId, JobTitleId = jobTitleId,
            DateOfJoining = DateTime.SpecifyKind(DateTime.UtcNow.AddYears(-2).Date, DateTimeKind.Utc),
            Status = EmployeeStatus.Active,
        });
        await seed.SaveChangesAsync();
        return employeeId;
    }

    [Fact]
    [Trait("TC", "TC-ONB-004-13")]
    public async Task Asset_issuance_persists_issue_date_as_dateonly_on_postgres_df1()
    {
        var employeeId = await SeedEmployee();

        await using var db = CreateContext();
        var (tc, cu) = Actors();
        var service = new AssetService(db, tc, cu,
            Substitute.For<IFileStorage>(), Substitute.For<IVirusScanner>(), NullLogger<AssetService>.Instance);

        var issueDate = new DateOnly(2026, 7, 1);

        // AssetId null → a new asset is created; IssueDate is written to the real `date` issue_date column.
        var result = await service.IssueAsync(new IssueAssetsInput(
            employeeId, null,
            new[]
            {
                new IssueAssetLineInput(
                    AssetId: null, AssetType: "Laptop", AssetTag: "LT-0001", SerialNumber: "SN-1",
                    Brand: "Acme", Model: "X1", Condition: AssetCondition.Good,
                    IssueDate: issueDate, Notes: null),
            },
            AcknowledgmentStream: null, AcknowledgmentFileName: null,
            AcknowledgmentContentType: null, AcknowledgmentSize: 0));

        result.IsSuccess.Should().BeTrue(result.Error);

        await using var verify = CreateContext();
        var asset = await verify.Assets.AsNoTracking().SingleAsync(a => a.AssetTag == "LT-0001");
        asset.Status.Should().Be(AssetStatus.Assigned);
        asset.IssueDate.Should().NotBeNull();
        // DF-1 contract: the exact calendar date round-trips through the `date` column (no time, no off-by-one).
        asset.IssueDate!.Value.Should().Be(issueDate);
    }

    [Fact]
    [Trait("TC", "TC-ONB-006-13")]
    public async Task Exit_interview_persists_interview_date_as_dateonly_on_postgres_df1()
    {
        var offboardingId = Guid.NewGuid();
        await using (var seed = CreateContext())
        {
            // A minimal offboarding instance — RecordAsync loads it by id; the template auto-seeds on first use.
            seed.OffboardingInstances.Add(new OffboardingInstance
            {
                Id = offboardingId, TenantId = _tenantId, EmployeeId = Guid.NewGuid(),
                TemplateName = "Default", Reason = OffboardingReason.Resignation,
                LastWorkingDay = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            });
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext();
        var (tc, cu) = Actors();
        var service = new ExitInterviewService(db, tc, cu, NullLogger<ExitInterviewService>.Instance);

        var interviewDate = new DateOnly(2026, 7, 1);

        // InterviewDate is written to the real `date` interview_date column.
        var result = await service.RecordAsync(
            new RecordExitInterviewInput(
                OffboardingId: offboardingId, InterviewMode: "HrConducted",
                InterviewDate: interviewDate, Responses: Array.Empty<ExitInterviewResponseInput>(),
                OverallExperienceRating: 4, WouldRecommendEmployer: true, AdditionalComments: null),
            isSelfService: false, allowEdit: true);

        result.IsSuccess.Should().BeTrue(result.Error);

        await using var verify = CreateContext();
        var interview = await verify.ExitInterviews.AsNoTracking()
            .SingleAsync(i => i.OffboardingInstanceId == offboardingId);
        // DF-1 contract: the exact calendar date round-trips through the `date` column (no time, no off-by-one).
        interview.InterviewDate.Should().Be(interviewDate);
    }
}
