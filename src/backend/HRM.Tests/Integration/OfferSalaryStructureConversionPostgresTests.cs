// ============================================================================
// Decision D1 (closes BUG-292) — the salary STRUCTURE is decided at OFFER time, and
// applicant→employee conversion carries it through to a real salary assignment.
//
// Runs on a Testcontainers Postgres (NOT InMemory) deliberately: this is a money path with
// a NEW nullable FK column and TRANSACTIONAL carry-through — exactly the class InMemory masks
// (no real transaction, no numeric(18,2) round-trip). The conversion wraps its atomic unit in a
// Postgres transaction under the Npgsql retrying execution strategy, and the salary assignment
// (US-PAY-002 rail) runs INSIDE that same unit — so a green run proves the employee + salary
// commit together, and a forced failure in the salary step leaves NO employee behind.
//
// Arms:
//   A. Offer WITH a structure ⇒ the converted employee HAS the salary assigned, offer amount as CTC
//      (the BUG-292 regression — fails if the carry-through is dropped).
//   B. Offer WITHOUT a structure (legacy/in-flight) ⇒ conversion still succeeds, no salary, no throw.
//   C. A cross-tenant structure is REJECTED on the offer write (hard tenant-isolation rule).
//   D. A forced failure in the salary-assignment step rolls the whole conversion back — no employee row.
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
/// D1 / BUG-292: proves offer-time salary structure selection flows through the applicant→employee
/// conversion into a real, committed salary assignment on Postgres — and that the whole conversion is
/// atomic when the salary step fails, and cross-tenant structures are rejected on the offer write.
/// </summary>
public sealed class OfferSalaryStructureConversionPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deptId = Guid.NewGuid();
    private readonly Guid _jobTitleId = Guid.NewGuid();
    private readonly Guid _managerId = Guid.NewGuid();
    private readonly Guid _vacancyId = Guid.NewGuid();

    // Structures.
    private readonly Guid _structureMatchesId = Guid.NewGuid();   // tenant A, BASIC 100% of gross → sum == CTC.
    private readonly Guid _structureMismatchId = Guid.NewGuid();  // tenant A, Fixed 50k → sum != 120k (forces failure).
    private readonly Guid _structureTenantBId = Guid.NewGuid();   // tenant B — must be rejected on tenant A's write.

    private const decimal Ctc = 120_000m;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var tenantContext = new MutableTenantContext { TenantId = _tenantId };
        await using var db = NewContext(tenantContext);
        await db.Database.MigrateAsync();

        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme Corp" });
        db.Tenants.Add(new Tenant { Id = _tenantB, Subdomain = "globex", Name = "Globex" });
        db.Departments.Add(new Department { Id = _deptId, TenantId = _tenantId, Name = "Engineering", Code = "ENG", IsActive = true });
        db.JobTitles.Add(new JobTitle { Id = _jobTitleId, TenantId = _tenantId, TitleName = "Senior Backend Engineer", IsActive = true });
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
            Headcount = 10, FilledCount = 0, Description = "Build things.", IsDeleted = false,
        });

        // Structures. Explicit TenantId per entity — the interceptor only stamps when TenantId is empty, so
        // the tenant-B structure below is genuinely owned by tenant B even though this context is tenant A.
        SeedStructure(db, _tenantId, _structureMatchesId, "FTA", CalculationMethod.PercentageOfGross, 100m);
        SeedStructure(db, _tenantId, _structureMismatchId, "FIX", CalculationMethod.Fixed, 50_000m);
        SeedStructure(db, _tenantB, _structureTenantBId, "FTB", CalculationMethod.PercentageOfGross, 100m);

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ── A. Carry-through: offer WITH a structure ⇒ employee gets the salary (BUG-292 regression) ──

    [Fact]
    [Trait("TC", "TC-REC-010")]
    public async Task Convert_OfferWithStructure_AssignsSalaryWithOfferAmountAsCtc()
    {
        var applicantId = await SeedApplicantWithAcceptedOffer("ada@acme.com", "Ada", _structureMatchesId);
        var mediator = BuildPipeline();

        var result = await mediator.Send(new ConvertApplicantToEmployeeCommand(
            ApplicantId: applicantId, JobTitleId: _jobTitleId, DepartmentId: _deptId,
            EmploymentType: EmploymentType.FullTime, DateOfJoining: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ReportsToEmployeeId: _managerId, LocationId: null, EmployeeNo: null, DateOfBirth: null, Gender: null));

        result.IsSuccess.Should().BeTrue(result.Error);
        var employeeId = result.Value!.EmployeeId;

        await using var verify = NewContext(new MutableTenantContext { TenantId = _tenantId });

        // Salary components were written for the NEW employee, effective on the joining date, from the offer's
        // structure — and the earnings sum to the offer's amount (CTC).
        var rows = await verify.EmployeeSalaryComponents.AsNoTracking()
            .Where(r => r.EmployeeId == employeeId).ToListAsync();
        rows.Should().NotBeEmpty("the accepted offer carried a salary structure (BUG-292 carry-through)");
        rows.Should().OnlyContain(r => r.SalaryStructureId == _structureMatchesId);
        rows.Should().OnlyContain(r => r.EffectiveFrom == new DateOnly(2026, 3, 1));
        rows.Sum(r => r.AnnualAmount).Should().Be(Ctc);

        // A revision-history row records the assignment at the offer amount.
        var revision = await verify.SalaryRevisionHistories.AsNoTracking()
            .SingleAsync(r => r.EmployeeId == employeeId);
        revision.NewStructureId.Should().Be(_structureMatchesId);
        revision.NewAnnualCtc.Should().Be(Ctc);
    }

    // ── B. No structure (legacy/in-flight) ⇒ conversion still succeeds, no salary ──

    [Fact]
    [Trait("TC", "TC-REC-010")]
    public async Task Convert_OfferWithoutStructure_SucceedsWithNoSalaryAssigned()
    {
        var applicantId = await SeedApplicantWithAcceptedOffer("legacy@acme.com", "Leg", salaryStructureId: null);
        var mediator = BuildPipeline();

        var result = await mediator.Send(new ConvertApplicantToEmployeeCommand(
            ApplicantId: applicantId, JobTitleId: _jobTitleId, DepartmentId: _deptId,
            EmploymentType: EmploymentType.FullTime, DateOfJoining: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ReportsToEmployeeId: _managerId, LocationId: null, EmployeeNo: null, DateOfBirth: null, Gender: null));

        result.IsSuccess.Should().BeTrue(result.Error);
        var employeeId = result.Value!.EmployeeId;

        await using var verify = NewContext(new MutableTenantContext { TenantId = _tenantId });
        var rows = await verify.EmployeeSalaryComponents.AsNoTracking()
            .Where(r => r.EmployeeId == employeeId).ToListAsync();
        rows.Should().BeEmpty("a structure-less offer must not fail or assign any salary (D1 legacy path)");
    }

    // ── C. Cross-tenant structure is rejected on the offer write ──

    [Fact]
    [Trait("TC", "TC-REC-010")]
    public async Task GenerateOffer_WithCrossTenantStructure_IsRejected()
    {
        var applicantId = await SeedBareApplicant("write@acme.com", "Wri");
        var mediator = BuildPipeline();

        var result = await mediator.Send(new GenerateOfferCommand(
            ApplicantId: applicantId, OfferedPosition: "Senior Backend Engineer", DepartmentId: _deptId,
            ReportingManagerEmployeeId: _managerId, SalaryAmount: Ctc, Currency: "USD",
            SalaryFrequency: SalaryFrequency.Annual, BenefitsSummary: null,
            StartDate: new DateOnly(2026, 3, 1), ExpiryDate: null, ProbationMonths: null, CustomClauses: null,
            SalaryStructureId: _structureTenantBId)); // structure owned by tenant B — must not resolve here.

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("salary_structure_not_found");
    }

    // ── C+. Positive write: an in-tenant structure persists on the generated offer ──

    [Fact]
    [Trait("TC", "TC-REC-010")]
    public async Task GenerateOffer_WithInTenantStructure_PersistsSalaryStructureId()
    {
        var applicantId = await SeedBareApplicant("ok@acme.com", "Okk");
        var mediator = BuildPipeline();

        var result = await mediator.Send(new GenerateOfferCommand(
            ApplicantId: applicantId, OfferedPosition: "Senior Backend Engineer", DepartmentId: _deptId,
            ReportingManagerEmployeeId: _managerId, SalaryAmount: Ctc, Currency: "USD",
            SalaryFrequency: SalaryFrequency.Annual, BenefitsSummary: null,
            StartDate: new DateOnly(2026, 3, 1), ExpiryDate: null, ProbationMonths: null, CustomClauses: null,
            SalaryStructureId: _structureMatchesId));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.SalaryStructureId.Should().Be(_structureMatchesId);

        await using var verify = NewContext(new MutableTenantContext { TenantId = _tenantId });
        var offer = await verify.Offers.AsNoTracking().FirstAsync(o => o.Id == result.Value!.Id);
        offer.SalaryStructureId.Should().Be(_structureMatchesId);
    }

    // ── D. Atomicity: a failing salary step leaves NO employee behind ──

    [Fact]
    [Trait("TC", "TC-REC-010")]
    public async Task Convert_SalaryAssignmentFails_RollsBackWholeConversion_NoEmployee()
    {
        // The mismatch structure's earnings (Fixed 50k) do not equal the offer amount (120k) and there are no
        // overrides ⇒ AssignAsync fails FR-6 (ctc_sum_mismatch) — a real failure in the salary step.
        var applicantId = await SeedApplicantWithAcceptedOffer("fail@acme.com", "Fai", _structureMismatchId);
        var mediator = BuildPipeline();

        var result = await mediator.Send(new ConvertApplicantToEmployeeCommand(
            ApplicantId: applicantId, JobTitleId: _jobTitleId, DepartmentId: _deptId,
            EmploymentType: EmploymentType.FullTime, DateOfJoining: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ReportsToEmployeeId: _managerId, LocationId: null, EmployeeNo: null, DateOfBirth: null, Gender: null));

        result.IsFailure.Should().BeTrue("the salary step failed, so the atomic conversion must roll back");
        result.ErrorCode.Should().Be("ctc_sum_mismatch");

        await using var verify = NewContext(new MutableTenantContext { TenantId = _tenantId });

        // No employee was committed for this hire (rolled back with the failed salary step).
        var employeeExists = await verify.Employees.AsNoTracking().AnyAsync(e => e.Email == "fail@acme.com");
        employeeExists.Should().BeFalse("a half-created employee with no salary must never be committed (NFR-3)");

        // The applicant was not linked, and no orphan salary rows exist.
        var applicant = await verify.Applicants.AsNoTracking().FirstAsync(a => a.Id == applicantId);
        applicant.ConvertedToEmployeeId.Should().BeNull();
        (await verify.EmployeeSalaryComponents.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    // ── Seeding helpers ─────────────────────────────────────────────────────────────────

    private static void SeedStructure(
        AppDbContext db, Guid tenantId, Guid structureId, string code, CalculationMethod method, decimal value)
    {
        var componentId = Guid.NewGuid();
        db.SalaryComponents.Add(new SalaryComponent
        {
            Id = componentId, TenantId = tenantId, Name = "Basic " + code, Code = code + "-BASIC",
            Type = SalaryComponentType.Earning, CalculationMethod = method, DefaultValue = value,
            IsActive = true, ProcessingOrder = 1,
        });
        db.SalaryStructures.Add(new SalaryStructure
        {
            Id = structureId, TenantId = tenantId, Name = "Structure " + code, Code = code,
            EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true,
        });
        db.SalaryStructureComponents.Add(new SalaryStructureComponent
        {
            Id = Guid.NewGuid(), TenantId = tenantId, SalaryStructureId = structureId,
            SalaryComponentId = componentId, ProcessingOrder = 1, IsMandatory = false,
        });
    }

    private async Task<Guid> SeedApplicantWithAcceptedOffer(string email, string first, Guid? salaryStructureId)
    {
        var applicantId = Guid.NewGuid();
        await using var db = NewContext(new MutableTenantContext { TenantId = _tenantId });
        db.Applicants.Add(new Applicant
        {
            Id = applicantId, TenantId = _tenantId, VacancyId = _vacancyId,
            ApplicationReferenceNumber = "APP-" + first, FirstName = first, LastName = "Test",
            Email = email, Phone = "+15551234567",
            ResumeStorageKey = $"recruitment/{_vacancyId}/x/z.pdf", ResumeFileName = "resume.pdf",
            Stage = ApplicantStage.Hired, Source = ApplicationSource.Public,
            AppliedAt = DateTime.UtcNow, IsDeleted = false,
        });
        db.Offers.Add(new Offer
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, ApplicantId = applicantId, VacancyId = _vacancyId,
            OfferReferenceNumber = "OFR-" + first, Status = OfferStatus.Accepted, Response = "Accepted",
            OfferedPosition = "Senior Backend Engineer", DepartmentId = _deptId,
            ReportingManagerEmployeeId = _managerId, SalaryAmount = Ctc, Currency = "USD",
            SalaryFrequency = SalaryFrequency.Annual, SalaryStructureId = salaryStructureId,
            StartDate = new DateOnly(2026, 3, 1), ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            Version = 1, RespondedAt = DateTime.UtcNow, IsDeleted = false,
        });
        await db.SaveChangesAsync();
        return applicantId;
    }

    private async Task<Guid> SeedBareApplicant(string email, string first)
    {
        var applicantId = Guid.NewGuid();
        await using var db = NewContext(new MutableTenantContext { TenantId = _tenantId });
        db.Applicants.Add(new Applicant
        {
            Id = applicantId, TenantId = _tenantId, VacancyId = _vacancyId,
            ApplicationReferenceNumber = "APP-" + first, FirstName = first, LastName = "Test",
            Email = email, Phone = "+15551234567",
            ResumeStorageKey = $"recruitment/{_vacancyId}/x/z.pdf", ResumeFileName = "resume.pdf",
            Stage = ApplicantStage.Applied, Source = ApplicationSource.Public,
            AppliedAt = DateTime.UtcNow, IsDeleted = false,
        });
        await db.SaveChangesAsync();
        return applicantId;
    }

    // ── Composition ───────────────────────────────────────────────────────────────────

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
        services.AddSingleton<IHtmlSanitizer, GanssHtmlSanitizer>();

        var customFields = Substitute.For<ICustomFieldService>();
        customFields.ValidateCustomFieldValuesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        services.AddSingleton(customFields);

        services.AddScoped<IPayrollAuditLogger, PayrollAuditLogger>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IRecruitmentNotificationService, LogOnlyRecruitmentNotificationService>();
        services.AddScoped<ISalaryComponentService, SalaryComponentService>();
        services.AddScoped<ISalaryStructureService, SalaryStructureService>();
        services.AddScoped<ISalaryAssignmentService, SalaryAssignmentService>();
        services.AddScoped<IOfferService, OfferService>();
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
