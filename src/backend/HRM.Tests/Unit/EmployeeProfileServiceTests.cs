// ============================================================================
// US-CHR-002: View and Edit Employee Profile — Unit Tests
// Tests: view profile (AC-1), edit-as-HR with audit (AC-2), concurrency
// conflict 409 (AC-3), edit-as-employee restricted field 403 (AC-4, AC-5),
// tenant isolation 404, employment history entries (AC-6, BR-4), and
// emergency contacts management.
// Uses EF Core InMemory provider for lightweight database testing.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Employees.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class EmployeeProfileServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _fileStorage;
    private readonly IVirusScanner _virusScanner;
    private readonly ICustomFieldService _customFieldService;
    private readonly ILogger<EmployeeService> _logger;

    public EmployeeProfileServiceTests()
    {
        _dbName = Guid.NewGuid().ToString();
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("hr@test.com");
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.IsAuthenticated.Returns(true);
        // Default: HR Officer with Employee.Edit permission
        _currentUser.Permissions.Returns(new List<string> { "Employee.Edit", "Employee.View.All" });
        _currentUser.Roles.Returns(new List<string> { "HR Officer" });

        _fileStorage = Substitute.For<IFileStorage>();
        _virusScanner = Substitute.For<IVirusScanner>();
        _virusScanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(VirusScanResult.Clean());

        _customFieldService = Substitute.For<ICustomFieldService>();
        _customFieldService.ValidateCustomFieldValuesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Application.Common.Models.Result.Success());

        _logger = Substitute.For<ILogger<EmployeeService>>();
    }

    private EmployeeService CreateService(ICurrentUser? currentUser = null)
    {
        var dbContext = TestDbContextFactory.Create(_tenantContext, _dbName);
        return new EmployeeService(dbContext, _tenantContext, currentUser ?? _currentUser, _fileStorage, _virusScanner, _customFieldService, _logger);
    }

    private HRM.Infrastructure.Persistence.AppDbContext CreateDbContext()
    {
        return TestDbContextFactory.Create(_tenantContext, _dbName);
    }

    private async Task<Guid> SeedDepartment(string name = "Engineering", string code = "ENG", Guid? tenantId = null)
    {
        var tid = tenantId ?? _tenantId;
        ITenantContext ctx;
        if (tenantId.HasValue && tenantId.Value != _tenantId)
        {
            ctx = Substitute.For<ITenantContext>();
            ctx.TenantId.Returns(tid);
            ctx.IsResolved.Returns(true);
        }
        else
        {
            ctx = _tenantContext;
        }

        using var db = TestDbContextFactory.Create(ctx, _dbName);
        var dept = new Department
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tid,
            Name = name,
            Code = code,
            IsActive = true,
            IsDeleted = false,
        };
        db.Departments.Add(dept);
        await db.SaveChangesAsync();
        return dept.Id;
    }

    private async Task<Guid> SeedJobTitle(string titleName = "Software Engineer", Guid? tenantId = null)
    {
        var tid = tenantId ?? _tenantId;
        ITenantContext ctx;
        if (tenantId.HasValue && tenantId.Value != _tenantId)
        {
            ctx = Substitute.For<ITenantContext>();
            ctx.TenantId.Returns(tid);
            ctx.IsResolved.Returns(true);
        }
        else
        {
            ctx = _tenantContext;
        }

        using var db = TestDbContextFactory.Create(ctx, _dbName);
        var jt = new JobTitle
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tid,
            TitleName = titleName,
            IsActive = true,
            IsDeleted = false,
        };
        db.JobTitles.Add(jt);
        await db.SaveChangesAsync();
        return jt.Id;
    }

    private async Task SeedTenant(Guid tenantId, int? maxEmployees = null)
    {
        using var db = CreateDbContext();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Subdomain = $"tenant-{tenantId.ToString()[..8]}",
            Name = $"Test Tenant {tenantId.ToString()[..8]}",
            Status = TenantStatus.Active,
            MaxEmployees = maxEmployees,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedEmployee(Guid deptId, Guid jtId, string email = "john@test.com")
    {
        await SeedTenant(_tenantId);
        var service = CreateService();
        var result = await service.CreateAsync(new CreateEmployeeRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = email,
            Phone = "+1234567890",
            DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender = Gender.Male,
            DateOfJoining = DateTime.UtcNow.Date,
            DepartmentId = deptId,
            JobTitleId = jtId,
            EmploymentType = EmploymentType.FullTime,
        });
        result.IsSuccess.Should().BeTrue();
        return result.Value!.Id;
    }

    // ── AC-1: View comprehensive profile ──────────────────────────

    [Fact]
    public async Task GetProfile_ShouldReturnComprehensiveProfile()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        var service = CreateService();
        var result = await service.GetProfileAsync(empId);

        result.IsSuccess.Should().BeTrue();
        var profile = result.Value!;
        profile.Id.Should().Be(empId);
        profile.EmployeeNo.Should().StartWith("EMP-");
        profile.FirstName.Should().Be("John");
        profile.LastName.Should().Be("Doe");
        profile.Email.Should().Be("john@test.com");
        profile.DepartmentName.Should().Be("Engineering");
        profile.JobTitleName.Should().Be("Software Engineer");
        profile.Status.Should().Be("Active");
        profile.EmergencyContacts.Should().BeEmpty();
        profile.EmploymentHistory.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProfile_NonExistent_ShouldReturn404()
    {
        var service = CreateService();
        var result = await service.GetProfileAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetProfile_TenantNotResolved_ShouldFail()
    {
        _tenantContext.IsResolved.Returns(false);
        var service = CreateService();
        var result = await service.GetProfileAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Tenant context is not resolved");

        // Restore for other tests
        _tenantContext.IsResolved.Returns(true);
    }

    // ── AC-2: Edit as HR with audit snapshot ──────────────────────

    [Fact]
    public async Task UpdateProfile_PersonalInfo_ShouldSucceedAndWriteAudit()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        var service = CreateService();
        var result = await service.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0, // InMemory doesn't enforce xmin, so 0 works
            PersonalInfo = new PersonalInfoUpdate
            {
                FirstName = "Jane",
                LastName = "Smith",
            },
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.FirstName.Should().Be("Jane");
        result.Value.LastName.Should().Be("Smith");

        // Verify audit log was written
        using var db = CreateDbContext();
        var auditLogs = await db.EmployeeFieldAuditLogs
            .Where(a => a.EmployeeId == empId && a.Section == "PersonalInfo")
            .ToListAsync();

        auditLogs.Should().HaveCount(1);
        auditLogs[0].BeforeSnapshot.Should().Contain("John");
        auditLogs[0].AfterSnapshot.Should().Contain("Jane");
        auditLogs[0].ChangedBy.Should().Be("hr@test.com");
    }

    [Fact]
    public async Task UpdateProfile_ContactInfo_ShouldSucceedAndWriteAudit()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        var service = CreateService();
        var result = await service.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            ContactInfo = new ContactInfoUpdate
            {
                Phone = "+9876543210",
                PersonalEmail = "john.personal@test.com",
                Address = "123 Main St",
            },
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Phone.Should().Be("+9876543210");
        result.Value.PersonalEmail.Should().Be("john.personal@test.com");
        result.Value.Address.Should().Be("123 Main St");

        // Verify audit log
        using var db = CreateDbContext();
        var auditLogs = await db.EmployeeFieldAuditLogs
            .Where(a => a.EmployeeId == empId && a.Section == "ContactInfo")
            .ToListAsync();

        auditLogs.Should().HaveCount(1);
    }

    // ── AC-4 / AC-5: Employee role restricted fields 403 ──────────

    [Fact]
    public async Task UpdateProfile_AsEmployee_PersonalInfo_ShouldReturn403()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        // Setup Employee role
        var employeeUser = Substitute.For<ICurrentUser>();
        employeeUser.Email.Returns("john@test.com");
        employeeUser.UserId.Returns(Guid.NewGuid());
        employeeUser.IsAuthenticated.Returns(true);
        employeeUser.Permissions.Returns(new List<string> { "Employee.Edit.Own", "Employee.View.Own" });
        employeeUser.Roles.Returns(new List<string> { "Employee" });

        var service = CreateService(employeeUser);
        var result = await service.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            PersonalInfo = new PersonalInfoUpdate { FirstName = "Hacked" },
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("Employees cannot modify personal info fields");
    }

    [Fact]
    public async Task UpdateProfile_AsEmployee_EmploymentInfo_ShouldReturn403()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        var employeeUser = Substitute.For<ICurrentUser>();
        employeeUser.Email.Returns("john@test.com");
        employeeUser.UserId.Returns(Guid.NewGuid());
        employeeUser.IsAuthenticated.Returns(true);
        employeeUser.Permissions.Returns(new List<string> { "Employee.Edit.Own", "Employee.View.Own" });
        employeeUser.Roles.Returns(new List<string> { "Employee" });

        var service = CreateService(employeeUser);
        var result = await service.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            EmploymentInfo = new EmploymentInfoUpdate { Status = EmployeeStatus.Terminated },
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("Employees cannot modify employment fields");
    }

    [Fact]
    public async Task UpdateProfile_AsEmployee_ContactInfo_ShouldSucceed()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        var employeeUser = Substitute.For<ICurrentUser>();
        employeeUser.Email.Returns("john@test.com");
        employeeUser.UserId.Returns(Guid.NewGuid());
        employeeUser.IsAuthenticated.Returns(true);
        employeeUser.Permissions.Returns(new List<string> { "Employee.Edit.Own", "Employee.View.Own" });
        employeeUser.Roles.Returns(new List<string> { "Employee" });

        var service = CreateService(employeeUser);
        var result = await service.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            ContactInfo = new ContactInfoUpdate { Phone = "+5555555555" },
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Phone.Should().Be("+5555555555");
    }

    [Fact]
    public async Task UpdateProfile_AsEmployee_EmergencyContacts_ShouldSucceed()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        var employeeUser = Substitute.For<ICurrentUser>();
        employeeUser.Email.Returns("john@test.com");
        employeeUser.UserId.Returns(Guid.NewGuid());
        employeeUser.IsAuthenticated.Returns(true);
        employeeUser.Permissions.Returns(new List<string> { "Employee.Edit.Own", "Employee.View.Own" });
        employeeUser.Roles.Returns(new List<string> { "Employee" });

        var service = CreateService(employeeUser);
        var result = await service.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            EmergencyContacts = new List<EmergencyContactInput>
            {
                new()
                {
                    ContactName = "Jane Doe",
                    Relationship = "Spouse",
                    Phone = "+1111111111",
                    IsPrimary = true,
                },
            },
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.EmergencyContacts.Should().HaveCount(1);
        result.Value.EmergencyContacts[0].ContactName.Should().Be("Jane Doe");
    }

    [Fact]
    public async Task UpdateProfile_AsEmployee_CustomFields_ShouldReturn403()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        var employeeUser = Substitute.For<ICurrentUser>();
        employeeUser.Email.Returns("john@test.com");
        employeeUser.UserId.Returns(Guid.NewGuid());
        employeeUser.IsAuthenticated.Returns(true);
        employeeUser.Permissions.Returns(new List<string> { "Employee.Edit.Own", "Employee.View.Own" });
        employeeUser.Roles.Returns(new List<string> { "Employee" });

        var service = CreateService(employeeUser);
        var result = await service.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            UpdateCustomFields = true,
            CustomFields = """{"hacked": true}""",
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("Employees cannot modify custom fields");
    }

    // ── Manager read-only (FR-3) ──────────────────────────────────

    [Fact]
    public async Task UpdateProfile_AsManager_ShouldReturn403()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        var managerUser = Substitute.For<ICurrentUser>();
        managerUser.Email.Returns("manager@test.com");
        managerUser.UserId.Returns(Guid.NewGuid());
        managerUser.IsAuthenticated.Returns(true);
        managerUser.Permissions.Returns(new List<string> { "Employee.View.Team" });
        managerUser.Roles.Returns(new List<string> { "Manager" });

        var service = CreateService(managerUser);
        var result = await service.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            ContactInfo = new ContactInfoUpdate { Phone = "+9999999999" },
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("read-only access");
    }

    // ── AC-3: Concurrency conflict 409 ────────────────────────────

    [Fact]
    public async Task UpdateProfile_ConcurrencyConflict_ShouldReturn409()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        // First update (succeeds)
        var service1 = CreateService();
        var result1 = await service1.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            PersonalInfo = new PersonalInfoUpdate { FirstName = "Alice" },
        });
        result1.IsSuccess.Should().BeTrue();

        // Second update with stale RowVersion (should fail with 409)
        // Since InMemory doesn't enforce xmin, we simulate by manually
        // modifying the employee's RowVersion to create a mismatch.
        using (var db = CreateDbContext())
        {
            var emp = await db.Employees.FirstAsync(e => e.Id == empId);
            emp.RowVersion = 999; // Simulate a different xmin value
            await db.SaveChangesAsync();
        }

        var service2 = CreateService();
        var result2 = await service2.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0, // Stale value
            PersonalInfo = new PersonalInfoUpdate { FirstName = "Bob" },
        });

        result2.IsFailure.Should().BeTrue();
        result2.StatusCode.Should().Be(409);
        result2.Error.Should().Contain("modified by another user");
    }

    // ── AC-6: Employment history entries ──────────────────────────

    [Fact]
    public async Task UpdateProfile_DepartmentChange_ShouldCreateHistoryEntry()
    {
        var deptId1 = await SeedDepartment("Engineering", "ENG");
        var deptId2 = await SeedDepartment("Marketing", "MKT");
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId1, jtId);

        var service = CreateService();
        var result = await service.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            EmploymentInfo = new EmploymentInfoUpdate
            {
                DepartmentId = deptId2,
                EffectiveDate = DateTime.UtcNow.Date,
                Reason = "Team restructuring",
            },
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.DepartmentName.Should().Be("Marketing");

        // Verify employment history
        result.Value.EmploymentHistory.Should().HaveCount(1);
        var entry = result.Value.EmploymentHistory[0];
        entry.ChangeType.Should().Be("Department");
        entry.PreviousValue.Should().Be("Engineering");
        entry.NewValue.Should().Be("Marketing");
        entry.Reason.Should().Be("Team restructuring");
    }

    [Fact]
    public async Task UpdateProfile_JobTitleChange_ShouldCreateHistoryEntry()
    {
        var deptId = await SeedDepartment();
        var jtId1 = await SeedJobTitle("Software Engineer");
        var jtId2 = await SeedJobTitle("Senior Software Engineer");
        var empId = await SeedEmployee(deptId, jtId1);

        var service = CreateService();
        var result = await service.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            EmploymentInfo = new EmploymentInfoUpdate
            {
                JobTitleId = jtId2,
                EffectiveDate = DateTime.UtcNow.Date,
                Reason = "Promotion",
            },
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.JobTitleName.Should().Be("Senior Software Engineer");

        result.Value.EmploymentHistory.Should().HaveCount(1);
        var entry = result.Value.EmploymentHistory[0];
        entry.ChangeType.Should().Be("JobTitle");
        entry.PreviousValue.Should().Be("Software Engineer");
        entry.NewValue.Should().Be("Senior Software Engineer");
    }

    [Fact]
    public async Task UpdateProfile_StatusChange_ShouldCreateHistoryEntry()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        var service = CreateService();
        var result = await service.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            EmploymentInfo = new EmploymentInfoUpdate
            {
                Status = EmployeeStatus.Terminated,
            },
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Terminated");
        result.Value.IsActive.Should().BeFalse();

        result.Value.EmploymentHistory.Should().HaveCount(1);
        var entry = result.Value.EmploymentHistory[0];
        entry.ChangeType.Should().Be("Status");
        entry.PreviousValue.Should().Be("Active");
        entry.NewValue.Should().Be("Terminated");
    }

    [Fact]
    public async Task UpdateProfile_TwoDepartmentChanges_ShouldCreateTwoHistoryEntries()
    {
        var deptId1 = await SeedDepartment("Engineering", "ENG");
        var deptId2 = await SeedDepartment("Marketing", "MKT");
        var deptId3 = await SeedDepartment("Sales", "SLS");
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId1, jtId);

        // First change
        var service1 = CreateService();
        var result1 = await service1.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            EmploymentInfo = new EmploymentInfoUpdate { DepartmentId = deptId2 },
        });
        result1.IsSuccess.Should().BeTrue();

        // Second change
        var service2 = CreateService();
        var result2 = await service2.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            EmploymentInfo = new EmploymentInfoUpdate { DepartmentId = deptId3 },
        });
        result2.IsSuccess.Should().BeTrue();

        // Verify two employment history entries
        result2.Value!.EmploymentHistory.Should().HaveCount(2);
        result2.Value.EmploymentHistory[0].NewValue.Should().Be("Sales");
        result2.Value.EmploymentHistory[1].NewValue.Should().Be("Marketing");
    }

    // ── Tenant isolation ──────────────────────────────────────────

    [Fact]
    public async Task GetProfile_CrossTenant_ShouldReturn404()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Seed in tenant A
        var ctxA = Substitute.For<ITenantContext>();
        ctxA.TenantId.Returns(tenantA);
        ctxA.IsResolved.Returns(true);
        var deptA = await SeedDepartment("Eng A", "ENGA", tenantA);
        var jtA = await SeedJobTitle("Eng A", tenantA);
        await SeedTenant(tenantA);

        var dbA = TestDbContextFactory.Create(ctxA, _dbName);
        var serviceA = new EmployeeService(dbA, ctxA, _currentUser, _fileStorage, _virusScanner, _customFieldService, _logger);
        var createResult = await serviceA.CreateAsync(new CreateEmployeeRequest
        {
            FirstName = "Secret", LastName = "Employee", Email = "secret@test.com",
            DateOfJoining = DateTime.UtcNow.Date,
            DepartmentId = deptA, JobTitleId = jtA, EmploymentType = EmploymentType.FullTime,
        });
        createResult.IsSuccess.Should().BeTrue();

        // Try to get profile from tenant B
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);
        var serviceB = new EmployeeService(
            TestDbContextFactory.Create(ctxB, _dbName), ctxB, _currentUser, _fileStorage, _virusScanner, _customFieldService, _logger);

        var result = await serviceB.GetProfileAsync(createResult.Value!.Id);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── Emergency contacts management ─────────────────────────────

    [Fact]
    public async Task UpdateProfile_EmergencyContacts_ShouldReplaceAll()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        // Add initial emergency contacts
        var service1 = CreateService();
        var result1 = await service1.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            EmergencyContacts = new List<EmergencyContactInput>
            {
                new() { ContactName = "Contact A", Relationship = "Spouse", Phone = "+111", IsPrimary = true },
                new() { ContactName = "Contact B", Relationship = "Parent", Phone = "+222" },
            },
        });
        result1.IsSuccess.Should().BeTrue();
        result1.Value!.EmergencyContacts.Should().HaveCount(2);

        // Replace with a single contact
        var service2 = CreateService();
        var result2 = await service2.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            EmergencyContacts = new List<EmergencyContactInput>
            {
                new() { ContactName = "Contact C", Relationship = "Sibling", Phone = "+333", IsPrimary = true },
            },
        });
        result2.IsSuccess.Should().BeTrue();
        result2.Value!.EmergencyContacts.Should().HaveCount(1);
        result2.Value.EmergencyContacts[0].ContactName.Should().Be("Contact C");
    }

    // ── Audit log verification ────────────────────────────────────

    [Fact]
    public async Task UpdateProfile_MultipleSection_ShouldWriteMultipleAuditEntries()
    {
        var deptId1 = await SeedDepartment("Engineering", "ENG");
        var deptId2 = await SeedDepartment("Marketing", "MKT");
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId1, jtId);

        var service = CreateService();
        var result = await service.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            PersonalInfo = new PersonalInfoUpdate { FirstName = "Jane" },
            ContactInfo = new ContactInfoUpdate { Phone = "+999" },
            EmploymentInfo = new EmploymentInfoUpdate { DepartmentId = deptId2 },
        });

        result.IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        var auditLogs = await db.EmployeeFieldAuditLogs
            .Where(a => a.EmployeeId == empId)
            .ToListAsync();

        // PersonalInfo + ContactInfo + EmploymentInfo = 3 audit entries
        auditLogs.Should().HaveCount(3);
        auditLogs.Select(a => a.Section).Should().Contain("PersonalInfo");
        auditLogs.Select(a => a.Section).Should().Contain("ContactInfo");
        auditLogs.Select(a => a.Section).Should().Contain("EmploymentInfo");
    }

    // ── Update non-existent employee ──────────────────────────────

    [Fact]
    public async Task UpdateProfile_NonExistent_ShouldReturn404()
    {
        var service = CreateService();
        var result = await service.UpdateProfileAsync(Guid.NewGuid(), new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            PersonalInfo = new PersonalInfoUpdate { FirstName = "Ghost" },
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── Update with invalid department/job title ──────────────────

    [Fact]
    public async Task UpdateProfile_InvalidDepartment_ShouldReturn400()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        var service = CreateService();
        var result = await service.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            EmploymentInfo = new EmploymentInfoUpdate { DepartmentId = Guid.NewGuid() },
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("Department not found");
    }

    [Fact]
    public async Task UpdateProfile_InvalidJobTitle_ShouldReturn400()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        var service = CreateService();
        var result = await service.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            EmploymentInfo = new EmploymentInfoUpdate { JobTitleId = Guid.NewGuid() },
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("Job title not found");
    }

    // ── No change should not write audit ──────────────────────────

    [Fact]
    public async Task UpdateProfile_NoActualChange_ShouldSucceedWithNoAudit()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        var service = CreateService();
        // Set FirstName to the same value
        var result = await service.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            PersonalInfo = new PersonalInfoUpdate { FirstName = "John" }, // same as current
        });

        result.IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        var auditLogs = await db.EmployeeFieldAuditLogs
            .Where(a => a.EmployeeId == empId)
            .ToListAsync();

        auditLogs.Should().BeEmpty();
    }

    public void Dispose()
    {
        // InMemory databases are cleaned up when the last connection closes
    }
}
