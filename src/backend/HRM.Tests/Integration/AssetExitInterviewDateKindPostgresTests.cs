// ============================================================================
// BUG-290 class: the two remaining `.Date`(Kind=Unspecified) → timestamptz write sites — Asset issuance
// (AssetService.IssueAsync → asset.issue_date) and Exit-interview (ExitInterviewService.RecordAsync →
// exit_interview.interview_date) — persist on REAL Postgres. Both were fixed with the same
// `DateTime.SpecifyKind(<.Date>, Utc)` idiom as the Postgres-proven Offboarding compound case, but were
// NOT independently verified on the real provider (the InMemory suites ignore DateTimeKind and hid the
// original throw). This closes that coverage gap. postgres:17-alpine via Testcontainers.
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
    public async Task Asset_issuance_persists_issue_date_utc_on_postgres_bug290()
    {
        var employeeId = await SeedEmployee();

        await using var db = CreateContext();
        var (tc, cu) = Actors();
        var service = new AssetService(db, tc, cu,
            Substitute.For<IFileStorage>(), Substitute.For<IVirusScanner>(), NullLogger<AssetService>.Instance);

        // AssetId null → a new asset is created; IssueDate is written to the timestamptz issue_date column.
        // `.Date` alone is Kind=Unspecified, which Npgsql rejects — the write used to throw on Postgres.
        var result = await service.IssueAsync(new IssueAssetsInput(
            employeeId, null,
            new[]
            {
                new IssueAssetLineInput(
                    AssetId: null, AssetType: "Laptop", AssetTag: "LT-0001", SerialNumber: "SN-1",
                    Brand: "Acme", Model: "X1", Condition: AssetCondition.Good,
                    // Kind=Unspecified, exactly as a date-only value arrives from model binding — this is what
                    // the fix must UTC-kind. `.Date` PRESERVES Kind, so a Utc input would not reproduce the bug.
                    IssueDate: new DateTime(2026, 7, 1), Notes: null),
            },
            AcknowledgmentStream: null, AcknowledgmentFileName: null,
            AcknowledgmentContentType: null, AcknowledgmentSize: 0));

        result.IsSuccess.Should().BeTrue(result.Error);

        await using var verify = CreateContext();
        var asset = await verify.Assets.AsNoTracking().SingleAsync(a => a.AssetTag == "LT-0001");
        asset.Status.Should().Be(AssetStatus.Assigned);
        asset.IssueDate.Should().NotBeNull();
        asset.IssueDate!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    [Trait("TC", "TC-ONB-006-13")]
    public async Task Exit_interview_persists_interview_date_utc_on_postgres_bug290()
    {
        var offboardingId = Guid.NewGuid();
        await using (var seed = CreateContext())
        {
            // A minimal offboarding instance — RecordAsync loads it by id; the template auto-seeds on first use.
            seed.OffboardingInstances.Add(new OffboardingInstance
            {
                Id = offboardingId, TenantId = _tenantId, EmployeeId = Guid.NewGuid(),
                TemplateName = "Default", Reason = OffboardingReason.Resignation,
                LastWorkingDay = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(14).Date, DateTimeKind.Utc),
            });
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext();
        var (tc, cu) = Actors();
        var service = new ExitInterviewService(db, tc, cu, NullLogger<ExitInterviewService>.Instance);

        // InterviewDate is written to the timestamptz interview_date column via SpecifyKind(.Date, Utc).
        var result = await service.RecordAsync(
            new RecordExitInterviewInput(
                OffboardingId: offboardingId, InterviewMode: "HrConducted",
                // Kind=Unspecified input (as it arrives from model binding) — the value the fix must UTC-kind.
                InterviewDate: new DateTime(2026, 7, 1), Responses: Array.Empty<ExitInterviewResponseInput>(),
                OverallExperienceRating: 4, WouldRecommendEmployer: true, AdditionalComments: null),
            isSelfService: false, allowEdit: true);

        result.IsSuccess.Should().BeTrue(result.Error);

        await using var verify = CreateContext();
        var interview = await verify.ExitInterviews.AsNoTracking()
            .SingleAsync(i => i.OffboardingInstanceId == offboardingId);
        interview.InterviewDate.Kind.Should().Be(DateTimeKind.Utc);
    }
}
