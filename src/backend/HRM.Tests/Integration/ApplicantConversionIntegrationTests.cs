// ============================================================================
// US-REC-010: Convert accepted applicant to employee — integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI
// container (MediatR pipeline + global query filters), covering:
//   - Convert a Hired + Accepted applicant -> Employee created with mapped fields (AC-1/AC-2)
//   - Applicant linked: ConvertedToEmployeeId/At/By set (AC-4/FR-6)
//   - Vacancy filledCount incremented + auto-close when full (FR-7/BR-5)
//   - Duplicate conversion rejected 409 (FR-10/BR-2)
//   - Convert non-Hired / no-accepted-offer rejected 409 (FR-1)
//   - Cross-tenant isolation (AC-5)
//   - Pre-fill maps applicant + offer data (AC-1/FR-2)
//
// PROVIDER: EF Core InMemory through the real composed pipeline (no PostgreSQL,
// no Docker in the verify gate) — same approach as OfferIntegrationTests. The
// conversion's BeginTransaction is guarded behind Database.IsRelational(), so on
// InMemory it runs a single SaveChanges (the PG schema + transaction path are
// validated by the migrations CI job applying the migration to real PG).
//
// EmployeeService (reused for the actual employee create) needs IVirusScanner /
// ICustomFieldService — substituted, since the convert path uses neither (no
// photo upload, no custom fields). IFileStorage is the in-memory stub.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.Commands;
using HRM.Application.Features.Recruitment.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class ApplicantConversionIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    private readonly Guid _deptA = Guid.NewGuid();
    private readonly Guid _jobTitleA = Guid.NewGuid();
    private readonly Guid _managerA = Guid.NewGuid();

    private Guid _vacancyA;          // headcount 2 (not auto-closed on first hire)
    private Guid _vacancyFull;       // headcount 1 (auto-closes on hire)
    private Guid _vacancyB;          // tenant B
    private Guid _hiredApplicantA;   // Hired + accepted offer (vacancyA)
    private Guid _hiredApplicantFull;// Hired + accepted offer (vacancyFull)
    private Guid _offerApplicantA;   // Offer stage, no accepted offer
    private Guid _hiredNoOfferA;     // Hired but no accepted offer
    private Guid _hiredApplicantB;   // tenant B Hired + accepted

    public ApplicantConversionIntegrationTests()
    {
        SeedData();
    }

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

    private IMediator BuildPipeline(Guid tenantId, Guid userId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.Email.Returns("hr@test.com");
        currentUser.TenantId.Returns(tenantId);
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Roles.Returns(new List<string>());
        currentUser.Permissions.Returns(new List<string>());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));

        services.AddSingleton<IFileStorage, InMemoryFileStorage>();
        services.AddSingleton(Substitute.For<IVirusScanner>());

        // Custom-field validation is never invoked on the convert path (no custom fields supplied);
        // substitute it so the EmployeeService dependency graph is satisfiable.
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

    private void SeedData()
    {
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        _vacancyA = Guid.NewGuid();
        _vacancyFull = Guid.NewGuid();
        _vacancyB = Guid.NewGuid();
        _hiredApplicantA = Guid.NewGuid();
        _hiredApplicantFull = Guid.NewGuid();
        _offerApplicantA = Guid.NewGuid();
        _hiredNoOfferA = Guid.NewGuid();
        _hiredApplicantB = Guid.NewGuid();

        db.Tenants.AddRange(
            new Tenant { Id = _tenantA, Subdomain = "acme", Name = "Acme Corp" },
            new Tenant { Id = _tenantB, Subdomain = "globex", Name = "Globex" });

        // Core HR master data for tenant A (EmployeeService validates department + job title exist/active).
        db.Departments.Add(new Department { Id = _deptA, TenantId = _tenantA, Name = "Engineering", IsActive = true });
        db.JobTitles.Add(new JobTitle { Id = _jobTitleA, TenantId = _tenantA, TitleName = "Senior Backend Engineer", IsActive = true });
        db.Employees.Add(new Employee
        {
            Id = _managerA,
            TenantId = _tenantA,
            EmployeeNo = "EMP-0001",
            FirstName = "Grace",
            LastName = "Hopper",
            Email = "grace@acme.com",
            DateOfJoining = DateTime.UtcNow.AddYears(-2),
            DepartmentId = _deptA,
            JobTitleId = _jobTitleA,
            EmploymentType = EmploymentType.FullTime,
            IsActive = true,
        });

        db.Vacancies.AddRange(
            Vacancy(_vacancyA, _tenantA, "Backend Engineer (A)", headcount: 2),
            Vacancy(_vacancyFull, _tenantA, "Sole Engineer", headcount: 1),
            Vacancy(_vacancyB, _tenantB, "Backend Engineer (B)", headcount: 1));

        db.Applicants.AddRange(
            Applicant(_hiredApplicantA, _tenantA, _vacancyA, "ada@a.com", "Ada", "Lovelace", ApplicantStage.Hired),
            Applicant(_hiredApplicantFull, _tenantA, _vacancyFull, "alan@a.com", "Alan", "Turing", ApplicantStage.Hired),
            Applicant(_offerApplicantA, _tenantA, _vacancyA, "edsger@a.com", "Edsger", "Dijkstra", ApplicantStage.Offer),
            Applicant(_hiredNoOfferA, _tenantA, _vacancyA, "donald@a.com", "Donald", "Knuth", ApplicantStage.Hired),
            Applicant(_hiredApplicantB, _tenantB, _vacancyB, "eve@b.com", "Eve", "Best", ApplicantStage.Hired));

        // Accepted offers for the convertible applicants (A, Full, B). No accepted offer for hiredNoOffer.
        db.Offers.AddRange(
            AcceptedOffer(_tenantA, _hiredApplicantA, _vacancyA, _deptA, _managerA),
            AcceptedOffer(_tenantA, _hiredApplicantFull, _vacancyFull, _deptA, _managerA),
            AcceptedOffer(_tenantB, _hiredApplicantB, _vacancyB, null, null));

        db.SaveChanges();
    }

    private static Vacancy Vacancy(Guid id, Guid tenantId, string title, int headcount) => new()
    {
        Id = id,
        TenantId = tenantId,
        ReferenceNumber = $"VAC-2026-{Math.Abs(id.GetHashCode()) % 10000:D4}",
        Title = title,
        Status = VacancyStatus.Open,
        EmploymentType = EmploymentType.FullTime,
        Headcount = headcount,
        FilledCount = 0,
        Description = "Build things.",
        IsDeleted = false,
    };

    private static Applicant Applicant(Guid id, Guid tenantId, Guid vacancyId, string email, string first, string last, ApplicantStage stage) => new()
    {
        Id = id,
        TenantId = tenantId,
        VacancyId = vacancyId,
        ApplicationReferenceNumber = $"APP-2026-{Math.Abs(email.GetHashCode()) % 10000:D4}",
        FirstName = first,
        LastName = last,
        Email = email,
        Phone = "+15551234567",
        ResumeStorageKey = $"recruitment/{vacancyId}/x/z.pdf",
        ResumeFileName = "resume.pdf",
        Stage = stage,
        Source = ApplicationSource.Public,
        AppliedAt = DateTime.UtcNow,
        IsDeleted = false,
    };

    private static Offer AcceptedOffer(Guid tenantId, Guid applicantId, Guid vacancyId, Guid? deptId, Guid? managerId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        ApplicantId = applicantId,
        VacancyId = vacancyId,
        OfferReferenceNumber = $"OFR-2026-{Math.Abs(applicantId.GetHashCode()) % 10000:D4}",
        Status = OfferStatus.Accepted,
        Response = "Accepted",
        OfferedPosition = "Senior Backend Engineer",
        DepartmentId = deptId,
        ReportingManagerEmployeeId = managerId,
        SalaryAmount = 120000m,
        Currency = "USD",
        SalaryFrequency = SalaryFrequency.Annual,
        StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
        ProbationMonths = 3,
        Version = 1,
        RespondedAt = DateTime.UtcNow,
        IsDeleted = false,
    };

    private AppDbContext ReadDb(Guid tenantId) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options,
        new MutableTenantContext { TenantId = tenantId });

    private ConvertApplicantToEmployeeCommand Convert(Guid applicantId, string? employeeNo = null) => new(
        applicantId, _jobTitleA, _deptA, EmploymentType.FullTime,
        DateOfJoining: DateTime.UtcNow.AddDays(30), ReportsToEmployeeId: _managerA,
        LocationId: null, EmployeeNo: employeeNo, DateOfBirth: null, Gender: null);

    // ── Pre-fill maps applicant + offer (AC-1/FR-2) ───────────────────

    [Fact]
    public async Task GetPrefill_MapsApplicantAndOfferData()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(new GetConversionPrefillQuery(_hiredApplicantA));

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.FirstName.Should().Be("Ada");
        dto.LastName.Should().Be("Lovelace");
        dto.Email.Should().Be("ada@a.com");
        dto.Phone.Should().Be("+15551234567");
        dto.OfferedPosition.Should().Be("Senior Backend Engineer");
        dto.DepartmentId.Should().Be(_deptA);
        dto.ReportingManagerEmployeeId.Should().Be(_managerA);
        dto.SalaryAmount.Should().Be(120000m);
        dto.ProbationMonths.Should().Be(3);
        dto.AlreadyConverted.Should().BeFalse();
    }

    // ── Convert -> employee created with mapped fields (AC-1/AC-2) ─────

    [Fact]
    public async Task Convert_HiredAcceptedApplicant_CreatesEmployeeWithMappedFields()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(Convert(_hiredApplicantA));

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.EmployeeId.Should().NotBeEmpty();
        dto.EmployeeNo.Should().NotBeNullOrWhiteSpace();

        using var db = ReadDb(_tenantA);
        var emp = await db.Employees.FirstAsync(e => e.Id == dto.EmployeeId);
        emp.FirstName.Should().Be("Ada");
        emp.LastName.Should().Be("Lovelace");
        emp.Email.Should().Be("ada@a.com");
        emp.Phone.Should().Be("+15551234567");
        emp.DepartmentId.Should().Be(_deptA);
        emp.JobTitleId.Should().Be(_jobTitleA);
        emp.ReportsToEmployeeId.Should().Be(_managerA);
        emp.EmploymentType.Should().Be(EmploymentType.FullTime);
    }

    [Fact]
    public async Task Convert_WithManualEmployeeNo_UsesOverride()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(Convert(_hiredApplicantA, employeeNo: "EMP-9999"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.EmployeeNo.Should().Be("EMP-9999");
    }

    // ── Applicant linked (AC-4/FR-6) ──────────────────────────────────

    [Fact]
    public async Task Convert_LinksApplicantToEmployee()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(Convert(_hiredApplicantA));

        using var db = ReadDb(_tenantA);
        var applicant = await db.Applicants.FirstAsync(a => a.Id == _hiredApplicantA);
        applicant.ConvertedToEmployeeId.Should().Be(result.Value!.EmployeeId);
        applicant.ConvertedAt.Should().NotBeNull();
        applicant.ConvertedByUserId.Should().Be(_userA);
        // BR-6: applicant is not deleted.
        applicant.IsDeleted.Should().BeFalse();
        applicant.Stage.Should().Be(ApplicantStage.Hired);
    }

    // ── Vacancy filledCount incremented; not closed when room remains (FR-7) ──

    [Fact]
    public async Task Convert_IncrementsVacancyFilledCount_WithoutClosingWhenRoomRemains()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(Convert(_hiredApplicantA));

        result.Value!.VacancyFilledCount.Should().Be(1);
        result.Value.VacancyHeadcount.Should().Be(2);
        result.Value.VacancyClosed.Should().BeFalse();

        using var db = ReadDb(_tenantA);
        var vacancy = await db.Vacancies.FirstAsync(v => v.Id == _vacancyA);
        vacancy.FilledCount.Should().Be(1);
        vacancy.Status.Should().Be(VacancyStatus.Open);
    }

    // ── Vacancy auto-closes when full (FR-7/BR-5) ─────────────────────

    [Fact]
    public async Task Convert_AutoClosesVacancy_WhenHeadcountReached()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(Convert(_hiredApplicantFull));

        result.Value!.VacancyClosed.Should().BeTrue();
        result.Value.VacancyFilledCount.Should().Be(1);
        result.Value.VacancyHeadcount.Should().Be(1);

        using var db = ReadDb(_tenantA);
        var vacancy = await db.Vacancies.FirstAsync(v => v.Id == _vacancyFull);
        vacancy.Status.Should().Be(VacancyStatus.Closed);
        vacancy.ClosedAt.Should().NotBeNull();
    }

    // ── Duplicate conversion rejected (FR-10/BR-2) ────────────────────

    [Fact]
    public async Task Convert_AlreadyConverted_IsRejected409()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        (await mediator.Send(Convert(_hiredApplicantA))).IsSuccess.Should().BeTrue();

        var second = await mediator.Send(Convert(_hiredApplicantA));

        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("already_converted");
    }

    // ── Non-Hired applicant rejected (FR-1) ───────────────────────────

    [Fact]
    public async Task Convert_NonHiredApplicant_IsRejected409()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(Convert(_offerApplicantA));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("applicant_not_hired");
    }

    // ── Hired but no accepted offer rejected (FR-1) ───────────────────

    [Fact]
    public async Task Convert_HiredWithoutAcceptedOffer_IsRejected409()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(Convert(_hiredNoOfferA));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("no_accepted_offer");
    }

    // ── Cross-tenant isolation (AC-5) ─────────────────────────────────

    [Fact]
    public async Task Convert_CrossTenantApplicant_Returns404()
    {
        // Tenant B tries to convert Tenant A's applicant — filtered out by the query filter → 404.
        var medB = BuildPipeline(_tenantB, _userB);

        var result = await medB.Send(Convert(_hiredApplicantA));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("applicant_not_found");
    }

    [Fact]
    public async Task GetPrefill_CrossTenantApplicant_Returns404()
    {
        var medB = BuildPipeline(_tenantB, _userB);

        var result = await medB.Send(new GetConversionPrefillQuery(_hiredApplicantA));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
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
