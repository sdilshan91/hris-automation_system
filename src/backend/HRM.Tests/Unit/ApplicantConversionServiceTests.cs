// ============================================================================
// US-REC-010: Convert accepted applicant to employee — service unit tests.
//
// Drives ApplicantConversionService through the real EF Core InMemory provider
// (so applicant/offer/vacancy rows are persisted and the linkage/vacancy updates
// are observable), with a SUBSTITUTED IEmployeeService so the mapping into the
// Core HR create request can be asserted directly (the actual Core HR create is
// covered by its own tests + the conversion integration tests).
//
// Covers the data-mapping (FR-2 prefill, the create-request mapping) and the
// precondition logic (FR-1 Hired + accepted offer; FR-10/BR-2 duplicate).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class ApplicantConversionServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _deptId = Guid.NewGuid();
    private readonly Guid _jobTitleId = Guid.NewGuid();
    private readonly Guid _managerId = Guid.NewGuid();
    private readonly Guid _vacancyId = Guid.NewGuid();
    private readonly Guid _applicantId = Guid.NewGuid();

    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    public ApplicantConversionServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_userId);
        _currentUser.Email.Returns("hr@test.com");
        _currentUser.IsAuthenticated.Returns(true);

        Seed(ApplicantStage.Hired, withAcceptedOffer: true);
    }

    private AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, _tenantContext);

    private readonly List<CreateEmployeeRequest> _capturedCreates = new();

    private (ApplicantConversionService svc, IEmployeeService employees) CreateService(AppDbContext db, Guid? createdEmployeeId = null)
    {
        var employees = Substitute.For<IEmployeeService>();
        var empId = createdEmployeeId ?? Guid.NewGuid();
        // The service re-reads the created employee from the DB to patch extra fields, so the created
        // employee must actually exist. Persist a stub row keyed by the returned DTO id.
        employees.CreateAsync(Arg.Any<CreateEmployeeRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var req = ci.Arg<CreateEmployeeRequest>();
                _capturedCreates.Add(req);
                using var seedDb = CreateDb();
                seedDb.Employees.Add(new Employee
                {
                    Id = empId,
                    TenantId = _tenantId,
                    EmployeeNo = "EMP-0042",
                    FirstName = req.FirstName,
                    LastName = req.LastName,
                    Email = req.Email,
                    Phone = req.Phone,
                    DateOfJoining = req.DateOfJoining,
                    DepartmentId = req.DepartmentId,
                    JobTitleId = req.JobTitleId,
                    EmploymentType = req.EmploymentType,
                    IsActive = true,
                });
                seedDb.SaveChanges();
                return Result<EmployeeDto>.Success(new EmployeeDto { Id = empId, EmployeeNo = "EMP-0042" });
            });

        var svc = new ApplicantConversionService(
            db, _tenantContext, _currentUser, employees,
            new LogOnlyRecruitmentNotificationService(
                Substitute.For<ILogger<LogOnlyRecruitmentNotificationService>>()),
            Substitute.For<ILogger<ApplicantConversionService>>());

        return (svc, employees);
    }

    private void Seed(ApplicantStage stage, bool withAcceptedOffer)
    {
        using var db = CreateDb();
        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });
        db.Departments.Add(new Department { Id = _deptId, TenantId = _tenantId, Name = "Engineering", IsActive = true });
        db.JobTitles.Add(new JobTitle { Id = _jobTitleId, TenantId = _tenantId, TitleName = "Senior Backend Engineer", IsActive = true });
        db.Employees.Add(new Employee
        {
            Id = _managerId,
            TenantId = _tenantId,
            EmployeeNo = "EMP-0001",
            FirstName = "Grace",
            LastName = "Hopper",
            Email = "grace@acme.com",
            DateOfJoining = DateTime.UtcNow.AddYears(-2),
            DepartmentId = _deptId,
            JobTitleId = _jobTitleId,
            EmploymentType = EmploymentType.FullTime,
            IsActive = true,
        });
        db.Vacancies.Add(new Vacancy
        {
            Id = _vacancyId,
            TenantId = _tenantId,
            ReferenceNumber = "VAC-2026-0001",
            Title = "Backend Engineer",
            Status = VacancyStatus.Open,
            EmploymentType = EmploymentType.FullTime,
            Headcount = 2,
            DepartmentId = _deptId,
            JobTitleId = _jobTitleId,
            Description = "Build.",
        });
        db.Applicants.Add(new Applicant
        {
            Id = _applicantId,
            TenantId = _tenantId,
            VacancyId = _vacancyId,
            ApplicationReferenceNumber = "APP-2026-0001",
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada@acme.com",
            Phone = "+15551234567",
            ResumeStorageKey = "k",
            ResumeFileName = "r.pdf",
            Stage = stage,
            Source = ApplicationSource.Public,
            AppliedAt = DateTime.UtcNow,
        });
        if (withAcceptedOffer)
        {
            db.Offers.Add(new Offer
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                ApplicantId = _applicantId,
                VacancyId = _vacancyId,
                OfferReferenceNumber = "OFR-2026-0001",
                Status = OfferStatus.Accepted,
                Response = "Accepted",
                OfferedPosition = "Senior Backend Engineer",
                DepartmentId = _deptId,
                ReportingManagerEmployeeId = _managerId,
                SalaryAmount = 120000m,
                Currency = "USD",
                SalaryFrequency = SalaryFrequency.Annual,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                ProbationMonths = 3,
                Version = 1,
            });
        }
        db.SaveChanges();
    }

    private static ConvertApplicantToEmployeeInput Input(Guid applicantId, Guid jobTitleId, Guid deptId, Guid? managerId) => new()
    {
        ApplicantId = applicantId,
        JobTitleId = jobTitleId,
        DepartmentId = deptId,
        EmploymentType = EmploymentType.FullTime,
        DateOfJoining = DateTime.UtcNow.AddDays(30),
        ReportsToEmployeeId = managerId,
    };

    // ── Prefill maps applicant + offer (FR-2) ─────────────────────────

    [Fact]
    public async Task GetPrefill_MapsOfferAndApplicantData()
    {
        using var db = CreateDb();
        var (svc, _) = CreateService(db);

        var result = await svc.GetPrefillAsync(_applicantId);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.FirstName.Should().Be("Ada");
        dto.Email.Should().Be("ada@acme.com");
        dto.OfferedPosition.Should().Be("Senior Backend Engineer");
        dto.DepartmentId.Should().Be(_deptId);
        dto.ReportingManagerEmployeeId.Should().Be(_managerId);
        dto.SalaryAmount.Should().Be(120000m);
        dto.SalaryFrequency.Should().Be(SalaryFrequency.Annual);
        dto.JobTitleId.Should().Be(_jobTitleId);     // companion from the vacancy
        dto.AlreadyConverted.Should().BeFalse();
    }

    // ── Convert maps the Core HR create request (data mapping) ────────

    [Fact]
    public async Task Convert_MapsApplicantAndOffer_IntoCreateEmployeeRequest()
    {
        using var db = CreateDb();
        var (svc, _) = CreateService(db);

        var result = await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));

        result.IsSuccess.Should().BeTrue();
        var captured = _capturedCreates.Should().ContainSingle().Subject;
        captured!.FirstName.Should().Be("Ada");                 // applicant
        captured.LastName.Should().Be("Lovelace");
        captured.Email.Should().Be("ada@acme.com");
        captured.Phone.Should().Be("+15551234567");
        captured.DepartmentId.Should().Be(_deptId);             // form/offer
        captured.JobTitleId.Should().Be(_jobTitleId);
        captured.EmploymentType.Should().Be(EmploymentType.FullTime);
    }

    // ── Precondition: non-Hired rejected (FR-1) ───────────────────────

    [Fact]
    public async Task Convert_NonHired_FailsPrecondition()
    {
        using var setupDb = CreateDb();
        // Flip the seeded applicant to the Offer stage so eligibility fails on the stage check.
        var applicant = await setupDb.Applicants.FirstAsync(a => a.Id == _applicantId);
        applicant.Stage = ApplicantStage.Offer;
        await setupDb.SaveChangesAsync();

        using var db = CreateDb();
        var (svc, _) = CreateService(db);

        var result = await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("applicant_not_hired");
    }

    // ── Precondition: no accepted offer rejected (FR-1) ───────────────

    [Fact]
    public async Task Convert_NoAcceptedOffer_FailsPrecondition()
    {
        using var setupDb = CreateDb();
        // Remove the accepted offer so eligibility fails on the offer check.
        var offers = await setupDb.Offers.Where(o => o.ApplicantId == _applicantId).ToListAsync();
        setupDb.Offers.RemoveRange(offers);
        await setupDb.SaveChangesAsync();

        using var db = CreateDb();
        var (svc, _) = CreateService(db);

        var result = await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("no_accepted_offer");
    }

    // ── Duplicate conversion rejected (FR-10/BR-2) ────────────────────

    [Fact]
    public async Task Convert_AlreadyConverted_Rejected()
    {
        using var db = CreateDb();
        var (svc, _) = CreateService(db);

        var first = await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));
        first.IsSuccess.Should().BeTrue();

        using var db2 = CreateDb();
        var (svc2, _) = CreateService(db2);
        var second = await svc2.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));

        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("already_converted");
    }
}
