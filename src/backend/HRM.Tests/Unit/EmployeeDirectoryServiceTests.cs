// ============================================================================
// US-CHR-003: Employee Directory Search, Filter, Sort, Export, and Role-Based
// Visibility Unit Tests.
// Tests: partial search across name/email/employee_no/phone (FR-1, AC-2),
// multi-select filter combinations (FR-2, AC-3), pagination counts (FR-5, AC-4),
// sorting (FR-4), tenant isolation (NFR-3), role-based field stripping (FR-9,
// BR-2/3/4), show-archived toggle (BR-1), CSV export row/column correctness
// (FR-8, AC-5), and Excel export (via ClosedXML).
// Uses EF Core InMemory provider for lightweight database testing.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Employees.DTOs;
using HRM.Application.Features.Employees.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class EmployeeDirectoryServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<EmployeeDirectoryService> _logger;

    public EmployeeDirectoryServiceTests()
    {
        _dbName = Guid.NewGuid().ToString();
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);
        _logger = Substitute.For<ILogger<EmployeeDirectoryService>>();
    }

    private EmployeeDirectoryService CreateService(ITenantContext? ctx = null)
    {
        var dbContext = TestDbContextFactory.Create(ctx ?? _tenantContext, _dbName);
        return new EmployeeDirectoryService(dbContext, ctx ?? _tenantContext, _logger);
    }

    private Infrastructure.Persistence.AppDbContext CreateDbContext(ITenantContext? ctx = null)
    {
        return TestDbContextFactory.Create(ctx ?? _tenantContext, _dbName);
    }

    private async Task<(Guid DeptId, Guid JtId)> SeedDepartmentAndJobTitle(
        string deptName = "Engineering", string deptCode = "ENG",
        string jtName = "Software Engineer", Guid? tenantId = null)
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
            Name = deptName,
            Code = deptCode,
            IsActive = true,
            IsDeleted = false,
        };
        var jt = new JobTitle
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tid,
            TitleName = jtName,
            IsActive = true,
            IsDeleted = false,
        };
        db.Departments.Add(dept);
        db.JobTitles.Add(jt);
        await db.SaveChangesAsync();
        return (dept.Id, jt.Id);
    }

    private async Task<Employee> SeedEmployee(
        Guid deptId, Guid jtId,
        string firstName = "John", string lastName = "Doe",
        string email = "john@test.com", string? phone = "+1234567890",
        EmployeeStatus status = EmployeeStatus.Active,
        EmploymentType empType = EmploymentType.FullTime,
        string? location = null,
        DateTime? dateOfJoining = null,
        bool isDeleted = false,
        Guid? tenantId = null,
        string? employeeNo = null)
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
        var emp = new Employee
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tid,
            EmployeeNo = employeeNo ?? $"EMP-{Guid.NewGuid().ToString()[..4]}",
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Phone = phone,
            DateOfJoining = dateOfJoining ?? DateTime.UtcNow.Date,
            DepartmentId = deptId,
            JobTitleId = jtId,
            EmploymentType = empType,
            Status = status,
            Location = location,
            IsActive = status == EmployeeStatus.Active || status == EmployeeStatus.Probation,
            IsDeleted = isDeleted,
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();
        return emp;
    }

    // ── FR-1 / AC-2: Search by partial name, email, employee_no, phone ──

    [Fact]
    public async Task Search_ByPartialFirstName_ShouldReturnMatches()
    {
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();
        await SeedEmployee(deptId, jtId, firstName: "Jonathan", email: "jon@test.com");
        await SeedEmployee(deptId, jtId, firstName: "Alice", email: "alice@test.com");

        var service = CreateService();
        var result = await service.GetDirectoryAsync(
            search: "jona", null, null, null, null, null, null, null,
            null, false, 1, 20, false, DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data.Should().HaveCount(1);
        result.Value.Data[0].FirstName.Should().Be("Jonathan");
    }

    [Fact]
    public async Task Search_ByPartialEmail_ShouldReturnMatches()
    {
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();
        await SeedEmployee(deptId, jtId, firstName: "John", email: "john.smith@example.com");
        await SeedEmployee(deptId, jtId, firstName: "Jane", email: "jane@test.com");

        var service = CreateService();
        var result = await service.GetDirectoryAsync(
            search: "smith@example", null, null, null, null, null, null, null,
            null, false, 1, 20, false, DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data.Should().HaveCount(1);
        result.Value.Data[0].Email.Should().Be("john.smith@example.com");
    }

    [Fact]
    public async Task Search_ByPartialEmployeeNo_ShouldReturnMatches()
    {
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();
        await SeedEmployee(deptId, jtId, firstName: "A", email: "a@test.com", employeeNo: "EMP-0042");
        await SeedEmployee(deptId, jtId, firstName: "B", email: "b@test.com", employeeNo: "EMP-0099");

        var service = CreateService();
        var result = await service.GetDirectoryAsync(
            search: "0042", null, null, null, null, null, null, null,
            null, false, 1, 20, false, DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data.Should().HaveCount(1);
        result.Value.Data[0].EmployeeNo.Should().Be("EMP-0042");
    }

    [Fact]
    public async Task Search_ByPhone_ShouldReturnMatches()
    {
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();
        await SeedEmployee(deptId, jtId, firstName: "A", email: "a@test.com", phone: "+94771234567");
        await SeedEmployee(deptId, jtId, firstName: "B", email: "b@test.com", phone: "+94779876543");

        var service = CreateService();
        var result = await service.GetDirectoryAsync(
            search: "1234567", null, null, null, null, null, null, null,
            null, false, 1, 20, false, DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data.Should().HaveCount(1);
        result.Value.Data[0].FirstName.Should().Be("A");
    }

    [Fact]
    public async Task Search_CaseInsensitive_ShouldWork()
    {
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();
        await SeedEmployee(deptId, jtId, firstName: "UPPERCASE", email: "up@test.com");

        var service = CreateService();
        var result = await service.GetDirectoryAsync(
            search: "uppercase", null, null, null, null, null, null, null,
            null, false, 1, 20, false, DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data.Should().HaveCount(1);
    }

    // ── FR-2 / AC-3: Filter by department + status combination ──────

    [Fact]
    public async Task Filter_ByDepartmentAndStatus_ShouldReturnOnlyMatching()
    {
        var (engDeptId, seJtId) = await SeedDepartmentAndJobTitle("Engineering", "ENG", "Engineer");
        var (hrDeptId, hrJtId) = await SeedDepartmentAndJobTitle("HR", "HR", "HR Specialist");

        await SeedEmployee(engDeptId, seJtId, firstName: "Eng Active", email: "ea@test.com", status: EmployeeStatus.Active);
        await SeedEmployee(engDeptId, seJtId, firstName: "Eng Terminated", email: "et@test.com", status: EmployeeStatus.Terminated);
        await SeedEmployee(hrDeptId, hrJtId, firstName: "HR Active", email: "ha@test.com", status: EmployeeStatus.Active);

        var service = CreateService();
        var result = await service.GetDirectoryAsync(
            search: null,
            departmentIds: [engDeptId],
            jobTitles: null,
            statuses: ["Active"],
            employmentTypes: null,
            locations: null,
            dateOfJoiningFrom: null,
            dateOfJoiningTo: null,
            sortBy: null,
            sortDescending: false,
            page: 1, pageSize: 20,
            showArchived: false,
            DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data.Should().HaveCount(1);
        result.Value.Data[0].FirstName.Should().Be("Eng Active");
    }

    [Fact]
    public async Task Filter_ByMultipleDepartments_ShouldReturnAll()
    {
        var (deptA, jtA) = await SeedDepartmentAndJobTitle("Dept A", "DA", "Title A");
        var (deptB, jtB) = await SeedDepartmentAndJobTitle("Dept B", "DB", "Title B");
        var (deptC, jtC) = await SeedDepartmentAndJobTitle("Dept C", "DC", "Title C");

        await SeedEmployee(deptA, jtA, firstName: "EmpA", email: "a@test.com");
        await SeedEmployee(deptB, jtB, firstName: "EmpB", email: "b@test.com");
        await SeedEmployee(deptC, jtC, firstName: "EmpC", email: "c@test.com");

        var service = CreateService();
        var result = await service.GetDirectoryAsync(
            search: null,
            departmentIds: [deptA, deptB],
            jobTitles: null, statuses: null, employmentTypes: null,
            locations: null, dateOfJoiningFrom: null, dateOfJoiningTo: null,
            sortBy: null, sortDescending: false,
            page: 1, pageSize: 20,
            showArchived: false,
            DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task Filter_ByEmploymentType_ShouldWork()
    {
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();
        await SeedEmployee(deptId, jtId, firstName: "FT", email: "ft@t.com", empType: EmploymentType.FullTime);
        await SeedEmployee(deptId, jtId, firstName: "CT", email: "ct@t.com", empType: EmploymentType.Contract);

        var service = CreateService();
        var result = await service.GetDirectoryAsync(
            null, null, null, null, employmentTypes: ["Contract"],
            null, null, null, null, false, 1, 20, false, DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data.Should().HaveCount(1);
        result.Value.Data[0].FirstName.Should().Be("CT");
    }

    [Fact]
    public async Task Filter_ByLocation_ShouldWork()
    {
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();
        await SeedEmployee(deptId, jtId, firstName: "NY", email: "ny@t.com", location: "New York");
        await SeedEmployee(deptId, jtId, firstName: "SF", email: "sf@t.com", location: "San Francisco");

        var service = CreateService();
        var result = await service.GetDirectoryAsync(
            null, null, null, null, null,
            locations: ["New York"],
            null, null, null, false, 1, 20, false, DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data.Should().HaveCount(1);
        result.Value.Data[0].FirstName.Should().Be("NY");
    }

    [Fact]
    public async Task Filter_ByDateOfJoiningRange_ShouldWork()
    {
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();
        await SeedEmployee(deptId, jtId, firstName: "Jan", email: "jan@t.com",
            dateOfJoining: new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc));
        await SeedEmployee(deptId, jtId, firstName: "Jun", email: "jun@t.com",
            dateOfJoining: new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc));
        await SeedEmployee(deptId, jtId, firstName: "Dec", email: "dec@t.com",
            dateOfJoining: new DateTime(2025, 12, 15, 0, 0, 0, DateTimeKind.Utc));

        var service = CreateService();
        var result = await service.GetDirectoryAsync(
            null, null, null, null, null, null,
            dateOfJoiningFrom: new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            dateOfJoiningTo: new DateTime(2025, 7, 31, 0, 0, 0, DateTimeKind.Utc),
            null, false, 1, 20, false, DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data.Should().HaveCount(1);
        result.Value.Data[0].FirstName.Should().Be("Jun");
    }

    // ── FR-5 / AC-4: Pagination counts ──────────────────────────────

    [Fact]
    public async Task Pagination_ShouldReturnCorrectPageAndTotal()
    {
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();
        for (int i = 1; i <= 25; i++)
        {
            await SeedEmployee(deptId, jtId,
                firstName: $"Emp{i:D2}", email: $"emp{i}@test.com",
                employeeNo: $"EMP-{i:D4}");
        }

        var service = CreateService();

        // Page 1: 10 items
        var page1 = await service.GetDirectoryAsync(
            null, null, null, null, null, null, null, null,
            null, false, 1, 10, false, DirectoryFieldVisibility.Full);

        page1.IsSuccess.Should().BeTrue();
        page1.Value!.Data.Should().HaveCount(10);
        page1.Value.Total.Should().Be(25);
        page1.Value.Page.Should().Be(1);
        page1.Value.PageSize.Should().Be(10);

        // Page 3: 5 items (remaining)
        var page3 = await service.GetDirectoryAsync(
            null, null, null, null, null, null, null, null,
            null, false, 3, 10, false, DirectoryFieldVisibility.Full);

        page3.IsSuccess.Should().BeTrue();
        page3.Value!.Data.Should().HaveCount(5);
        page3.Value.Total.Should().Be(25);
        page3.Value.Page.Should().Be(3);
    }

    // ── FR-4: Sorting ───────────────────────────────────────────────

    [Fact]
    public async Task Sort_ByNameAscending_ShouldBeDefault()
    {
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();
        await SeedEmployee(deptId, jtId, firstName: "Zara", email: "z@t.com");
        await SeedEmployee(deptId, jtId, firstName: "Alice", email: "a@t.com");
        await SeedEmployee(deptId, jtId, firstName: "Mike", email: "m@t.com");

        var service = CreateService();
        var result = await service.GetDirectoryAsync(
            null, null, null, null, null, null, null, null,
            sortBy: null, sortDescending: false,
            1, 20, false, DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data[0].FirstName.Should().Be("Alice");
        result.Value.Data[1].FirstName.Should().Be("Mike");
        result.Value.Data[2].FirstName.Should().Be("Zara");
    }

    [Fact]
    public async Task Sort_ByEmployeeNoDescending_ShouldWork()
    {
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();
        await SeedEmployee(deptId, jtId, firstName: "A", email: "a@t.com", employeeNo: "EMP-0001");
        await SeedEmployee(deptId, jtId, firstName: "B", email: "b@t.com", employeeNo: "EMP-0010");
        await SeedEmployee(deptId, jtId, firstName: "C", email: "c@t.com", employeeNo: "EMP-0005");

        var service = CreateService();
        var result = await service.GetDirectoryAsync(
            null, null, null, null, null, null, null, null,
            sortBy: "employee_no", sortDescending: true,
            1, 20, false, DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data[0].EmployeeNo.Should().Be("EMP-0010");
        result.Value.Data[1].EmployeeNo.Should().Be("EMP-0005");
        result.Value.Data[2].EmployeeNo.Should().Be("EMP-0001");
    }

    [Fact]
    public async Task Sort_ByDateOfJoining_ShouldWork()
    {
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();
        await SeedEmployee(deptId, jtId, firstName: "Old", email: "old@t.com",
            dateOfJoining: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await SeedEmployee(deptId, jtId, firstName: "New", email: "new@t.com",
            dateOfJoining: new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var service = CreateService();
        var result = await service.GetDirectoryAsync(
            null, null, null, null, null, null, null, null,
            sortBy: "date_of_joining", sortDescending: true,
            1, 20, false, DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data[0].FirstName.Should().Be("New");
        result.Value.Data[1].FirstName.Should().Be("Old");
    }

    // ── NFR-3: Tenant isolation ─────────────────────────────────────

    [Fact]
    public async Task TenantIsolation_ShouldNotLeakCrossTenantData()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var (deptA, jtA) = await SeedDepartmentAndJobTitle("Eng A", "ENGA", "SE A", tenantA);
        var (deptB, jtB) = await SeedDepartmentAndJobTitle("Eng B", "ENGB", "SE B", tenantB);

        await SeedEmployee(deptA, jtA, firstName: "TenantA", email: "a@t.com", tenantId: tenantA);
        await SeedEmployee(deptB, jtB, firstName: "TenantB", email: "b@t.com", tenantId: tenantB);

        // Query from tenant A
        var ctxA = Substitute.For<ITenantContext>();
        ctxA.TenantId.Returns(tenantA);
        ctxA.IsResolved.Returns(true);
        var serviceA = CreateService(ctxA);

        var resultA = await serviceA.GetDirectoryAsync(
            null, null, null, null, null, null, null, null,
            null, false, 1, 20, false, DirectoryFieldVisibility.Full);

        resultA.IsSuccess.Should().BeTrue();
        resultA.Value!.Data.Should().HaveCount(1);
        resultA.Value.Data[0].FirstName.Should().Be("TenantA");
        resultA.Value.Total.Should().Be(1);

        // Query from tenant B
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);
        var serviceB = CreateService(ctxB);

        var resultB = await serviceB.GetDirectoryAsync(
            null, null, null, null, null, null, null, null,
            null, false, 1, 20, false, DirectoryFieldVisibility.Full);

        resultB.IsSuccess.Should().BeTrue();
        resultB.Value!.Data.Should().HaveCount(1);
        resultB.Value.Data[0].FirstName.Should().Be("TenantB");
    }

    [Fact]
    public async Task TenantNotResolved_ShouldFail()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.IsResolved.Returns(false);
        var service = CreateService(ctx);

        var result = await service.GetDirectoryAsync(
            null, null, null, null, null, null, null, null,
            null, false, 1, 20, false, DirectoryFieldVisibility.Full);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Tenant context is not resolved");
    }

    // ── FR-9 / BR-2/3/4: Role-based field stripping ─────────────────

    [Fact]
    public async Task FieldVisibility_Full_ShouldIncludeAllFields()
    {
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();
        await SeedEmployee(deptId, jtId, firstName: "John", email: "john@test.com",
            phone: "+1234", location: "NYC",
            dateOfJoining: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var service = CreateService();
        var result = await service.GetDirectoryAsync(
            null, null, null, null, null, null, null, null,
            null, false, 1, 20, false, DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        var emp = result.Value!.Data[0];
        emp.Email.Should().Be("john@test.com");
        emp.Phone.Should().Be("+1234");
        emp.DateOfJoining.Should().NotBeNull();
        emp.EmploymentType.Should().NotBeNull();
    }

    [Fact]
    public async Task FieldVisibility_Manager_ShouldIncludeEmailPhoneDateOfJoining()
    {
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();
        await SeedEmployee(deptId, jtId, firstName: "John", email: "john@test.com",
            phone: "+1234",
            dateOfJoining: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var service = CreateService();
        var result = await service.GetDirectoryAsync(
            null, null, null, null, null, null, null, null,
            null, false, 1, 20, false, DirectoryFieldVisibility.Manager);

        result.IsSuccess.Should().BeTrue();
        var emp = result.Value!.Data[0];
        emp.Email.Should().Be("john@test.com");
        emp.Phone.Should().Be("+1234");
        emp.DateOfJoining.Should().NotBeNull();
    }

    [Fact]
    public async Task FieldVisibility_Basic_ShouldStripSensitiveFields()
    {
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();
        await SeedEmployee(deptId, jtId, firstName: "John", email: "john@test.com",
            phone: "+1234", location: "NYC",
            dateOfJoining: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var service = CreateService();
        var result = await service.GetDirectoryAsync(
            null, null, null, null, null, null, null, null,
            null, false, 1, 20, false, DirectoryFieldVisibility.Basic);

        result.IsSuccess.Should().BeTrue();
        var emp = result.Value!.Data[0];
        emp.Email.Should().BeNull("Basic visibility should strip email");
        emp.Phone.Should().BeNull("Basic visibility should strip phone");
        emp.DateOfJoining.Should().BeNull("Basic visibility should strip date of joining");
        emp.EmploymentType.Should().BeNull("Basic visibility should strip employment type");

        // These should still be visible
        emp.FirstName.Should().Be("John");
        emp.DepartmentName.Should().NotBeNull();
        emp.JobTitleName.Should().NotBeNull();
        emp.Status.Should().NotBeNull();
        emp.Location.Should().Be("NYC");
    }

    // ── BR-1: Show archived employees ───────────────────────────────

    [Fact]
    public async Task ShowArchived_True_ShouldIncludeDeletedEmployees()
    {
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();
        await SeedEmployee(deptId, jtId, firstName: "Active", email: "active@t.com", isDeleted: false);
        await SeedEmployee(deptId, jtId, firstName: "Deleted", email: "deleted@t.com", isDeleted: true);

        var service = CreateService();

        // Without showArchived
        var withoutArchived = await service.GetDirectoryAsync(
            null, null, null, null, null, null, null, null,
            null, false, 1, 20, showArchived: false, DirectoryFieldVisibility.Full);
        withoutArchived.Value!.Data.Should().HaveCount(1);
        withoutArchived.Value.Data[0].FirstName.Should().Be("Active");

        // With showArchived (uses IgnoreQueryFilters but still tenant-scoped)
        service = CreateService(); // fresh context
        var withArchived = await service.GetDirectoryAsync(
            null, null, null, null, null, null, null, null,
            null, false, 1, 20, showArchived: true, DirectoryFieldVisibility.Full);
        withArchived.Value!.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task ShowArchived_ShouldStillBeTenantScoped()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var (deptA, jtA) = await SeedDepartmentAndJobTitle("Eng A", "ENGA", "SE A", tenantA);
        var (deptB, jtB) = await SeedDepartmentAndJobTitle("Eng B", "ENGB", "SE B", tenantB);

        await SeedEmployee(deptA, jtA, firstName: "A-Deleted", email: "ad@t.com", isDeleted: true, tenantId: tenantA);
        await SeedEmployee(deptB, jtB, firstName: "B-Deleted", email: "bd@t.com", isDeleted: true, tenantId: tenantB);

        var ctxA = Substitute.For<ITenantContext>();
        ctxA.TenantId.Returns(tenantA);
        ctxA.IsResolved.Returns(true);
        var serviceA = CreateService(ctxA);

        var result = await serviceA.GetDirectoryAsync(
            null, null, null, null, null, null, null, null,
            null, false, 1, 20, showArchived: true, DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data.Should().HaveCount(1);
        result.Value.Data[0].FirstName.Should().Be("A-Deleted");
    }

    // ── FR-8 / AC-5 / BR-4: CSV export ─────────────────────────────

    [Fact]
    public async Task ExportCsv_ShouldContainCorrectRowsAndColumns()
    {
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();
        for (int i = 1; i <= 10; i++)
        {
            await SeedEmployee(deptId, jtId,
                firstName: $"Emp{i}", email: $"emp{i}@test.com",
                employeeNo: $"EMP-{i:D4}", location: "NYC");
        }

        var service = CreateService();
        var result = await service.ExportDirectoryAsync(
            ExportFormat.Csv,
            null, null, null, null, null, null, null, null,
            null, false, false, DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("text/csv");
        result.Value.FileName.Should().EndWith(".csv");

        // Parse CSV: BOM + header + 10 data rows
        var csvText = System.Text.Encoding.UTF8.GetString(result.Value.FileBytes);
        // Strip BOM if present
        if (csvText.Length > 0 && csvText[0] == '﻿')
            csvText = csvText[1..];

        var lines = csvText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        lines.Should().HaveCount(11); // 1 header + 10 data rows

        // Header should include all full-visibility columns
        var header = lines[0];
        header.Should().Contain("Employee No");
        header.Should().Contain("First Name");
        header.Should().Contain("Email");
        header.Should().Contain("Phone");
        header.Should().Contain("Department");
        header.Should().Contain("Job Title");
        header.Should().Contain("Status");
        header.Should().Contain("Employment Type");
        header.Should().Contain("Date of Joining");
        header.Should().Contain("Location");
    }

    [Fact]
    public async Task ExportCsv_BasicVisibility_ShouldOmitSensitiveColumns()
    {
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();
        await SeedEmployee(deptId, jtId, firstName: "John", email: "john@t.com");

        var service = CreateService();
        var result = await service.ExportDirectoryAsync(
            ExportFormat.Csv,
            null, null, null, null, null, null, null, null,
            null, false, false, DirectoryFieldVisibility.Basic);

        result.IsSuccess.Should().BeTrue();
        var csvText = System.Text.Encoding.UTF8.GetString(result.Value!.FileBytes);
        if (csvText.Length > 0 && csvText[0] == '﻿')
            csvText = csvText[1..];

        var header = csvText.Split('\n')[0];
        header.Should().NotContain("Email");
        header.Should().NotContain("Phone");
        header.Should().NotContain("Employment Type");
        header.Should().NotContain("Date of Joining");

        // These should still be present
        header.Should().Contain("Employee No");
        header.Should().Contain("First Name");
        header.Should().Contain("Department");
    }

    [Fact]
    public async Task ExportCsv_WithFilters_ShouldRespectFilters()
    {
        var (engDeptId, seJtId) = await SeedDepartmentAndJobTitle("Engineering", "ENG", "Engineer");
        var (hrDeptId, hrJtId) = await SeedDepartmentAndJobTitle("HR", "HR", "HR Specialist");

        await SeedEmployee(engDeptId, seJtId, firstName: "Eng1", email: "eng1@t.com");
        await SeedEmployee(engDeptId, seJtId, firstName: "Eng2", email: "eng2@t.com");
        await SeedEmployee(hrDeptId, hrJtId, firstName: "HR1", email: "hr1@t.com");

        var service = CreateService();
        var result = await service.ExportDirectoryAsync(
            ExportFormat.Csv,
            null,
            departmentIds: [engDeptId],
            null, null, null, null, null, null,
            null, false, false, DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        var csvText = System.Text.Encoding.UTF8.GetString(result.Value!.FileBytes);
        if (csvText.Length > 0 && csvText[0] == '﻿')
            csvText = csvText[1..];

        var lines = csvText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        lines.Should().HaveCount(3); // header + 2 engineering employees
    }

    // ── Excel export ────────────────────────────────────────────────

    [Fact]
    public async Task ExportExcel_ShouldReturnValidXlsx()
    {
        var (deptId, jtId) = await SeedDepartmentAndJobTitle();
        await SeedEmployee(deptId, jtId, firstName: "John", email: "john@t.com", employeeNo: "EMP-0001");

        var service = CreateService();
        var result = await service.ExportDirectoryAsync(
            ExportFormat.Excel,
            null, null, null, null, null, null, null, null,
            null, false, false, DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        result.Value.FileName.Should().EndWith(".xlsx");
        result.Value.FileBytes.Should().NotBeEmpty();

        // Verify we can open the workbook
        using var stream = new MemoryStream(result.Value.FileBytes);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
        var ws = workbook.Worksheets.First();
        ws.Name.Should().Be("Employee Directory");
        ws.Cell(1, 1).GetString().Should().Be("Employee No");
        ws.Cell(2, 1).GetString().Should().Be("EMP-0001");
    }

    // ── Export tenant isolation ──────────────────────────────────────

    [Fact]
    public async Task ExportCsv_TenantIsolation_ShouldNotIncludeCrossTenantData()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var (deptA, jtA) = await SeedDepartmentAndJobTitle("Eng A", "ENGA", "SE A", tenantA);
        var (deptB, jtB) = await SeedDepartmentAndJobTitle("Eng B", "ENGB", "SE B", tenantB);

        await SeedEmployee(deptA, jtA, firstName: "TenantA", email: "a@t.com", tenantId: tenantA);
        await SeedEmployee(deptB, jtB, firstName: "TenantB", email: "b@t.com", tenantId: tenantB);

        var ctxA = Substitute.For<ITenantContext>();
        ctxA.TenantId.Returns(tenantA);
        ctxA.IsResolved.Returns(true);
        var serviceA = CreateService(ctxA);

        var result = await serviceA.ExportDirectoryAsync(
            ExportFormat.Csv,
            null, null, null, null, null, null, null, null,
            null, false, false, DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        var csvText = System.Text.Encoding.UTF8.GetString(result.Value!.FileBytes);
        csvText.Should().Contain("TenantA");
        csvText.Should().NotContain("TenantB");
    }

    // ── Empty state ─────────────────────────────────────────────────

    [Fact]
    public async Task GetDirectory_NoEmployees_ShouldReturnEmptyResult()
    {
        var service = CreateService();
        var result = await service.GetDirectoryAsync(
            null, null, null, null, null, null, null, null,
            null, false, 1, 20, false, DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data.Should().BeEmpty();
        result.Value.Total.Should().Be(0);
    }

    // ── Search + Filter combined ────────────────────────────────────

    [Fact]
    public async Task SearchAndFilter_Combined_ShouldIntersect()
    {
        var (engDeptId, seJtId) = await SeedDepartmentAndJobTitle("Engineering", "ENG", "Engineer");
        var (hrDeptId, hrJtId) = await SeedDepartmentAndJobTitle("HR", "HR", "HR Specialist");

        await SeedEmployee(engDeptId, seJtId, firstName: "John", lastName: "Smith", email: "js@t.com");
        await SeedEmployee(engDeptId, seJtId, firstName: "Jane", lastName: "Smith", email: "janes@t.com");
        await SeedEmployee(hrDeptId, hrJtId, firstName: "John", lastName: "HR", email: "jh@t.com");

        var service = CreateService();
        var result = await service.GetDirectoryAsync(
            search: "John",
            departmentIds: [engDeptId],
            null, null, null, null, null, null,
            null, false, 1, 20, false, DirectoryFieldVisibility.Full);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data.Should().HaveCount(1);
        result.Value.Data[0].LastName.Should().Be("Smith");
    }

    public void Dispose()
    {
        // InMemory databases are cleaned up when the last connection closes
    }
}
