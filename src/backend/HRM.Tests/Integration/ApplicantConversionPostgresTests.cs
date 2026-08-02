// ============================================================================
// US-REC-010 (AC-2/AC-4/FR-6/FR-7/NFR-3) — ApplicantConversionService on REAL PostgreSQL.
//
// The sibling ApplicantConversionIntegrationTests runs on the EF InMemory provider. Its own
// header note explains the gap: the conversion's atomic unit (employee create + applicant
// linkage + vacancy filled-count update) is wrapped in a manual DB transaction that is guarded
// behind Database.IsRelational() — so on InMemory the BeginTransactionAsync is SKIPPED and a
// single SaveChanges runs. The real relational transaction path is therefore never exercised
// by that suite (the BUG-068 "InMemory-masks-Postgres" class).
//
// This harness runs the same conversion against a Testcontainers Postgres so the manual
// transaction actually executes. Critically it configures the DbContext WITH
// EnableRetryOnFailure (matching production, DependencyInjection.cs:41): ApplicantConversionService
// wraps its BeginTransactionAsync inside CreateExecutionStrategy().ExecuteAsync(...) (the BUG-068
// fix, service lines 142-163), so this test also proves that fix holds under the real Npgsql
// retrying execution strategy — a raw user-initiated BeginTransaction would throw there.
//
// A green run proves the conversion COMMITS ATOMICALLY on Postgres: the new employee and the
// updated applicant + vacancy are all visible together on a FRESH context after commit.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.Commands;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

/// <summary>
/// US-REC-010: proves the applicant→employee conversion runs atomically inside a real Postgres
/// transaction (through the Npgsql retrying execution strategy) — the employee is created and the
/// applicant + vacancy are updated in one committed unit, all visible on a fresh context.
/// </summary>
public sealed class ApplicantConversionPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deptId = Guid.NewGuid();
    private readonly Guid _jobTitleId = Guid.NewGuid();
    private readonly Guid _managerId = Guid.NewGuid();
    private readonly Guid _vacancyId = Guid.NewGuid();
    private readonly Guid _applicantId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var tenantContext = new MutableTenantContext { TenantId = _tenantId };
        await using var db = NewContext(tenantContext);
        await db.Database.MigrateAsync();

        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme Corp" });
        db.Departments.Add(new Department
        {
            Id = _deptId, TenantId = _tenantId, Name = "Engineering", Code = "ENG", IsActive = true,
        });
        db.JobTitles.Add(new JobTitle
        {
            Id = _jobTitleId, TenantId = _tenantId, TitleName = "Senior Backend Engineer", IsActive = true,
        });
        db.Employees.Add(new Employee
        {
            Id = _managerId, TenantId = _tenantId, EmployeeNo = "EMP-0001",
            FirstName = "Grace", LastName = "Hopper", Email = "grace@acme.com",
            DateOfJoining = DateTime.UtcNow.AddYears(-2), DepartmentId = _deptId, JobTitleId = _jobTitleId,
            EmploymentType = EmploymentType.FullTime, IsActive = true,
        });
        db.Vacancies.Add(new Vacancy
        {
            Id = _vacancyId, TenantId = _tenantId, ReferenceNumber = "VAC-2026-0001",
            Title = "Backend Engineer", Status = VacancyStatus.Open, EmploymentType = EmploymentType.FullTime,
            Headcount = 2, FilledCount = 0, Description = "Build things.", IsDeleted = false,
        });
        db.Applicants.Add(new Applicant
        {
            Id = _applicantId, TenantId = _tenantId, VacancyId = _vacancyId,
            ApplicationReferenceNumber = "APP-2026-0001", FirstName = "Ada", LastName = "Lovelace",
            Email = "ada@acme.com", Phone = "+15551234567",
            ResumeStorageKey = $"recruitment/{_vacancyId}/x/z.pdf", ResumeFileName = "resume.pdf",
            Stage = ApplicantStage.Hired, Source = ApplicationSource.Public,
            AppliedAt = DateTime.UtcNow, IsDeleted = false,
        });
        db.Offers.Add(new Offer
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, ApplicantId = _applicantId, VacancyId = _vacancyId,
            OfferReferenceNumber = "OFR-2026-0001", Status = OfferStatus.Accepted, Response = "Accepted",
            OfferedPosition = "Senior Backend Engineer", DepartmentId = _deptId,
            ReportingManagerEmployeeId = _managerId, SalaryAmount = 120000m, Currency = "USD",
            SalaryFrequency = SalaryFrequency.Annual,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            ProbationMonths = 3, Version = 1, RespondedAt = DateTime.UtcNow, IsDeleted = false,
        });

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ── Conversion commits atomically on real Postgres (transaction under the retry strategy) ──

    [Fact]
    public async Task Convert_HiredAcceptedApplicant_CommitsEmployeeAndApplicantAtomically()
    {
        var mediator = BuildPipeline();

        var result = await mediator.Send(new ConvertApplicantToEmployeeCommand(
            ApplicantId: _applicantId, JobTitleId: _jobTitleId, DepartmentId: _deptId,
            EmploymentType: EmploymentType.FullTime, DateOfJoining: DateTime.UtcNow.AddDays(30),
            ReportsToEmployeeId: _managerId, LocationId: null, EmployeeNo: null,
            DateOfBirth: null, Gender: null));

        result.IsSuccess.Should().BeTrue(result.Error);
        var dto = result.Value!;
        dto.EmployeeId.Should().NotBeEmpty();
        dto.EmployeeNo.Should().NotBeNullOrWhiteSpace();

        // Fresh context — everything below was read back from the committed transaction on Postgres.
        await using var verify = NewContext(new MutableTenantContext { TenantId = _tenantId });

        // (1) Employee created with the mapped applicant/offer fields.
        var emp = await verify.Employees.AsNoTracking().FirstAsync(e => e.Id == dto.EmployeeId);
        emp.FirstName.Should().Be("Ada");
        emp.LastName.Should().Be("Lovelace");
        emp.Email.Should().Be("ada@acme.com");
        emp.DepartmentId.Should().Be(_deptId);
        emp.JobTitleId.Should().Be(_jobTitleId);
        emp.ReportsToEmployeeId.Should().Be(_managerId);
        emp.TenantId.Should().Be(_tenantId);

        // (2) Applicant linked to the new employee — same committed unit (atomicity).
        var applicant = await verify.Applicants.AsNoTracking().FirstAsync(a => a.Id == _applicantId);
        applicant.ConvertedToEmployeeId.Should().Be(dto.EmployeeId);
        applicant.ConvertedAt.Should().NotBeNull();
        applicant.ConvertedByUserId.Should().Be(_userId);
        applicant.Stage.Should().Be(ApplicantStage.Hired); // BR-6: not deleted / not stage-mutated.

        // (3) Vacancy filled-count incremented in the same unit (headcount 2 ⇒ stays Open).
        var vacancy = await verify.Vacancies.AsNoTracking().FirstAsync(v => v.Id == _vacancyId);
        vacancy.FilledCount.Should().Be(1);
        vacancy.Status.Should().Be(VacancyStatus.Open);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A DbContext on the container, WITH EnableRetryOnFailure — production-faithful (the conversion's
    /// BeginTransaction must run inside the execution strategy). Interceptors mirror production DI.
    /// </summary>
    private AppDbContext NewContext(ITenantContext tenantContext)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(_userId);
        currentUser.Email.Returns("hr@acme.com");
        currentUser.TenantId.Returns(_tenantId);

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

    /// <summary>Composes the real MediatR + service graph (mirrors the InMemory suite's BuildPipeline) against Postgres.</summary>
    private IMediator BuildPipeline()
    {
        var tenantContext = new MutableTenantContext { TenantId = _tenantId };

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(_userId);
        currentUser.Email.Returns("hr@acme.com");
        currentUser.TenantId.Returns(_tenantId);
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Roles.Returns(new List<string>());
        currentUser.Permissions.Returns(new List<string>());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(_postgres.GetConnectionString(), n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tenantContext), new AuditInterceptor(currentUser)));

        services.AddSingleton<IFileStorage, InMemoryFileStorage>();
        services.AddSingleton(Substitute.For<IVirusScanner>());

        var customFields = Substitute.For<ICustomFieldService>();
        customFields.ValidateCustomFieldValuesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        services.AddSingleton(customFields);

        // ISSUE-293: EmployeeService now depends on IPayrollAuditLogger (National-ID reveal audit).
        // The conversion path never reveals, so a substitute suffices.
        services.AddSingleton(Substitute.For<IPayrollAuditLogger>());
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IRecruitmentNotificationService, LogOnlyRecruitmentNotificationService>();
        // D1 (BUG-292): the conversion now carries an offer's salary structure through the assignment rail.
        services.AddScoped<ISalaryAssignmentService, SalaryAssignmentService>();
        services.AddScoped<IApplicantConversionService, ApplicantConversionService>();

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ConvertApplicantToEmployeeCommand).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

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

    /// <summary>In-memory IFileStorage stub (the convert path never uploads, but EmployeeService takes it).</summary>
    private sealed class InMemoryFileStorage : IFileStorage
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public async Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, cancellationToken);
            _store[$"{tenantId}/{relativePath}"] = ms.ToArray();
            return $"/{tenantId}/{relativePath}";
        }

        public Task<Stream?> OpenReadAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream?>(
                _store.TryGetValue($"{tenantId}/{relativePath}", out var bytes) ? new MemoryStream(bytes) : null);

        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null)
            => $"/files/{tenantId}/{relativePath}";

        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
