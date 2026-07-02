// ============================================================================
// US-REC-010 / BUG-068 regression: converting an applicant must succeed under
// the Npgsql RETRYING execution strategy (production config,
// EnableRetryOnFailure). This is the one path the sibling InMemory integration
// test structurally cannot cover — InMemory has no execution strategy and no
// transactions, so a user-initiated BeginTransactionAsync never throws there.
//
// PROVIDER: a real PostgreSQL Testcontainer, with the AppDbContext configured
// EXACTLY like production (UseNpgsql + EnableRetryOnFailure + snake_case + the
// same SaveChanges interceptors) so the retrying execution strategy is active.
//
// Pre-fix: ConvertAsync called Database.BeginTransactionAsync() directly, which
// NpgsqlRetryingExecutionStrategy rejects with InvalidOperationException
// ("The configured execution strategy 'NpgsqlRetryingExecutionStrategy' does
// not support user-initiated transactions...") → HTTP 500 on every convert.
// Post-fix: the atomic unit runs inside Database.CreateExecutionStrategy()
// .ExecuteAsync(...), so it commits normally.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
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

public sealed class ApplicantConversionRetryStrategyTests : IAsyncLifetime
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
        // Mirror production DI (DependencyInjection.AddInfrastructure): Npgsql + the RETRYING
        // execution strategy + snake_case + the SaveChanges interceptors. The retry strategy is
        // what makes a direct BeginTransactionAsync throw (BUG-068).
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                new TenantInterceptor(tenantContext),
                new AuditInterceptor(currentUser),
                new AuditCaptureInterceptor(tenantContext, currentUser))
            .Options;

        return new AppDbContext(options, tenantContext);
    }

    private ICurrentUser MakeCurrentUser()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.Email.Returns("hr@acme.com");
        currentUser.TenantId.Returns(_tenantId);
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Roles.Returns(new List<string>());
        currentUser.Permissions.Returns(new List<string>());
        return currentUser;
    }

    private ApplicantConversionService BuildService(AppDbContext db, ITenantContext tenantContext, ICurrentUser currentUser)
    {
        var virusScanner = Substitute.For<IVirusScanner>();
        var customFields = Substitute.For<ICustomFieldService>();
        customFields.ValidateCustomFieldValuesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var fileStorage = Substitute.For<IFileStorage>();

        var employeeService = new EmployeeService(
            db, tenantContext, currentUser, fileStorage, virusScanner, customFields,
            NullLogger<EmployeeService>.Instance);

        return new ApplicantConversionService(
            db, tenantContext, currentUser, employeeService,
            new LogOnlyRecruitmentNotificationService(NullLogger<LogOnlyRecruitmentNotificationService>.Instance),
            NullLogger<ApplicantConversionService>.Instance);
    }

    [Fact]
    public async Task Convert_UnderRetryingExecutionStrategy_Succeeds()
    {
        var tenantContext = new MutableTenantContext { TenantId = _tenantId };
        var currentUser = MakeCurrentUser();

        await using var db = CreateContext(tenantContext, currentUser);
        await db.Database.MigrateAsync();

        // Seed the minimal convertible pipeline: a Hired applicant with an Accepted offer on an
        // open vacancy, plus the Core HR master data EmployeeService validates.
        var deptId = Guid.NewGuid();
        var jobTitleId = Guid.NewGuid();
        var vacancyId = Guid.NewGuid();
        var applicantId = Guid.NewGuid();

        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme Corp" });
        db.Departments.Add(new Department { Id = deptId, TenantId = _tenantId, Name = "Engineering", Code = "ENG", IsActive = true });
        db.JobTitles.Add(new JobTitle { Id = jobTitleId, TenantId = _tenantId, TitleName = "Backend Engineer", IsActive = true });
        db.Vacancies.Add(new Vacancy
        {
            Id = vacancyId,
            TenantId = _tenantId,
            ReferenceNumber = "VAC-2026-0001",
            Title = "Backend Engineer",
            Status = VacancyStatus.Open,
            EmploymentType = EmploymentType.FullTime,
            Headcount = 2,
            FilledCount = 0,
            Description = "Build things.",
        });
        db.Applicants.Add(new Applicant
        {
            Id = applicantId,
            TenantId = _tenantId,
            VacancyId = vacancyId,
            ApplicationReferenceNumber = "APP-2026-0001",
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada@acme.com",
            Phone = "+15551234567",
            ResumeStorageKey = $"recruitment/{vacancyId}/x/z.pdf",
            ResumeFileName = "resume.pdf",
            Stage = ApplicantStage.Hired,
            Source = ApplicationSource.Public,
            AppliedAt = DateTime.UtcNow,
        });
        db.Offers.Add(new Offer
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ApplicantId = applicantId,
            VacancyId = vacancyId,
            OfferReferenceNumber = "OFR-2026-0001",
            Status = OfferStatus.Accepted,
            Response = "Accepted",
            OfferedPosition = "Backend Engineer",
            DepartmentId = deptId,
            SalaryAmount = 120000m,
            Currency = "USD",
            SalaryFrequency = SalaryFrequency.Annual,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            ProbationMonths = 3,
            Version = 1,
            RespondedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var service = BuildService(db, tenantContext, currentUser);

        var result = await service.ConvertAsync(new ConvertApplicantToEmployeeInput
        {
            ApplicantId = applicantId,
            DepartmentId = deptId,
            JobTitleId = jobTitleId,
            EmploymentType = EmploymentType.FullTime,
            DateOfJoining = DateTime.UtcNow.Date,
        });

        // Pre-fix this threw InvalidOperationException (retrying strategy rejects the user-initiated
        // BeginTransactionAsync). Post-fix the conversion commits.
        result.IsSuccess.Should().BeTrue();
        result.Value!.EmployeeId.Should().NotBeEmpty();
        result.Value.EmployeeNo.Should().NotBeNullOrWhiteSpace();

        var applicant = await db.Applicants.AsNoTracking().FirstAsync(a => a.Id == applicantId);
        applicant.ConvertedToEmployeeId.Should().Be(result.Value.EmployeeId);
    }
}
