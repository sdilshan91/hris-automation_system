// ============================================================================
// US-CHR-001: Employee Management Unit Tests
// Tests employee creation with auto employee_no (AC-2, FR-2, BR-1),
// email uniqueness per tenant (AC-3, FR-3, BR-2), plan-limit enforcement
// (AC-5, FR-5), profile photo upload MIME/size validation (AC-4, FR-6),
// virus scanner invocation (NFR-3), custom_fields JSONB persistence (AC-6),
// cross-tenant isolation (NFR-2), and deferred FK wiring for Department
// (manager_id FK) and JobTitle (real employee count + deactivation block).
// Uses EF Core InMemory provider for lightweight database testing.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Employees.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class EmployeeServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _fileStorage;
    private readonly IVirusScanner _virusScanner;
    private readonly ILogger<EmployeeService> _logger;

    public EmployeeServiceTests()
    {
        _dbName = Guid.NewGuid().ToString();
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("admin@test.com");
        _currentUser.UserId.Returns(Guid.NewGuid());

        _fileStorage = Substitute.For<IFileStorage>();
        _fileStorage.UploadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => $"/{callInfo.ArgAt<Guid>(0)}/{callInfo.ArgAt<string>(1)}");
        _fileStorage.GetSignedUrl(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<TimeSpan?>())
            .Returns(callInfo => $"/files/{callInfo.ArgAt<Guid>(0)}/{callInfo.ArgAt<string>(1)}");

        _virusScanner = Substitute.For<IVirusScanner>();
        _virusScanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(VirusScanResult.Clean());

        _logger = Substitute.For<ILogger<EmployeeService>>();
    }

    private EmployeeService CreateService()
    {
        var dbContext = TestDbContextFactory.Create(_tenantContext, _dbName);
        return new EmployeeService(dbContext, _tenantContext, _currentUser, _fileStorage, _virusScanner, _logger);
    }

    private Infrastructure.Persistence.AppDbContext CreateDbContext()
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
        // Seed via a non-filtered context since Tenant doesn't use the standard tenant filter
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

    private CreateEmployeeRequest MakeRequest(Guid departmentId, Guid jobTitleId, string email = "john@test.com")
    {
        return new CreateEmployeeRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = email,
            Phone = "+1234567890",
            DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender = Gender.Male,
            DateOfJoining = DateTime.UtcNow.Date,
            DepartmentId = departmentId,
            JobTitleId = jobTitleId,
            EmploymentType = EmploymentType.FullTime,
        };
    }

    // ── AC-2: Create employee (happy path) ─────────────────────────

    [Fact]
    public async Task Create_ValidEmployee_ShouldSucceed_WithAutoEmployeeNo()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId);
        var service = CreateService();

        var result = await service.CreateAsync(MakeRequest(deptId, jtId));

        result.IsSuccess.Should().BeTrue();
        result.Value!.EmployeeNo.Should().Be("EMP-0001");
        result.Value.FirstName.Should().Be("John");
        result.Value.LastName.Should().Be("Doe");
        result.Value.Email.Should().Be("john@test.com");
        result.Value.Status.Should().Be("Active");
        result.Value.IsActive.Should().BeTrue();
        result.Value.DepartmentId.Should().Be(deptId);
        result.Value.JobTitleId.Should().Be(jtId);
        result.Value.EmploymentType.Should().Be("FullTime");
    }

    [Fact]
    public async Task Create_ShouldSetTenantIdFromContext()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId);
        var service = CreateService();

        var result = await service.CreateAsync(MakeRequest(deptId, jtId));

        result.IsSuccess.Should().BeTrue();

        // Verify tenant_id was set from session context (FR-4)
        using var db = CreateDbContext();
        var employee = db.Employees.First(e => e.Id == result.Value!.Id);
        employee.TenantId.Should().Be(_tenantId);
    }

    [Fact]
    public async Task Create_SecondEmployee_ShouldGetIncrementedNo()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId);
        var service = CreateService();

        var r1 = await service.CreateAsync(MakeRequest(deptId, jtId, "first@test.com"));
        r1.IsSuccess.Should().BeTrue();
        r1.Value!.EmployeeNo.Should().Be("EMP-0001");

        // Need new service instance to pick up the new state
        service = CreateService();
        var r2 = await service.CreateAsync(MakeRequest(deptId, jtId, "second@test.com"));
        r2.IsSuccess.Should().BeTrue();
        r2.Value!.EmployeeNo.Should().Be("EMP-0002");
    }

    [Fact]
    public async Task Create_WithProbationStatus_ShouldSucceed()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId);
        var service = CreateService();

        var request = MakeRequest(deptId, jtId);
        var probRequest = request with { Status = EmployeeStatus.Probation };
        var result = await service.CreateAsync(probRequest);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Probation");
    }

    // ── AC-3 / BR-2: Duplicate email rejection (same tenant) ──────

    [Fact]
    public async Task Create_DuplicateEmailSameTenant_ShouldFail()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId);
        var service = CreateService();

        var r1 = await service.CreateAsync(MakeRequest(deptId, jtId, "dup@test.com"));
        r1.IsSuccess.Should().BeTrue();

        service = CreateService();
        var r2 = await service.CreateAsync(MakeRequest(deptId, jtId, "dup@test.com"));

        r2.IsFailure.Should().BeTrue();
        r2.Error.Should().Be("An employee with this email already exists.");
    }

    [Fact]
    public async Task Create_SameEmailDifferentTenant_ShouldSucceed()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Seed tenant A data
        var ctxA = Substitute.For<ITenantContext>();
        ctxA.TenantId.Returns(tenantA);
        ctxA.IsResolved.Returns(true);

        var deptA = await SeedDepartment("Eng A", "ENGA", tenantA);
        var jtA = await SeedJobTitle("Eng A", tenantA);
        await SeedTenant(tenantA);

        var dbA = TestDbContextFactory.Create(ctxA, _dbName);
        var serviceA = new EmployeeService(dbA, ctxA, _currentUser, _fileStorage, _virusScanner, _logger);
        var r1 = await serviceA.CreateAsync(new CreateEmployeeRequest
        {
            FirstName = "John", LastName = "Doe", Email = "same@test.com",
            DateOfJoining = DateTime.UtcNow.Date,
            DepartmentId = deptA, JobTitleId = jtA, EmploymentType = EmploymentType.FullTime,
        });
        r1.IsSuccess.Should().BeTrue();

        // Seed tenant B data
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);

        var deptB = await SeedDepartment("Eng B", "ENGB", tenantB);
        var jtB = await SeedJobTitle("Eng B", tenantB);
        await SeedTenant(tenantB);

        var dbB = TestDbContextFactory.Create(ctxB, _dbName);
        var serviceB = new EmployeeService(dbB, ctxB, _currentUser, _fileStorage, _virusScanner, _logger);
        var r2 = await serviceB.CreateAsync(new CreateEmployeeRequest
        {
            FirstName = "Jane", LastName = "Doe", Email = "same@test.com",
            DateOfJoining = DateTime.UtcNow.Date,
            DepartmentId = deptB, JobTitleId = jtB, EmploymentType = EmploymentType.FullTime,
        });

        // Same email should succeed in different tenant (BR-2)
        r2.IsSuccess.Should().BeTrue();
    }

    // ── BR-1: Employee_no unique per tenant ────────────────────────

    [Fact]
    public async Task Create_EmployeeNoIsUniquePerTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Create employees in tenant A
        var ctxA = Substitute.For<ITenantContext>();
        ctxA.TenantId.Returns(tenantA);
        ctxA.IsResolved.Returns(true);
        var deptA = await SeedDepartment("Eng", "ENG", tenantA);
        var jtA = await SeedJobTitle("Eng", tenantA);
        await SeedTenant(tenantA);

        var dbA = TestDbContextFactory.Create(ctxA, _dbName);
        var serviceA = new EmployeeService(dbA, ctxA, _currentUser, _fileStorage, _virusScanner, _logger);
        var r1 = await serviceA.CreateAsync(new CreateEmployeeRequest
        {
            FirstName = "A1", LastName = "Test", Email = "a1@test.com",
            DateOfJoining = DateTime.UtcNow.Date,
            DepartmentId = deptA, JobTitleId = jtA, EmploymentType = EmploymentType.FullTime,
        });
        r1.Value!.EmployeeNo.Should().Be("EMP-0001");

        // Create employees in tenant B - should also start at EMP-0001
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);
        var deptB = await SeedDepartment("Eng", "ENG", tenantB);
        var jtB = await SeedJobTitle("Eng", tenantB);
        await SeedTenant(tenantB);

        var dbB = TestDbContextFactory.Create(ctxB, _dbName);
        var serviceB = new EmployeeService(dbB, ctxB, _currentUser, _fileStorage, _virusScanner, _logger);
        var r2 = await serviceB.CreateAsync(new CreateEmployeeRequest
        {
            FirstName = "B1", LastName = "Test", Email = "b1@test.com",
            DateOfJoining = DateTime.UtcNow.Date,
            DepartmentId = deptB, JobTitleId = jtB, EmploymentType = EmploymentType.FullTime,
        });

        // Each tenant has its own sequence
        r2.Value!.EmployeeNo.Should().Be("EMP-0001");
    }

    // ── AC-5 / FR-5: Plan limit enforcement ────────────────────────

    [Fact]
    public async Task Create_PlanLimitReached_ShouldBlock()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId, maxEmployees: 2);
        var service = CreateService();

        // Create 2 employees (at limit)
        var r1 = await service.CreateAsync(MakeRequest(deptId, jtId, "e1@test.com"));
        r1.IsSuccess.Should().BeTrue();

        service = CreateService();
        var r2 = await service.CreateAsync(MakeRequest(deptId, jtId, "e2@test.com"));
        r2.IsSuccess.Should().BeTrue();

        // 3rd should be blocked
        service = CreateService();
        var r3 = await service.CreateAsync(MakeRequest(deptId, jtId, "e3@test.com"));

        r3.IsFailure.Should().BeTrue();
        r3.Error.Should().Contain("Employee limit reached");
        r3.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Create_NoMaxEmployeesSet_ShouldSucceed()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId, maxEmployees: null); // no limit
        var service = CreateService();

        var result = await service.CreateAsync(MakeRequest(deptId, jtId));

        result.IsSuccess.Should().BeTrue();
    }

    // ── AC-4 / FR-6: Profile photo upload ──────────────────────────

    [Fact]
    public async Task UploadPhoto_ValidJpeg_ShouldSucceed()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId);
        var service = CreateService();

        var createResult = await service.CreateAsync(MakeRequest(deptId, jtId));
        createResult.IsSuccess.Should().BeTrue();

        service = CreateService();
        using var stream = new MemoryStream(new byte[1024]); // 1KB test file
        var result = await service.UploadProfilePhotoAsync(
            createResult.Value!.Id, stream, "photo.jpg", "image/jpeg", 1024);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("profile");
    }

    [Fact]
    public async Task UploadPhoto_InvalidMimeType_ShouldFail()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId);
        var service = CreateService();

        var createResult = await service.CreateAsync(MakeRequest(deptId, jtId));
        service = CreateService();
        using var stream = new MemoryStream(new byte[1024]);
        var result = await service.UploadProfilePhotoAsync(
            createResult.Value!.Id, stream, "malware.exe", "application/x-msdownload", 1024);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Invalid file type");
    }

    [Fact]
    public async Task UploadPhoto_ExceedsMaxSize_ShouldFail()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId);
        var service = CreateService();

        var createResult = await service.CreateAsync(MakeRequest(deptId, jtId));
        service = CreateService();
        using var stream = new MemoryStream(new byte[1024]);
        var result = await service.UploadProfilePhotoAsync(
            createResult.Value!.Id, stream, "large.jpg", "image/jpeg",
            6 * 1024 * 1024); // 6 MB, exceeds 5 MB limit

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("exceeds the maximum allowed size");
    }

    [Fact]
    public async Task UploadPhoto_ShouldInvokeVirusScanner()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId);
        var service = CreateService();

        var createResult = await service.CreateAsync(MakeRequest(deptId, jtId));
        service = CreateService();
        using var stream = new MemoryStream(new byte[1024]);
        await service.UploadProfilePhotoAsync(
            createResult.Value!.Id, stream, "photo.png", "image/png", 1024);

        // Verify that the virus scanner was called
        await _virusScanner.Received(1).ScanAsync(
            Arg.Any<Stream>(), Arg.Is("photo.png"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadPhoto_InfectedFile_ShouldReject()
    {
        _virusScanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(VirusScanResult.Infected("Eicar-Test-Signature"));

        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId);
        var service = CreateService();

        var createResult = await service.CreateAsync(MakeRequest(deptId, jtId));
        service = CreateService();
        using var stream = new MemoryStream(new byte[1024]);
        var result = await service.UploadProfilePhotoAsync(
            createResult.Value!.Id, stream, "infected.jpg", "image/jpeg", 1024);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("malware scanner");
        result.Error.Should().Contain("Eicar-Test-Signature");
    }

    // ── AC-6: Custom fields JSONB persistence ──────────────────────

    [Fact]
    public async Task Create_WithCustomFields_ShouldPersist()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId);
        var service = CreateService();

        var customJson = """{"shirt_size": "L", "dietary_pref": "vegetarian", "parking_spot": "A12"}""";
        var request = MakeRequest(deptId, jtId) with { CustomFields = customJson };
        var result = await service.CreateAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CustomFields.Should().Be(customJson);

        // Verify in DB
        using var db = CreateDbContext();
        var emp = db.Employees.First(e => e.Id == result.Value.Id);
        emp.CustomFields.Should().Be(customJson);
    }

    // ── Cross-tenant isolation (NFR-2) ─────────────────────────────

    [Fact]
    public async Task GetAll_ShouldOnlyReturnCurrentTenantEmployees()
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
        var serviceA = new EmployeeService(dbA, ctxA, _currentUser, _fileStorage, _virusScanner, _logger);
        await serviceA.CreateAsync(new CreateEmployeeRequest
        {
            FirstName = "Tenant A", LastName = "Employee", Email = "a@test.com",
            DateOfJoining = DateTime.UtcNow.Date,
            DepartmentId = deptA, JobTitleId = jtA, EmploymentType = EmploymentType.FullTime,
        });

        // Seed in tenant B
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);
        var deptB = await SeedDepartment("Eng B", "ENGB", tenantB);
        var jtB = await SeedJobTitle("Eng B", tenantB);
        await SeedTenant(tenantB);

        var dbB = TestDbContextFactory.Create(ctxB, _dbName);
        var serviceB = new EmployeeService(dbB, ctxB, _currentUser, _fileStorage, _virusScanner, _logger);
        await serviceB.CreateAsync(new CreateEmployeeRequest
        {
            FirstName = "Tenant B", LastName = "Employee", Email = "b@test.com",
            DateOfJoining = DateTime.UtcNow.Date,
            DepartmentId = deptB, JobTitleId = jtB, EmploymentType = EmploymentType.FullTime,
        });

        // Query from tenant A
        var queryServiceA = new EmployeeService(
            TestDbContextFactory.Create(ctxA, _dbName), ctxA, _currentUser, _fileStorage, _virusScanner, _logger);
        var resultA = await queryServiceA.GetAllAsync();

        resultA.IsSuccess.Should().BeTrue();
        resultA.Value!.Items.Should().HaveCount(1);
        resultA.Value.Items[0].FirstName.Should().Be("Tenant A");

        // Query from tenant B
        var queryServiceB = new EmployeeService(
            TestDbContextFactory.Create(ctxB, _dbName), ctxB, _currentUser, _fileStorage, _virusScanner, _logger);
        var resultB = await queryServiceB.GetAllAsync();

        resultB.IsSuccess.Should().BeTrue();
        resultB.Value!.Items.Should().HaveCount(1);
        resultB.Value.Items[0].FirstName.Should().Be("Tenant B");
    }

    [Fact]
    public async Task GetById_CrossTenant_ShouldReturn404()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var ctxA = Substitute.For<ITenantContext>();
        ctxA.TenantId.Returns(tenantA);
        ctxA.IsResolved.Returns(true);
        var deptA = await SeedDepartment("Eng A", "ENGA", tenantA);
        var jtA = await SeedJobTitle("Eng A", tenantA);
        await SeedTenant(tenantA);

        var dbA = TestDbContextFactory.Create(ctxA, _dbName);
        var serviceA = new EmployeeService(dbA, ctxA, _currentUser, _fileStorage, _virusScanner, _logger);
        var createResult = await serviceA.CreateAsync(new CreateEmployeeRequest
        {
            FirstName = "Secret", LastName = "Employee", Email = "secret@test.com",
            DateOfJoining = DateTime.UtcNow.Date,
            DepartmentId = deptA, JobTitleId = jtA, EmploymentType = EmploymentType.FullTime,
        });

        // Try to access from tenant B
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);
        var serviceB = new EmployeeService(
            TestDbContextFactory.Create(ctxB, _dbName), ctxB, _currentUser, _fileStorage, _virusScanner, _logger);

        var result = await serviceB.GetByIdAsync(createResult.Value!.Id);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── Tenant context not resolved ────────────────────────────────

    [Fact]
    public async Task Create_TenantNotResolved_ShouldFail()
    {
        _tenantContext.IsResolved.Returns(false);
        var service = CreateService();

        var result = await service.CreateAsync(MakeRequest(Guid.NewGuid(), Guid.NewGuid()));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Tenant context is not resolved");
    }

    // ── Department/JobTitle validation ──────────────────────────────

    [Fact]
    public async Task Create_NonExistentDepartment_ShouldFail()
    {
        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId);
        var service = CreateService();

        var result = await service.CreateAsync(MakeRequest(Guid.NewGuid(), jtId));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Department not found");
    }

    [Fact]
    public async Task Create_NonExistentJobTitle_ShouldFail()
    {
        var deptId = await SeedDepartment();
        await SeedTenant(_tenantId);
        var service = CreateService();

        var result = await service.CreateAsync(MakeRequest(deptId, Guid.NewGuid()));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Job title not found");
    }

    [Fact]
    public async Task Create_InactiveDepartment_ShouldFail()
    {
        using var db = CreateDbContext();
        var inactiveDept = new Department
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            Name = "Inactive Dept",
            Code = "INACT",
            IsActive = false,
            IsDeleted = false,
        };
        db.Departments.Add(inactiveDept);
        await db.SaveChangesAsync();

        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId);
        var service = CreateService();

        var result = await service.CreateAsync(MakeRequest(inactiveDept.Id, jtId));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Department not found or is not active");
    }

    // ── Deferred FK: Department.manager_id -> Employee (US-CHR-001) ──

    [Fact]
    public async Task DepartmentDeactivate_WithActiveEmployees_ShouldBlock()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId);

        // Create an employee in this department
        var empService = CreateService();
        var createResult = await empService.CreateAsync(MakeRequest(deptId, jtId));
        createResult.IsSuccess.Should().BeTrue();

        // Try to deactivate the department
        var deptLogger = Substitute.For<ILogger<DepartmentService>>();
        var deptDbContext = TestDbContextFactory.Create(_tenantContext, _dbName);
        var deptService = new DepartmentService(deptDbContext, _tenantContext, _currentUser, deptLogger);

        var result = await deptService.DeactivateAsync(deptId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("active employee(s) assigned");
    }

    // ── Deferred: JobTitle real employee count + deactivation block ──

    [Fact]
    public async Task JobTitleGetById_ShouldReturnRealEmployeeCount()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId);

        // Create 2 employees with this job title
        var empService = CreateService();
        await empService.CreateAsync(MakeRequest(deptId, jtId, "emp1@test.com"));
        empService = CreateService();
        await empService.CreateAsync(MakeRequest(deptId, jtId, "emp2@test.com"));

        // Check job title employee count
        var jtLogger = Substitute.For<ILogger<JobTitleService>>();
        var jtDbContext = TestDbContextFactory.Create(_tenantContext, _dbName);
        var jtService = new JobTitleService(jtDbContext, _tenantContext, _currentUser, jtLogger);

        var result = await jtService.GetByIdAsync(jtId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.EmployeeCount.Should().Be(2);
    }

    [Fact]
    public async Task JobTitleDeactivate_WithActiveEmployees_ShouldBlock()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId);

        // Create an employee with this job title
        var empService = CreateService();
        await empService.CreateAsync(MakeRequest(deptId, jtId));

        // Try to deactivate the job title
        var jtLogger = Substitute.For<ILogger<JobTitleService>>();
        var jtDbContext = TestDbContextFactory.Create(_tenantContext, _dbName);
        var jtService = new JobTitleService(jtDbContext, _tenantContext, _currentUser, jtLogger);

        var result = await jtService.DeactivateAsync(jtId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("active employee(s)");
        result.Error.Should().Contain("Reassign them before deactivating");
    }

    [Fact]
    public async Task JobTitleDeactivate_NoEmployees_ShouldSucceed()
    {
        var jtId = await SeedJobTitle("Unused Title");

        var jtLogger = Substitute.For<ILogger<JobTitleService>>();
        var jtDbContext = TestDbContextFactory.Create(_tenantContext, _dbName);
        var jtService = new JobTitleService(jtDbContext, _tenantContext, _currentUser, jtLogger);

        var result = await jtService.DeactivateAsync(jtId);

        result.IsSuccess.Should().BeTrue();
    }

    // ── GetAll with pagination ──────────────────────────────────────

    [Fact]
    public async Task GetAll_WithPagination_ShouldReturnPagedResults()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId);

        // Create 5 employees
        for (int i = 1; i <= 5; i++)
        {
            var svc = CreateService();
            await svc.CreateAsync(MakeRequest(deptId, jtId, $"emp{i}@test.com"));
        }

        var service = CreateService();
        var result = await service.GetAllAsync(page: 1, pageSize: 2);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(2);
        result.Value.TotalCount.Should().Be(5);
        result.Value.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(2);
    }

    // ── GetById ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_ExistingEmployee_ShouldReturnWithNavProperties()
    {
        var deptId = await SeedDepartment();
        var jtId = await SeedJobTitle();
        await SeedTenant(_tenantId);
        var service = CreateService();

        var createResult = await service.CreateAsync(MakeRequest(deptId, jtId));
        service = CreateService();
        var result = await service.GetByIdAsync(createResult.Value!.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DepartmentName.Should().Be("Engineering");
        result.Value.JobTitleName.Should().Be("Software Engineer");
    }

    [Fact]
    public async Task GetById_NonExistent_ShouldReturn404()
    {
        var service = CreateService();

        var result = await service.GetByIdAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    public void Dispose()
    {
        // InMemory databases are cleaned up when the last connection closes
    }
}
