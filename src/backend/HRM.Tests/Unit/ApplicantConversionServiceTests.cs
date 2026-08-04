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

using System.Text.Json;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
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
        _tenantContext.Subdomain.Returns("acme"); // FR-9 builds the set-password link from this.

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_userId);
        _currentUser.Email.Returns("hr@test.com");
        _currentUser.IsAuthenticated.Returns(true);

        Seed(ApplicantStage.Hired, withAcceptedOffer: true);
    }

    private AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, _tenantContext);

    private readonly List<CreateEmployeeRequest> _capturedCreates = new();

    // D1: the salary-assignment spy is returned so arms can assert the offer's structure actually reached it.
    private (ApplicantConversionService svc, IEmployeeService employees, ISalaryAssignmentService salary) CreateService(
        AppDbContext db, Guid? createdEmployeeId = null,
        IOnboardingChecklistService? onboarding = null,
        INotificationDispatcher? dispatcher = null,
        IConfiguration? configuration = null)
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

        var salary = Substitute.For<ISalaryAssignmentService>();
        // Default to SUCCESS. An unconfigured NSubstitute call returns null, and the conversion path reads
        // salaryResult.IsFailure straight away — so leaving it unconfigured NREs inside the transaction and
        // makes every arm fail for a reason that has nothing to do with what it is testing.
        salary.AssignAsync(Arg.Any<AssignSalaryStructureInput>(), Arg.Any<CancellationToken>())
            .Returns(Result<CtcBreakdownDto>.Success(new CtcBreakdownDto()));
        var svc = new ApplicantConversionService(
            db, _tenantContext, _currentUser, employees,
            new LogOnlyRecruitmentNotificationService(
                Substitute.For<ILogger<LogOnlyRecruitmentNotificationService>>()),
            salary,
            Substitute.For<ILogger<ApplicantConversionService>>(),
            onboarding,
            dispatcher,
            configuration);

        return (svc, employees, salary);
    }

    private void Seed(ApplicantStage stage, bool withAcceptedOffer, Guid? offerSalaryStructureId = null)
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
                SalaryStructureId = offerSalaryStructureId,   // D1: null unless the arm supplies one
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
        var (svc, _, _) = CreateService(db);

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
        var (svc, _, _) = CreateService(db);

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
        var (svc, _, _) = CreateService(db);

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
        var (svc, _, _) = CreateService(db);

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
        var (svc, _, _) = CreateService(db);

        var first = await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));
        first.IsSuccess.Should().BeTrue();

        using var db2 = CreateDb();
        var (svc2, _, _) = CreateService(db2);
        var second = await svc2.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));

        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("already_converted");
    }

    // ── FR-5/BR-7 (ISSUE-140): auto-create login account gated on the tenant toggle ──

    [Fact]
    public async Task Convert_AutoCreateOn_ProvisionsUserMembershipAndRole_AndLinksEmployee()
    {
        EnableAutoCreateUserOnHire();
        var roleId = SeedEmployeeRole();

        var empId = Guid.NewGuid();
        using var db = CreateDb();
        var (svc, _, _) = CreateService(db, empId);

        var result = await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.UserAccountCreated.Should().BeTrue();

        using var assertDb = CreateDb();
        // A User row exists for the applicant email (lower-cased).
        var user = await assertDb.Users.IgnoreQueryFilters()
            .SingleOrDefaultAsync(u => u.Email == "ada@acme.com");
        user.Should().NotBeNull("the toggle is on, so a login account is provisioned");
        user!.PasswordHash.Should().BeNull(
            "the provisioned account is passwordless — FR-9 delivers a set-password link, never a password");
        user.IsActive.Should().BeTrue();

        // An ACTIVE UserTenant membership for this tenant.
        var membership = await assertDb.UserTenants.IgnoreQueryFilters()
            .SingleOrDefaultAsync(ut => ut.UserId == user.Id && ut.TenantId == _tenantId);
        membership.Should().NotBeNull();
        membership!.Status.Should().Be(UserTenantStatus.Active);

        // A UserTenantRole assigning the built-in Employee role.
        var hasRole = await assertDb.UserTenantRoles.IgnoreQueryFilters()
            .AnyAsync(x => x.UserTenantId == membership.Id && x.RoleId == roleId);
        hasRole.Should().BeTrue("the new account is granted the built-in Employee role");

        // Employee.UserId is linked to the provisioned user.
        var employee = await assertDb.Employees.IgnoreQueryFilters().SingleAsync(e => e.Id == empId);
        employee.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task Convert_AutoCreateOff_CreatesNoUser_AndFlagFalse()
    {
        // Default: the seeded tenant has AutoCreateUserOnHire = false. Preserves the prior behaviour.
        SeedEmployeeRole(); // role present, but the toggle is off so it must not be used.

        var empId = Guid.NewGuid();
        using var db = CreateDb();
        var (svc, _, _) = CreateService(db, empId);

        var result = await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.UserAccountCreated.Should().BeFalse();

        using var assertDb = CreateDb();
        (await assertDb.Users.IgnoreQueryFilters().CountAsync()).Should().Be(0, "toggle off — no account created");
        (await assertDb.UserTenants.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await assertDb.UserTenantRoles.IgnoreQueryFilters().CountAsync()).Should().Be(0);

        var employee = await assertDb.Employees.IgnoreQueryFilters().SingleAsync(e => e.Id == empId);
        employee.UserId.Should().BeNull("no account was provisioned, so nothing is linked");
    }

    [Fact]
    public async Task Convert_AutoCreateOn_ExistingGlobalUser_IsReusedNotDuplicated()
    {
        EnableAutoCreateUserOnHire();
        var roleId = SeedEmployeeRole();

        // A global user already exists with the applicant's email (e.g. they belong to another tenant).
        var existingUserId = Guid.NewGuid();
        using (var seedDb = CreateDb())
        {
            seedDb.Users.Add(new User
            {
                Id = existingUserId,
                Email = "ada@acme.com",
                DisplayName = "Existing Ada",
                PasswordHash = "existing-hash",
                IsActive = true,
            });
            seedDb.SaveChanges();
        }

        var empId = Guid.NewGuid();
        using var db = CreateDb();
        var (svc, _, _) = CreateService(db, empId);

        var result = await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.UserAccountCreated.Should().BeTrue();

        using var assertDb = CreateDb();
        // No duplicate: still exactly ONE user with that email, and it is the pre-existing one (hash untouched).
        var users = await assertDb.Users.IgnoreQueryFilters().Where(u => u.Email == "ada@acme.com").ToListAsync();
        users.Should().ContainSingle("the existing global account is reused, not duplicated");
        users[0].Id.Should().Be(existingUserId);
        users[0].PasswordHash.Should().Be("existing-hash", "the existing account must not be mutated");

        // The existing user is linked + granted membership/role in THIS tenant.
        var membership = await assertDb.UserTenants.IgnoreQueryFilters()
            .SingleOrDefaultAsync(ut => ut.UserId == existingUserId && ut.TenantId == _tenantId);
        membership.Should().NotBeNull();
        membership!.Status.Should().Be(UserTenantStatus.Active);
        (await assertDb.UserTenantRoles.IgnoreQueryFilters()
            .AnyAsync(x => x.UserTenantId == membership.Id && x.RoleId == roleId)).Should().BeTrue();

        var employee = await assertDb.Employees.IgnoreQueryFilters().SingleAsync(e => e.Id == empId);
        employee.UserId.Should().Be(existingUserId);
    }

    // Tenant isolation (Critical Rule #1): hiring a globally-known user in THIS tenant must not touch that
    // user's membership/roles in ANOTHER tenant, and must grant only THIS tenant's Employee role.
    [Fact]
    public async Task Convert_AutoCreateOn_DoesNotTouchAnotherTenantsMembershipOrRole()
    {
        EnableAutoCreateUserOnHire();
        var roleIdA = SeedEmployeeRole();

        // The applicant's email already belongs to a user who is an active member of a DIFFERENT tenant (B),
        // holding a B-scoped role. (UserTenant is not a BaseEntity, so its TenantId is not auto-stamped.)
        var otherTenantId = Guid.NewGuid();
        var existingUserId = Guid.NewGuid();
        var bMembershipId = Guid.NewGuid();
        var bRoleId = Guid.NewGuid();
        using (var seedDb = CreateDb())
        {
            seedDb.Users.Add(new User
            {
                Id = existingUserId, Email = "ada@acme.com", DisplayName = "Ada", PasswordHash = "b-hash", IsActive = true,
            });
            seedDb.UserTenants.Add(new UserTenant
            {
                Id = bMembershipId, UserId = existingUserId, TenantId = otherTenantId, Status = UserTenantStatus.Active,
            });
            seedDb.UserTenantRoles.Add(new UserTenantRole
            {
                UserTenantId = bMembershipId, RoleId = bRoleId, AssignedAt = DateTime.UtcNow,
            });
            seedDb.SaveChanges();
        }

        var empId = Guid.NewGuid();
        using var db = CreateDb();
        var (svc, _, _) = CreateService(db, empId);
        (await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId)))
            .IsSuccess.Should().BeTrue();

        using var assertDb = CreateDb();

        // Tenant B's membership + role are UNTOUCHED.
        var bMembership = await assertDb.UserTenants.IgnoreQueryFilters().SingleOrDefaultAsync(ut => ut.Id == bMembershipId);
        bMembership.Should().NotBeNull("hiring in tenant A must not remove tenant B's membership");
        bMembership!.TenantId.Should().Be(otherTenantId);
        (await assertDb.UserTenantRoles.IgnoreQueryFilters().CountAsync(x => x.UserTenantId == bMembershipId))
            .Should().Be(1, "tenant B's role assignment is untouched");

        // Tenant A got its OWN new membership for the reused global user, with only A's Employee role.
        var aMembership = await assertDb.UserTenants.IgnoreQueryFilters()
            .SingleOrDefaultAsync(ut => ut.UserId == existingUserId && ut.TenantId == _tenantId);
        aMembership.Should().NotBeNull();
        aMembership!.Id.Should().NotBe(bMembershipId);
        (await assertDb.UserTenantRoles.IgnoreQueryFilters()
            .AnyAsync(x => x.UserTenantId == aMembership.Id && x.RoleId == roleIdA)).Should().BeTrue();
        (await assertDb.UserTenantRoles.IgnoreQueryFilters()
            .AnyAsync(x => x.UserTenantId == aMembership.Id && x.RoleId == bRoleId))
            .Should().BeFalse("tenant A must never pick up tenant B's role");
    }

    private void EnableAutoCreateUserOnHire()
    {
        using var db = CreateDb();
        var tenant = db.Tenants.IgnoreQueryFilters().Single(t => t.Id == _tenantId);
        tenant.AutoCreateUserOnHire = true;
        db.SaveChanges();
    }

    private Guid SeedEmployeeRole()
    {
        using var db = CreateDb();
        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role
        {
            Id = roleId,
            TenantId = _tenantId,
            Name = PermissionCatalog.BuiltInRoles.Employee,
            IsBuiltIn = true,
        });
        db.SaveChanges();
        return roleId;
    }
    // ── D1 / BUG-292: the offer's salary structure must reach the new employee ──────────────
    // BUG-292 was that HR could type a salary at conversion and it silently vanished. D1's answer is that the
    // STRUCTURE is decided at offer time and conversion carries it through, so the money a candidate agreed to
    // and the money payroll pays derive from one approved record.
    //
    // These arms did not exist when the carry-through shipped — the implementing agent ran out of budget before
    // writing them, and the suite passed simply because the new code was untested. Mutating the carry-through
    // away killed nothing, which is how the gap was found.

    [Fact]
    public async Task Conversion_assigns_the_offers_salary_structure_to_the_new_employee_D1_BUG292()
    {
        // The ctor already seeded a Hired applicant with an accepted offer; attach a structure to THAT offer
        // rather than re-seeding (a second Seed() would duplicate keys in the shared InMemory store).
        var structureId = Guid.NewGuid();
        SetOfferSalaryStructure(structureId);
        using var db = CreateDb();
        var (svc, _, salary) = CreateService(db);

        var result = await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));

        result.IsSuccess.Should().BeTrue(result.Error);
        await salary.Received(1).AssignAsync(
            Arg.Is<AssignSalaryStructureInput>(i =>
                i.SalaryStructureId == structureId && i.AnnualCtc == 120000m),
            Arg.Any<CancellationToken>());
    }

    // The complement, and the one that stops an over-correction: a legacy/in-flight offer with NO structure
    // must still convert. Failing a hire because this feature is not configured would be worse than the bug.
    [Fact]
    public async Task Conversion_succeeds_and_assigns_nothing_when_the_offer_has_no_structure_D1()
    {
        SetOfferSalaryStructure(null);   // the seeded offer carries no structure — the legacy/in-flight case
        using var db = CreateDb();
        var (svc, _, salary) = CreateService(db);

        var result = await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));

        result.IsSuccess.Should().BeTrue(
            "a hire must never fail because the offer predates the salary-structure feature");
        await salary.DidNotReceive().AssignAsync(
            Arg.Any<AssignSalaryStructureInput>(), Arg.Any<CancellationToken>());
    }


    /// <summary>Points the seeded accepted offer at a salary structure (or clears it) — D1/BUG-292 arms.</summary>
    private void SetOfferSalaryStructure(Guid? structureId)
    {
        using var db = CreateDb();
        var offer = db.Offers.Single(o => o.ApplicantId == _applicantId);
        offer.SalaryStructureId = structureId;
        db.SaveChanges();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  FR-8 — onboarding checklist trigger on hire
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>An onboarding service whose applicable-template list is fixed per arm.</summary>
    private static IOnboardingChecklistService OnboardingReturning(params ApplicableTemplateDto[] templates)
    {
        var onboarding = Substitute.For<IOnboardingChecklistService>();
        onboarding.GetApplicableTemplatesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<ApplicableTemplateDto>>.Success(templates));
        onboarding.AssignAsync(Arg.Any<AssignChecklistInput>(), Arg.Any<CancellationToken>())
            .Returns(Result<OnboardingChecklistInstanceDto>.Success(new OnboardingChecklistInstanceDto()));
        return onboarding;
    }

    [Fact]
    public async Task Convert_AssignsTheApplicableOnboardingChecklist_ForTheNewEmployee_FR8()
    {
        var templateId = Guid.NewGuid();
        var onboarding = OnboardingReturning(new ApplicableTemplateDto
        {
            Id = templateId,
            TemplateName = "Engineering Onboarding",
            IsUniversal = false,
        });

        var empId = Guid.NewGuid();
        using var db = CreateDb();
        var (svc, _, _) = CreateService(db, empId, onboarding: onboarding);

        var result = await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));
        result.IsSuccess.Should().BeTrue(result.Error);

        await onboarding.Received(1).AssignAsync(
            Arg.Is<AssignChecklistInput>(i =>
                i.EmployeeId == empId &&
                i.TemplateId == templateId &&
                // Derived from the applicant, so a retried post-commit call returns the existing checklist
                // instead of assigning a second one.
                i.IdempotencyKey == "hire-conversion:" + _applicantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Convert_WithNoApplicableTemplate_AssignsNothing_AndStillSucceeds_FR8()
    {
        // "Trigger the onboarding workflow (IF CONFIGURED)" — a tenant that has configured no template is a
        // normal state, not an error. The conversion must not fail and no checklist may be invented.
        var onboarding = OnboardingReturning();

        using var db = CreateDb();
        var (svc, _, _) = CreateService(db, onboarding: onboarding);

        var result = await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));

        result.IsSuccess.Should().BeTrue(result.Error);
        await onboarding.DidNotReceive().AssignAsync(
            Arg.Any<AssignChecklistInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Convert_WithSeveralApplicableTemplates_PrefersTheMostSpecificOverTheUniversalOne_FR8()
    {
        var universalId = Guid.NewGuid();
        var specificId = Guid.NewGuid();
        // Deliberately ordered universal-first, and named so that a naive "first by name" pick would choose
        // the universal one — the arm fails if specificity is not actually what decides.
        var onboarding = OnboardingReturning(
            new ApplicableTemplateDto { Id = universalId, TemplateName = "All New Joiners", IsUniversal = true },
            new ApplicableTemplateDto { Id = specificId, TemplateName = "Engineering Onboarding", IsUniversal = false });

        using var db = CreateDb();
        var (svc, _, _) = CreateService(db, onboarding: onboarding);

        await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));

        await onboarding.Received(1).AssignAsync(
            Arg.Is<AssignChecklistInput>(i => i.TemplateId == specificId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Convert_WithSeveralNonUniversalTemplates_PicksTheFirstByName_FR8()
    {
        // Pins the tie-break that OrderBy(IsUniversal) alone does not decide. The service returns templates
        // already ordered by name and OrderBy is stable, so name order must survive — without that guarantee
        // which of two equally-specific templates a hire gets would be arbitrary and could change silently.
        var alpha = Guid.NewGuid();
        var beta = Guid.NewGuid();
        var onboarding = OnboardingReturning(
            new ApplicableTemplateDto { Id = alpha, TemplateName = "Alpha Onboarding", IsUniversal = false },
            new ApplicableTemplateDto { Id = beta, TemplateName = "Beta Onboarding", IsUniversal = false });

        using var db = CreateDb();
        var (svc, _, _) = CreateService(db, onboarding: onboarding);

        await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));

        await onboarding.Received(1).AssignAsync(
            Arg.Is<AssignChecklistInput>(i => i.TemplateId == alpha), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Convert_WhenTheChecklistAssignmentIsRejected_TheConversionStillSucceeds_FR8()
    {
        // AssignAsync can fail without throwing (e.g. BR-2 already has an active checklist). That branch is
        // logged, not raised — the committed hire must survive it.
        var onboarding = Substitute.For<IOnboardingChecklistService>();
        onboarding.GetApplicableTemplatesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<ApplicableTemplateDto>>.Success(new[]
            {
                new ApplicableTemplateDto { Id = Guid.NewGuid(), TemplateName = "Eng", IsUniversal = false },
            }));
        onboarding.AssignAsync(Arg.Any<AssignChecklistInput>(), Arg.Any<CancellationToken>())
            .Returns(Result<OnboardingChecklistInstanceDto>.Failure(
                "This employee already has an active checklist.", 409, "already_assigned"));

        var empId = Guid.NewGuid();
        using var db = CreateDb();
        var (svc, _, _) = CreateService(db, empId, onboarding: onboarding);

        var result = await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));

        result.IsSuccess.Should().BeTrue(result.Error);
        await onboarding.Received(1).AssignAsync(
            Arg.Any<AssignChecklistInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Convert_WhenTheOnboardingTriggerThrows_TheCommittedConversionStillSucceeds_FR8()
    {
        var onboarding = Substitute.For<IOnboardingChecklistService>();
        onboarding.GetApplicableTemplatesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<Result<IReadOnlyList<ApplicableTemplateDto>>>(_ => throw new InvalidOperationException("boom"));

        var empId = Guid.NewGuid();
        using var db = CreateDb();
        var (svc, _, _) = CreateService(db, empId, onboarding: onboarding);

        var result = await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));

        result.IsSuccess.Should().BeTrue(
            "the employee is already committed — a post-commit side effect must never fail the conversion");
        using var assertDb = CreateDb();
        (await assertDb.Employees.IgnoreQueryFilters().AnyAsync(e => e.Id == empId)).Should().BeTrue();

        // Without this the arm is falsely reassuring: if the onboarding seam were never invoked at all, the
        // stub would never throw, the conversion would succeed, and the test would pass while proving nothing
        // about the try/catch. Asserting the throwing collaborator WAS reached is what makes it a real guard.
        await onboarding.Received(1).GetApplicableTemplatesAsync(empId, Arg.Any<CancellationToken>());
    }

    // ══════════════════════════════════════════════════════════════════════
    //  FR-9 — welcome email
    // ══════════════════════════════════════════════════════════════════════

    private static (INotificationDispatcher dispatcher, List<NotificationRequest> sent) CapturingDispatcher()
    {
        var sent = new List<NotificationRequest>();
        var dispatcher = Substitute.For<INotificationDispatcher>();
        dispatcher.SendEmailAsync(Arg.Do<NotificationRequest>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return (dispatcher, sent);
    }

    private static IConfiguration BaseDomainConfig(string baseDomain = "yourhrm.com")
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Platform:BaseDomain"] = baseDomain })
            .Build();

    [Fact]
    public async Task Convert_WithProvisionedAccount_SendsWelcomeWithLoginAndSetPasswordLink_ButNoToken_FR9()
    {
        EnableAutoCreateUserOnHire();
        SeedEmployeeRole();
        var (dispatcher, sent) = CapturingDispatcher();

        using var db = CreateDb();
        var (svc, _, _) = CreateService(
            db, dispatcher: dispatcher, configuration: BaseDomainConfig("yourhrm.com"));

        var result = await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));
        result.IsSuccess.Should().BeTrue(result.Error);

        sent.Should().ContainSingle();
        var request = sent[0];
        request.EventKey.Should().Be("employee_welcome_credentials");
        request.RecipientEmail.Should().Be("ada@acme.com");
        request.TenantId.Should().Be(_tenantId);

        // The in-app leg needs a user id; FR-5 linked one, so it must be carried.
        request.RecipientUserId.Should().NotBeNull("an account was provisioned, so the in-app leg has a target");

        using var payload = JsonDocument.Parse(request.PayloadJson);
        payload.RootElement.GetProperty("login").GetProperty("email").GetString()
            .Should().Be("ada@acme.com");
        payload.RootElement.GetProperty("forgotPassword").GetProperty("url").GetString()
            .Should().Be("https://acme.yourhrm.com/forgot-password");

        // The whole point of the no-token decision: a reset token expires in an hour, and this email is
        // typically read days before the start date. A token in the payload would be expired on arrival.
        request.PayloadJson.Should().NotContainEquivalentOf("token");
    }

    [Fact]
    public async Task Convert_WithNoBaseDomainConfigured_StillBuildsAUsableSetPasswordLink_FR9()
    {
        // Platform:BaseDomain is absent in some hosts. The fallback must still produce a real absolute URL —
        // a welcome email whose only call to action is "https://acme./forgot-password" is a broken email.
        EnableAutoCreateUserOnHire();
        SeedEmployeeRole();
        var (dispatcher, sent) = CapturingDispatcher();

        using var db = CreateDb();
        var (svc, _, _) = CreateService(db, dispatcher: dispatcher, configuration: null);

        await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));

        using var payload = JsonDocument.Parse(sent.Single().PayloadJson);
        payload.RootElement.GetProperty("forgotPassword").GetProperty("url").GetString()
            .Should().Be("https://acme.yourhrm.com/forgot-password");
    }

    [Fact]
    public async Task Convert_NormalisesTheLoginEmailCasing_ButEmailsTheAddressAsGiven_FR9()
    {
        // Logins are matched lower-cased (FR-5 stores the account that way), so the credential line must show
        // the address the user will actually type. The delivery address itself is not case-normalised.
        EnableAutoCreateUserOnHire();
        SeedEmployeeRole();
        using (var seed = CreateDb())
        {
            var applicant = seed.Applicants.Single(a => a.Id == _applicantId);
            applicant.Email = "Ada@Acme.COM";
            seed.SaveChanges();
        }
        var (dispatcher, sent) = CapturingDispatcher();

        using var db = CreateDb();
        var (svc, _, _) = CreateService(db, dispatcher: dispatcher, configuration: BaseDomainConfig());

        await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));

        var request = sent.Single();
        using var payload = JsonDocument.Parse(request.PayloadJson);
        payload.RootElement.GetProperty("login").GetProperty("email").GetString()
            .Should().Be("ada@acme.com", "the account is keyed on the lower-cased address");
        request.RecipientEmail.Should().Be("Ada@Acme.COM");
    }

    [Fact]
    public async Task Convert_WithoutAProvisionedAccount_SendsThePlainWelcome_WithNoCredentialBlock_FR9()
    {
        // AutoCreateUserOnHire is off by default, so FR-9's "(if user account was created)" clause does not
        // apply — the hire is still welcomed, but a credential template would render an empty login block.
        var (dispatcher, sent) = CapturingDispatcher();

        using var db = CreateDb();
        var (svc, _, _) = CreateService(db, dispatcher: dispatcher, configuration: BaseDomainConfig());

        var result = await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));
        result.IsSuccess.Should().BeTrue(result.Error);

        sent.Should().ContainSingle();
        sent[0].EventKey.Should().Be("onboarding_welcome");
        sent[0].RecipientUserId.Should().BeNull("no account was provisioned, so there is no in-app recipient");

        using var payload = JsonDocument.Parse(sent[0].PayloadJson);
        payload.RootElement.TryGetProperty("login", out _).Should().BeFalse();
        payload.RootElement.TryGetProperty("forgotPassword", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Convert_WelcomeEmailCarriesTheEmployeeAndTenantDetailsTheTemplateRenders_FR9()
    {
        var (dispatcher, sent) = CapturingDispatcher();

        using var db = CreateDb();
        var (svc, _, _) = CreateService(db, dispatcher: dispatcher, configuration: BaseDomainConfig());

        await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));

        using var payload = JsonDocument.Parse(sent.Single().PayloadJson);
        var employee = payload.RootElement.GetProperty("employee");
        employee.GetProperty("firstName").GetString().Should().Be("Ada");
        // Resolved names, not raw ids — the template renders these verbatim, so ids would print as GUIDs.
        employee.GetProperty("department").GetString().Should().Be("Engineering");
        employee.GetProperty("jobTitle").GetString().Should().Be("Senior Backend Engineer");
        payload.RootElement.GetProperty("manager").GetProperty("name").GetString().Should().Be("Grace Hopper");
        payload.RootElement.GetProperty("tenant").GetProperty("companyName").GetString().Should().Be("Acme");
    }

    [Fact]
    public async Task Convert_WhenTheWelcomeEmailThrows_TheCommittedConversionStillSucceeds_FR9()
    {
        var dispatcher = Substitute.For<INotificationDispatcher>();
        dispatcher.SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("smtp down"));

        var empId = Guid.NewGuid();
        using var db = CreateDb();
        var (svc, _, _) = CreateService(db, empId, dispatcher: dispatcher, configuration: BaseDomainConfig());

        var result = await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));

        result.IsSuccess.Should().BeTrue("a mail failure must never fail a committed conversion");
        using var assertDb = CreateDb();
        (await assertDb.Employees.IgnoreQueryFilters().AnyAsync(e => e.Id == empId)).Should().BeTrue();

        // Same reasoning as the FR-8 arm: prove the throwing dispatcher was actually reached, so this cannot
        // pass vacuously on a build where the welcome email is never sent at all.
        await dispatcher.Received(1).SendEmailAsync(
            Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Convert_AFailingOnboardingTrigger_DoesNotSuppressTheWelcomeEmail_FR8_FR9()
    {
        // The two legs have separate try/catch blocks on purpose. A single shared catch would let the first
        // failure silently swallow the second leg — which is exactly how a "notifications are best-effort"
        // seam quietly stops notifying.
        var onboarding = Substitute.For<IOnboardingChecklistService>();
        onboarding.GetApplicableTemplatesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<Result<IReadOnlyList<ApplicableTemplateDto>>>(_ => throw new InvalidOperationException("boom"));
        var (dispatcher, sent) = CapturingDispatcher();

        using var db = CreateDb();
        var (svc, _, _) = CreateService(
            db, onboarding: onboarding, dispatcher: dispatcher, configuration: BaseDomainConfig());

        await svc.ConvertAsync(Input(_applicantId, _jobTitleId, _deptId, _managerId));

        sent.Should().ContainSingle("the welcome email must still go out when the onboarding leg fails");
    }
}
