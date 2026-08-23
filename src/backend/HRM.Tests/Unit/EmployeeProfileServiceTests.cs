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
using HRM.Application.Features.AuditLog.DTOs;
using HRM.Domain.Enums;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly IPayrollAuditLogger _auditLogger;
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

        _auditLogger = Substitute.For<IPayrollAuditLogger>();

        _logger = Substitute.For<ILogger<EmployeeService>>();
    }

    private EmployeeService CreateService(ICurrentUser? currentUser = null)
    {
        var dbContext = TestDbContextFactory.Create(_tenantContext, _dbName);
        return new EmployeeService(dbContext, _tenantContext, currentUser ?? _currentUser, _fileStorage, _virusScanner, _customFieldService, _auditLogger, _logger);
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

    /// <summary>
    /// Links a seeded employee record to a user id so a self-service Employee-role caller with the
    /// matching UserId is recognised as the record's OWNER (BUG-119 ownership check). Employees are
    /// created without a linked UserId by default, so this establishes the self-edit scenario.
    /// </summary>
    private async Task LinkEmployeeToUser(Guid employeeId, Guid userId)
    {
        using var db = CreateDbContext();
        var emp = await db.Employees.FirstAsync(e => e.Id == employeeId);
        emp.UserId = userId;
        await db.SaveChangesAsync();
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

    // ── ISSUE-225: profile DTO exposes the reporting manager (TC-CHR-269) ─────────

    [Fact]
    public async Task GetProfile_ExposesReportingManager_Issue225()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId);

        Guid managerId, reportId;
        using (var db = CreateDbContext())
        {
            var manager = new Employee
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeNo = "EMP-MGR",
                FirstName = "Mona", LastName = "Manager", Email = "boss@test.com",
                DateOfJoining = DateTime.UtcNow.AddDays(-60), DepartmentId = deptId, JobTitleId = jtId,
                EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
            };
            managerId = manager.Id;

            var report = new Employee
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeNo = "EMP-RPT",
                FirstName = "Riley", LastName = "Report", Email = "report@test.com",
                DateOfJoining = DateTime.UtcNow.AddDays(-30), DepartmentId = deptId, JobTitleId = jtId,
                EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
                ReportsToEmployeeId = managerId,
            };
            reportId = report.Id;

            db.Employees.AddRange(manager, report);
            await db.SaveChangesAsync();
        }

        var result = await CreateService().GetProfileAsync(reportId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ReportsToEmployeeId.Should().Be(managerId);
        result.Value.ManagerName.Should().Be("Mona Manager");
    }

    [Fact]
    public async Task GetProfile_ManagerFieldsNull_WhenNoManager_Issue225()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        var result = await CreateService().GetProfileAsync(empId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ReportsToEmployeeId.Should().BeNull();
        result.Value.ManagerName.Should().BeNull();
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

    /// <summary>
    /// C3/GAP-025: editing an employee must reach the CENTRAL audit trail, not only the forensic table.
    ///
    /// <para>
    /// <c>Employee</c> is <c>IAuditExempt</c>, so <c>AuditCaptureInterceptor</c> skips it and the per-section
    /// <c>employee_field_audit_logs</c> rows were the only record — a table the US-NTF-005 audit viewer
    /// cannot read. The effect was that editing an employee left nothing visible to compliance, while merely
    /// VIEWING that same profile logged <c>Employee.ProfileViewed</c>.
    /// </para>
    ///
    /// <para>
    /// Only the section NAMES go on the central row. The values stay in the forensic table by design — those
    /// snapshots carry masked PII that must not surface in a viewer everyone with audit access can read.
    /// See docs/vault/decisions/2026-08-23-employee-field-audit-is-forensic.md.
    /// </para>
    /// </summary>
    [Fact]
    public async Task UpdateProfile_writes_one_central_audit_row_beside_the_forensic_rows_c3()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        // TWO sections on purpose. With one, "one row per EDIT" and "one row per SECTION" are
        // indistinguishable — moving the AuditLogs.Add inside the per-section loop would keep a
        // single-section arm green. Two sections also exercise the `.Order()` join in Detail, which a
        // single key never does.
        var result = await CreateService().UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            PersonalInfo = new PersonalInfoUpdate { FirstName = "Jane", LastName = "Smith" },
            ContactInfo = new ContactInfoUpdate { Phone = "+94 77 000 1111" },
        });
        result.IsSuccess.Should().BeTrue(result.Error);

        using var db = CreateDbContext();

        // Read THROUGH the query filter, not around it. IgnoreQueryFilters() waives the very property this
        // arm is about — whether the row is reachable by a tenant-scoped reader.
        var central = await db.AuditLogs
            .Where(a => a.ResourceId == empId.ToString() && a.EventType == "Employee.ProfileUpdated")
            .ToListAsync();

        central.Should().ContainSingle(
            "one central row per edit — not one per section, which would flood the viewer");
        central[0].ResourceType.Should().Be("Employee");
        central[0].TenantId.Should().Be(_tenantId,
            "audit_logs has NO global filter scoping reads — AuditLogService scopes explicitly with "
            + "TenantId == tenantId, so a null-tenant row is invisible to the viewer this fix exists to feed");
        central[0].Detail.Should().Contain("PersonalInfo");
        central[0].Detail.Should().Contain("ContactInfo",
            "the central row names WHICH sections changed so an auditor knows where to look");

        // ...and it deliberately carries NO values — neither the new one nor, more importantly, the OLD one.
        // The before-snapshot is what the forensic table holds and what the masked-PII argument is about.
        central[0].Detail.Should().NotContain("Jane");
        central[0].Detail.Should().NotContain("John",
            "the BEFORE value is the one the ADR keeps out of the general viewer");

        // The forensic rows are still written alongside — the two records serve different readers.
        (await db.EmployeeFieldAuditLogs.CountAsync(a => a.EmployeeId == empId))
            .Should().Be(2, "one forensic row per changed section");
    }

    /// <summary>
    /// THE ARM C3'S THESIS ACTUALLY RESTS ON: the row is reachable by the US-NTF-005 VIEWER, not merely
    /// present in the table.
    ///
    /// <para>
    /// Every other arm reads <c>db.AuditLogs</c> directly. That is one predicate short of the claim:
    /// <c>AuditLogService.BuildFilteredQuery</c> scopes reads with an EXPLICIT
    /// <c>Where(a =&gt; a.TenantId == tenantId)</c> — <c>audit_logs</c> has no read-scoping global filter — so a
    /// row written with a null or wrong tenant sits in the table, satisfies every direct-read arm, and is
    /// invisible to compliance. If someone later adds an action allow-list to that query, this is the arm
    /// that reddens; the others would all stay green while the feature silently reverted to GAP-025.
    /// </para>
    /// </summary>
    [Fact]
    public async Task UpdateProfile_audit_row_is_returned_by_the_audit_viewer_c3()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        (await CreateService().UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            PersonalInfo = new PersonalInfoUpdate { FirstName = "Jane" },
        })).IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        var viewer = new AuditLogService(
            db, _tenantContext, _currentUser, NullLogger<AuditLogService>.Instance);

        var page = await viewer.ListAsync(
            new AuditLogFilter(null, null, null, null, null, null), page: 1, pageSize: 50);

        page.IsSuccess.Should().BeTrue(page.Error);
        page.Value!.Items.Should().Contain(
            i => i.Action == "Employee.ProfileUpdated" && i.ResourceId == empId.ToString(),
            "the audit VIEWER must return the row — being in the table is not the same as being visible, "
            + "and the difference is exactly the tenant predicate this fix has to get right");
    }

    /// <summary>
    /// A save that changes NOTHING must not fabricate an audit entry. The impl guards this with
    /// <c>if (beforeSnapshots.Count &gt; 0)</c>; hoisting the central write out of that block — a plausible
    /// refactor — would record an edit that never happened. For a compliance trail a FALSE entry is worse
    /// than a missing one, and nothing else catches it.
    /// </summary>
    [Fact]
    public async Task UpdateProfile_with_no_actual_changes_writes_no_central_audit_row_c3()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        var result = await CreateService().UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
        });
        result.IsSuccess.Should().BeTrue(result.Error);

        using var db = CreateDbContext();
        (await db.AuditLogs.CountAsync(a => a.ResourceId == empId.ToString()
                && a.EventType == "Employee.ProfileUpdated"))
            .Should().Be(0, "a no-op save must not manufacture a record of an edit");
    }

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

    // ── ISSUE-293: updating the National ID persists the new value AND audits it MASKED (never raw PII) ──
    [Fact]
    public async Task UpdateProfile_NationalId_PersistsValue_AndAuditsMaskedNotRaw_ISSUE293()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        var result = await CreateService().UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            PersonalInfo = new PersonalInfoUpdate { NationalId = "SL-777-6789" },
        });
        result.IsSuccess.Should().BeTrue(result.Error);

        using var db = CreateDbContext();
        // The new value is persisted (decrypts back on read).
        var emp = await db.Employees.IgnoreQueryFilters().SingleAsync(e => e.Id == empId);
        emp.NationalId.Should().Be("SL-777-6789");

        // The field-audit snapshot records the MASKED value only — the raw National ID must never appear.
        var audit = await db.EmployeeFieldAuditLogs
            .SingleAsync(a => a.EmployeeId == empId && a.Section == "PersonalInfo");
        audit.AfterSnapshot.Should().Contain("6789").And.NotContain("SL-777-6789",
            "the audit trail masks the National ID, it does not store the raw PII value");
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

        // BUG-119 (#124) restricts a self-service Employee-role caller to editing ONLY their own
        // record (employee.UserId must equal the caller's UserId) — closing a horizontal privilege
        // escalation. This test predates that fix and used an unrelated random UserId, so the caller
        // was correctly rejected with 403. Link the seeded employee to the caller so this genuinely
        // represents an employee editing their OWN profile (the scenario under test).
        var ownerUserId = Guid.NewGuid();
        await LinkEmployeeToUser(empId, ownerUserId);

        var employeeUser = Substitute.For<ICurrentUser>();
        employeeUser.Email.Returns("john@test.com");
        employeeUser.UserId.Returns(ownerUserId);
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

        // BUG-119 (#124): a self-service Employee-role caller may edit only their OWN record. Link
        // the seeded employee to the caller (see UpdateProfile_AsEmployee_ContactInfo_ShouldSucceed).
        var ownerUserId = Guid.NewGuid();
        await LinkEmployeeToUser(empId, ownerUserId);

        var employeeUser = Substitute.For<ICurrentUser>();
        employeeUser.Email.Returns("john@test.com");
        employeeUser.UserId.Returns(ownerUserId);
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

    // ── BUG-113: Location reassignment via the employment-info update path ──

    private async Task<Guid> SeedLocation(string name = "Head Office", bool isActive = true)
    {
        using var db = CreateDbContext();
        var location = new Location
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            Name = name,
            TimeZone = "Asia/Colombo",
            IsActive = isActive,
            IsDeleted = false,
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        return location.Id;
    }

    [Fact]
    public async Task UpdateProfile_LocationChange_ShouldPersistFk_AndCreateHistoryEntry_BUG113()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);
        var newLocationId = await SeedLocation("Galle Office");

        var service = CreateService();
        var result = await service.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            EmploymentInfo = new EmploymentInfoUpdate
            {
                LocationId = newLocationId,
                EffectiveDate = DateTime.UtcNow.Date,
                Reason = "Relocation",
            },
        });

        result.IsSuccess.Should().BeTrue();

        // FK persisted on the row
        using var db = CreateDbContext();
        var emp = db.Employees.First(e => e.Id == empId);
        emp.LocationId.Should().Be(newLocationId);

        // Employment-history entry mirrors the Department/JobTitle pattern
        result.Value!.EmploymentHistory.Should().ContainSingle(h => h.ChangeType == "Location");
        var entry = result.Value.EmploymentHistory.First(h => h.ChangeType == "Location");
        entry.NewValue.Should().Be("Galle Office");
        entry.NewReferenceId.Should().Be(newLocationId);
        entry.Reason.Should().Be("Relocation");
    }

    [Fact]
    public async Task UpdateProfile_LocationChangeToInactive_ShouldFail_BUG113()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);
        var inactiveLocationId = await SeedLocation("Shuttered Office", isActive: false);

        var service = CreateService();
        var result = await service.UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            EmploymentInfo = new EmploymentInfoUpdate { LocationId = inactiveLocationId },
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("Location not found or is not active");
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
        var serviceA = new EmployeeService(dbA, ctxA, _currentUser, _fileStorage, _virusScanner, _customFieldService, _auditLogger, _logger);
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
            TestDbContextFactory.Create(ctxB, _dbName), ctxB, _currentUser, _fileStorage, _virusScanner, _customFieldService, _auditLogger, _logger);

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

    // ── DF-38: structured address components ──────────────────────

    [Fact]
    [Trait("TC", "TC-CHR-335")]
    public async Task UpdateProfile_AddressComponents_ShouldPersistAndReadBack_DF38()
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
                Address = "123 Main St",
                City = "Colombo",
                State = "Western",
                PostalCode = "00100",
                Country = "Sri Lanka",
            },
        });

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.City.Should().Be("Colombo");
        result.Value.State.Should().Be("Western");
        result.Value.PostalCode.Should().Be("00100");
        result.Value.Country.Should().Be("Sri Lanka");

        // Persisted on the row
        using var db = CreateDbContext();
        var emp = await db.Employees.FirstAsync(e => e.Id == empId);
        emp.City.Should().Be("Colombo");
        emp.State.Should().Be("Western");
        emp.PostalCode.Should().Be("00100");
        emp.Country.Should().Be("Sri Lanka");

        // Contact-section audit captures the new component
        var audit = await db.EmployeeFieldAuditLogs
            .SingleAsync(a => a.EmployeeId == empId && a.Section == "ContactInfo");
        audit.AfterSnapshot.Should().Contain("Colombo");
    }

    // ── DF-39: Education / WorkHistory / Dependents full-replace ───

    [Fact]
    [Trait("TC", "TC-CHR-335")]
    public async Task UpdateProfile_Education_FullReplace_AddUpdateRemove_DF39()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        // Seed two entries
        var result1 = await CreateService().UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            UpdateEducation = true,
            Education = new List<EducationInput>
            {
                new() { Institution = "MIT", Degree = "BSc", FieldOfStudy = "CS", StartYear = "2010", EndYear = "2014" },
                new() { Institution = "Harvard", Degree = "MBA" },
            },
        });
        result1.IsSuccess.Should().BeTrue(result1.Error);
        result1.Value!.Education.Should().HaveCount(2);
        var keptId = result1.Value.Education.Single(e => e.Institution == "MIT").Id;

        // Full replace: keep MIT (by Id, updated), drop Harvard, add a new one
        var result2 = await CreateService().UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            UpdateEducation = true,
            Education = new List<EducationInput>
            {
                new() { Id = keptId, Institution = "MIT", Degree = "MSc", FieldOfStudy = "AI" },
                new() { Institution = "Stanford", Degree = "PhD" },
            },
        });
        result2.IsSuccess.Should().BeTrue(result2.Error);
        result2.Value!.Education.Should().HaveCount(2);
        result2.Value.Education.Should().Contain(e => e.Id == keptId && e.Degree == "MSc" && e.FieldOfStudy == "AI");
        result2.Value.Education.Should().Contain(e => e.Institution == "Stanford");
        result2.Value.Education.Should().NotContain(e => e.Institution == "Harvard");

        // Rows are tenant-stamped
        using var db = CreateDbContext();
        var rows = await db.EmployeeEducation.Where(e => e.EmployeeId == empId).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.TenantId == _tenantId);
    }

    [Fact]
    [Trait("TC", "TC-CHR-335")]
    public async Task UpdateProfile_Education_EmptyList_ClearsAll_DF39()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        await CreateService().UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            UpdateEducation = true,
            Education = new List<EducationInput> { new() { Institution = "MIT", Degree = "BSc" } },
        });

        var cleared = await CreateService().UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            UpdateEducation = true,
            Education = new List<EducationInput>(),
        });

        cleared.IsSuccess.Should().BeTrue(cleared.Error);
        cleared.Value!.Education.Should().BeEmpty();
    }

    [Fact]
    [Trait("TC", "TC-CHR-335")]
    public async Task UpdateProfile_Education_NullWithoutFlag_LeavesUntouched_DF39()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        await CreateService().UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            UpdateEducation = true,
            Education = new List<EducationInput> { new() { Institution = "MIT", Degree = "BSc" } },
        });

        // A subsequent update that does not touch education (flag false, list null) must preserve it.
        var untouched = await CreateService().UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            ContactInfo = new ContactInfoUpdate { Phone = "+123" },
        });

        untouched.IsSuccess.Should().BeTrue(untouched.Error);
        untouched.Value!.Education.Should().ContainSingle(e => e.Institution == "MIT");
    }

    [Fact]
    [Trait("TC", "TC-CHR-335")]
    public async Task UpdateProfile_WorkHistory_FullReplace_WithDateOnly_DF39()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        var result = await CreateService().UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            UpdateWorkHistory = true,
            WorkHistory = new List<WorkHistoryInput>
            {
                new()
                {
                    Company = "Acme",
                    Position = "Engineer",
                    FromDate = new DateOnly(2018, 1, 1),
                    ToDate = new DateOnly(2020, 6, 30),
                    Description = "Built things",
                },
            },
        });

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.WorkHistory.Should().ContainSingle();
        var wh = result.Value.WorkHistory[0];
        wh.Company.Should().Be("Acme");
        wh.Position.Should().Be("Engineer");
        wh.FromDate.Should().Be(new DateOnly(2018, 1, 1));
        wh.ToDate.Should().Be(new DateOnly(2020, 6, 30));

        using var db = CreateDbContext();
        var rows = await db.EmployeeWorkHistory.Where(e => e.EmployeeId == empId).ToListAsync();
        rows.Should().HaveCount(1);
        rows.Should().OnlyContain(r => r.TenantId == _tenantId);
    }

    [Fact]
    [Trait("TC", "TC-CHR-335")]
    public async Task UpdateProfile_Dependents_FullReplace_WithDateOnly_DF39()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        var result = await CreateService().UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            UpdateDependents = true,
            Dependents = new List<DependentInput>
            {
                new() { Name = "Kid One", Relationship = "Child", DateOfBirth = new DateOnly(2015, 5, 20) },
                new() { Name = "Spouse", Relationship = "Spouse" },
            },
        });

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Dependents.Should().HaveCount(2);
        result.Value.Dependents.Should().Contain(d => d.Name == "Kid One" && d.DateOfBirth == new DateOnly(2015, 5, 20));
        result.Value.Dependents.Should().Contain(d => d.Name == "Spouse" && d.DateOfBirth == null);

        using var db = CreateDbContext();
        var rows = await db.EmployeeDependents.Where(e => e.EmployeeId == empId).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.TenantId == _tenantId);
    }

    // Removal-of-omitted for WorkHistory (the WithDateOnly arm above only proves add) — a full replace that
    // seeds two rows, keeps one by Id, drops the other, adds a new one, and asserts the dropped row is gone.
    [Fact]
    [Trait("TC", "TC-CHR-335")]
    public async Task UpdateProfile_WorkHistory_FullReplace_RemovesOmitted_DF39()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        var seed = await CreateService().UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            UpdateWorkHistory = true,
            WorkHistory = new List<WorkHistoryInput>
            {
                new() { Company = "Acme", Position = "Engineer", FromDate = new DateOnly(2018, 1, 1) },
                new() { Company = "Globex", Position = "Lead", FromDate = new DateOnly(2020, 2, 1) },
            },
        });
        seed.IsSuccess.Should().BeTrue(seed.Error);
        var keptId = seed.Value!.WorkHistory.Single(w => w.Company == "Acme").Id;

        var replaced = await CreateService().UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            UpdateWorkHistory = true,
            WorkHistory = new List<WorkHistoryInput>
            {
                new() { Id = keptId, Company = "Acme", Position = "Principal", Description = "Promoted" },
                new() { Company = "Initech", Position = "Architect" },
            },
        });
        replaced.IsSuccess.Should().BeTrue(replaced.Error);
        replaced.Value!.WorkHistory.Should().HaveCount(2);
        replaced.Value.WorkHistory.Should().Contain(w => w.Id == keptId && w.Position == "Principal" && w.Description == "Promoted");
        replaced.Value.WorkHistory.Should().Contain(w => w.Company == "Initech");
        replaced.Value.WorkHistory.Should().NotContain(w => w.Company == "Globex");

        using var db = CreateDbContext();
        var rows = await db.EmployeeWorkHistory.Where(e => e.EmployeeId == empId).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.TenantId == _tenantId);
    }

    // Removal-of-omitted for Dependents (the WithDateOnly arm above only proves add).
    [Fact]
    [Trait("TC", "TC-CHR-335")]
    public async Task UpdateProfile_Dependents_FullReplace_RemovesOmitted_DF39()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        var seed = await CreateService().UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            UpdateDependents = true,
            Dependents = new List<DependentInput>
            {
                new() { Name = "Kid One", Relationship = "Child", DateOfBirth = new DateOnly(2015, 5, 20) },
                new() { Name = "Kid Two", Relationship = "Child", DateOfBirth = new DateOnly(2018, 8, 10) },
            },
        });
        seed.IsSuccess.Should().BeTrue(seed.Error);
        var keptId = seed.Value!.Dependents.Single(d => d.Name == "Kid One").Id;

        var replaced = await CreateService().UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            UpdateDependents = true,
            Dependents = new List<DependentInput>
            {
                new() { Id = keptId, Name = "Kid One", Relationship = "Daughter" },
                new() { Name = "Spouse", Relationship = "Spouse" },
            },
        });
        replaced.IsSuccess.Should().BeTrue(replaced.Error);
        replaced.Value!.Dependents.Should().HaveCount(2);
        replaced.Value.Dependents.Should().Contain(d => d.Id == keptId && d.Relationship == "Daughter");
        replaced.Value.Dependents.Should().Contain(d => d.Name == "Spouse");
        replaced.Value.Dependents.Should().NotContain(d => d.Name == "Kid Two");

        using var db = CreateDbContext();
        var rows = await db.EmployeeDependents.Where(e => e.EmployeeId == empId).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.TenantId == _tenantId);
    }

    // Cross-tenant isolation on the 3 new sub-tables: the global query filters were added but were previously
    // untested. Seed rows under tenant A, then read each sub-table through a DIFFERENT tenant's context and
    // assert nothing leaks (the EF query filter is provider-agnostic, so InMemory genuinely exercises it).
    [Fact]
    [Trait("TC", "TC-CHR-335")]
    public async Task SubCollections_AreTenantIsolated_DF39()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        await CreateService().UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            UpdateEducation = true,
            Education = new List<EducationInput> { new() { Institution = "MIT", Degree = "BSc" } },
            UpdateWorkHistory = true,
            WorkHistory = new List<WorkHistoryInput> { new() { Company = "Acme", Position = "Engineer" } },
            UpdateDependents = true,
            Dependents = new List<DependentInput> { new() { Name = "Kid One", Relationship = "Child" } },
        });

        var otherCtx = Substitute.For<ITenantContext>();
        otherCtx.TenantId.Returns(Guid.NewGuid());
        otherCtx.IsResolved.Returns(true);
        using var otherDb = TestDbContextFactory.Create(otherCtx, _dbName);

        (await otherDb.EmployeeEducation.Where(e => e.EmployeeId == empId).ToListAsync()).Should().BeEmpty();
        (await otherDb.EmployeeWorkHistory.Where(e => e.EmployeeId == empId).ToListAsync()).Should().BeEmpty();
        (await otherDb.EmployeeDependents.Where(e => e.EmployeeId == empId).ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    [Trait("TC", "TC-CHR-335")]
    public async Task GetProfile_ReturnsAllNewSubCollections_DF39()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        var empId = await SeedEmployee(deptId, jtId);

        await CreateService().UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
        {
            RowVersion = 0,
            UpdateEducation = true,
            Education = new List<EducationInput> { new() { Institution = "MIT", Degree = "BSc" } },
            UpdateWorkHistory = true,
            WorkHistory = new List<WorkHistoryInput> { new() { Company = "Acme", Position = "Eng" } },
            UpdateDependents = true,
            Dependents = new List<DependentInput> { new() { Name = "Kid", Relationship = "Child" } },
        });

        var profile = await CreateService().GetProfileAsync(empId);

        profile.IsSuccess.Should().BeTrue(profile.Error);
        profile.Value!.Education.Should().ContainSingle(e => e.Institution == "MIT");
        profile.Value.WorkHistory.Should().ContainSingle(w => w.Company == "Acme");
        profile.Value.Dependents.Should().ContainSingle(d => d.Name == "Kid");
    }

    public void Dispose()
    {
        // InMemory databases are cleaned up when the last connection closes
    }
}
