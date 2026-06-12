// ============================================================================
// US-CHR-007: Location Management Unit Tests
// Tests location CRUD, name uniqueness (FR-2, BR-1), deactivation guard with
// active employees (AC-3, FR-5), cross-tenant isolation (NFR-2, FR-8),
// time zone validation, and employee count per location (FR-7).
// Uses EF Core InMemory provider for lightweight database testing.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class LocationServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<LocationService> _logger;

    public LocationServiceTests()
    {
        _dbName = Guid.NewGuid().ToString();
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("admin@test.com");
        _currentUser.UserId.Returns(Guid.NewGuid());

        _logger = Substitute.For<ILogger<LocationService>>();
    }

    private LocationService CreateService()
    {
        var dbContext = TestDbContextFactory.Create(_tenantContext, _dbName);
        return new LocationService(dbContext, _tenantContext, _currentUser, _logger);
    }

    private Infrastructure.Persistence.AppDbContext CreateDbContext()
    {
        return TestDbContextFactory.Create(_tenantContext, _dbName);
    }

    private async Task<Guid> SeedLocation(
        string name, string timeZone = "Asia/Colombo",
        bool isActive = true, Guid? tenantId = null)
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
        var location = new Location
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tid,
            Name = name,
            TimeZone = timeZone,
            IsActive = isActive,
            IsDeleted = false,
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        return location.Id;
    }

    private async Task SeedEmployeeAtLocation(Guid locationId, bool isActive = true, Guid? tenantId = null)
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

        // Seed a department and job title for the employee (required FKs)
        var deptId = BaseEntity.NewUuidV7();
        var jtId = BaseEntity.NewUuidV7();

        if (!db.Departments.Any(d => d.TenantId == tid))
        {
            db.Departments.Add(new Department
            {
                Id = deptId,
                TenantId = tid,
                Name = "Test Dept",
                Code = "TD",
                IsActive = true,
            });
            db.JobTitles.Add(new JobTitle
            {
                Id = jtId,
                TenantId = tid,
                TitleName = "Test Title",
                IsActive = true,
            });
            await db.SaveChangesAsync();
        }
        else
        {
            deptId = db.Departments.First(d => d.TenantId == tid).Id;
            jtId = db.JobTitles.First(j => j.TenantId == tid).Id;
        }

        db.Employees.Add(new Employee
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tid,
            EmployeeNo = $"EMP-{Random.Shared.Next(1000, 9999)}",
            FirstName = "Test",
            LastName = "Employee",
            Email = $"test{Guid.NewGuid():N}@test.com",
            DateOfJoining = DateTime.UtcNow,
            DepartmentId = deptId,
            JobTitleId = jtId,
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            LocationId = locationId,
            IsActive = isActive,
        });
        await db.SaveChangesAsync();
    }

    // -- AC-2: Create location --

    [Fact]
    public async Task Create_ValidLocation_ShouldSucceed()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            "Head Office", "123 Main St", "Suite 100",
            "Colombo", "Western", "Sri Lanka", "00100",
            "Asia/Colombo", "+94112345678");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Head Office");
        result.Value.AddressLine1.Should().Be("123 Main St");
        result.Value.AddressLine2.Should().Be("Suite 100");
        result.Value.City.Should().Be("Colombo");
        result.Value.StateProvince.Should().Be("Western");
        result.Value.Country.Should().Be("Sri Lanka");
        result.Value.PostalCode.Should().Be("00100");
        result.Value.TimeZone.Should().Be("Asia/Colombo");
        result.Value.Phone.Should().Be("+94112345678");
        result.Value.IsActive.Should().BeTrue();
        result.Value.EmployeeCount.Should().Be(0);
    }

    [Fact]
    public async Task Create_MinimalFields_ShouldSucceed()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            "Remote Office", null, null, null, null, null, null,
            "UTC", null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Remote Office");
        result.Value.TimeZone.Should().Be("UTC");
    }

    // -- FR-2 / BR-1: Duplicate name rejection --

    [Fact]
    public async Task Create_DuplicateNameSameTenant_ShouldFail()
    {
        await SeedLocation("Head Office");
        var service = CreateService();

        var result = await service.CreateAsync(
            "Head Office", null, null, null, null, null, null,
            "UTC", null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("A location with this name already exists.");
    }

    // -- BR-1: Same name allowed cross-tenant --

    [Fact]
    public async Task Create_SameNameDifferentTenant_ShouldSucceed()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Seed in tenant A
        var tenantAContext = Substitute.For<ITenantContext>();
        tenantAContext.TenantId.Returns(tenantA);
        tenantAContext.IsResolved.Returns(true);
        var dbA = TestDbContextFactory.Create(tenantAContext, _dbName);
        dbA.Locations.Add(new Location
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantA,
            Name = "Head Office",
            TimeZone = "UTC",
            IsActive = true,
        });
        await dbA.SaveChangesAsync();

        // Act: create same name in tenant B
        var tenantBContext = Substitute.For<ITenantContext>();
        tenantBContext.TenantId.Returns(tenantB);
        tenantBContext.IsResolved.Returns(true);
        var dbB = TestDbContextFactory.Create(tenantBContext, _dbName);
        var serviceB = new LocationService(
            dbB, tenantBContext, _currentUser, _logger);

        var result = await serviceB.CreateAsync(
            "Head Office", null, null, null, null, null, null,
            "UTC", null);

        result.IsSuccess.Should().BeTrue();
    }

    // -- Edit / Update --

    [Fact]
    public async Task Update_ChangeName_ShouldSucceed()
    {
        var id = await SeedLocation("Old Location");
        var service = CreateService();

        var result = await service.UpdateAsync(
            id, "New Location", "456 New St", null,
            "Kandy", "Central", "Sri Lanka", "20000",
            "Asia/Colombo", null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("New Location");
        result.Value.AddressLine1.Should().Be("456 New St");
        result.Value.City.Should().Be("Kandy");
    }

    [Fact]
    public async Task Update_SameNameAsSelf_ShouldSucceed()
    {
        var id = await SeedLocation("Head Office");
        var service = CreateService();

        var result = await service.UpdateAsync(
            id, "Head Office", null, null, null, null, null, null,
            "Asia/Colombo", null);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Update_DuplicateNameExcludingSelf_ShouldFail()
    {
        await SeedLocation("Head Office");
        var id2 = await SeedLocation("Branch Office");
        var service = CreateService();

        var result = await service.UpdateAsync(
            id2, "Head Office", null, null, null, null, null, null,
            "UTC", null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("A location with this name already exists.");
    }

    [Fact]
    public async Task Update_NonExistentLocation_ShouldFail()
    {
        var service = CreateService();

        var result = await service.UpdateAsync(
            Guid.NewGuid(), "Ghost", null, null, null, null, null, null,
            "UTC", null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Update_ChangeTimeZone_ShouldSucceed()
    {
        var id = await SeedLocation("NYC Office", "America/New_York");
        var service = CreateService();

        var result = await service.UpdateAsync(
            id, "NYC Office", null, null, null, null, null, null,
            "America/Chicago", null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TimeZone.Should().Be("America/Chicago");
    }

    // -- Deactivate --

    [Fact]
    public async Task Deactivate_ActiveLocation_ShouldSucceed()
    {
        var id = await SeedLocation("Old Branch");
        var service = CreateService();

        var result = await service.DeactivateAsync(id);

        result.IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        var loc = db.Locations.FirstOrDefault(l => l.Id == id);
        loc.Should().NotBeNull();
        loc!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Deactivate_AlreadyDeactivated_ShouldFail()
    {
        var id = await SeedLocation("Closed Branch", isActive: false);
        var service = CreateService();

        var result = await service.DeactivateAsync(id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("already deactivated");
    }

    [Fact]
    public async Task Deactivate_NonExistentLocation_ShouldFail()
    {
        var service = CreateService();

        var result = await service.DeactivateAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // -- AC-3 / FR-5: Deactivation guard with active employees --

    [Fact]
    public async Task Deactivate_WithActiveEmployees_ShouldFail()
    {
        var locationId = await SeedLocation("Busy Office");
        await SeedEmployeeAtLocation(locationId, isActive: true);
        await SeedEmployeeAtLocation(locationId, isActive: true);

        var service = CreateService();

        var result = await service.DeactivateAsync(locationId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("2 active employee(s)");
        result.Error.Should().Contain("Reassign them before deactivating");
    }

    [Fact]
    public async Task Deactivate_WithOnlyInactiveEmployees_ShouldSucceed()
    {
        var locationId = await SeedLocation("Quiet Office");
        await SeedEmployeeAtLocation(locationId, isActive: false);

        var service = CreateService();

        var result = await service.DeactivateAsync(locationId);

        result.IsSuccess.Should().BeTrue();
    }

    // -- FR-7: Employee count per location --

    [Fact]
    public async Task GetById_ShouldReturnEmployeeCount()
    {
        var locationId = await SeedLocation("Office with Staff");
        await SeedEmployeeAtLocation(locationId, isActive: true);
        await SeedEmployeeAtLocation(locationId, isActive: true);
        await SeedEmployeeAtLocation(locationId, isActive: false); // inactive, should not count

        var service = CreateService();

        var result = await service.GetByIdAsync(locationId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.EmployeeCount.Should().Be(2);
    }

    [Fact]
    public async Task GetAll_ShouldReturnEmployeeCounts()
    {
        var loc1 = await SeedLocation("Office A");
        var loc2 = await SeedLocation("Office B");
        await SeedEmployeeAtLocation(loc1, isActive: true);
        await SeedEmployeeAtLocation(loc1, isActive: true);
        await SeedEmployeeAtLocation(loc2, isActive: true);

        var service = CreateService();

        var result = await service.GetAllAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
        result.Value!.First(l => l.Name == "Office A").EmployeeCount.Should().Be(2);
        result.Value!.First(l => l.Name == "Office B").EmployeeCount.Should().Be(1);
    }

    // -- Cross-tenant isolation (NFR-2, FR-8) --

    [Fact]
    public async Task GetAll_ShouldOnlyReturnCurrentTenantLocations()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Seed in tenant A
        var ctxA = Substitute.For<ITenantContext>();
        ctxA.TenantId.Returns(tenantA);
        ctxA.IsResolved.Returns(true);
        var dbA = TestDbContextFactory.Create(ctxA, _dbName);
        dbA.Locations.Add(new Location
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantA,
            Name = "Tenant A Office",
            TimeZone = "UTC",
            IsActive = true,
        });
        await dbA.SaveChangesAsync();

        // Seed in tenant B
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);
        var dbB = TestDbContextFactory.Create(ctxB, _dbName);
        dbB.Locations.Add(new Location
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantB,
            Name = "Tenant B Office",
            TimeZone = "UTC",
            IsActive = true,
        });
        await dbB.SaveChangesAsync();

        // Query from tenant A
        var serviceA = new LocationService(
            TestDbContextFactory.Create(ctxA, _dbName), ctxA, _currentUser, _logger);
        var resultA = await serviceA.GetAllAsync();

        resultA.IsSuccess.Should().BeTrue();
        resultA.Value!.Should().HaveCount(1);
        resultA.Value[0].Name.Should().Be("Tenant A Office");

        // Query from tenant B
        var serviceB = new LocationService(
            TestDbContextFactory.Create(ctxB, _dbName), ctxB, _currentUser, _logger);
        var resultB = await serviceB.GetAllAsync();

        resultB.IsSuccess.Should().BeTrue();
        resultB.Value!.Should().HaveCount(1);
        resultB.Value[0].Name.Should().Be("Tenant B Office");
    }

    [Fact]
    public async Task GetById_CrossTenant_ShouldReturn404()
    {
        var tenantA = Guid.NewGuid();
        var ctxA = Substitute.For<ITenantContext>();
        ctxA.TenantId.Returns(tenantA);
        ctxA.IsResolved.Returns(true);
        var dbA = TestDbContextFactory.Create(ctxA, _dbName);
        var locA = new Location
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantA,
            Name = "Secret Office",
            TimeZone = "UTC",
            IsActive = true,
        };
        dbA.Locations.Add(locA);
        await dbA.SaveChangesAsync();

        var tenantB = Guid.NewGuid();
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);
        var serviceB = new LocationService(
            TestDbContextFactory.Create(ctxB, _dbName), ctxB, _currentUser, _logger);

        var result = await serviceB.GetByIdAsync(locA.Id);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // -- GetAll with filter --

    [Fact]
    public async Task GetAll_ActiveOnly_ShouldFilterInactive()
    {
        await SeedLocation("Active Office");
        await SeedLocation("Inactive Office", isActive: false);
        var service = CreateService();

        var result = await service.GetAllAsync(activeOnly: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(1);
        result.Value[0].Name.Should().Be("Active Office");
    }

    [Fact]
    public async Task GetAll_NoFilter_ShouldReturnAll()
    {
        await SeedLocation("Active Office");
        await SeedLocation("Inactive Office", isActive: false);
        var service = CreateService();

        var result = await service.GetAllAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
    }

    // -- Tenant context not resolved --

    [Fact]
    public async Task Create_TenantNotResolved_ShouldFail()
    {
        _tenantContext.IsResolved.Returns(false);
        var service = CreateService();

        var result = await service.CreateAsync(
            "Office", null, null, null, null, null, null, "UTC", null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Tenant context is not resolved");
    }

    [Fact]
    public async Task GetAll_TenantNotResolved_ShouldFail()
    {
        _tenantContext.IsResolved.Returns(false);
        var service = CreateService();

        var result = await service.GetAllAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Tenant context is not resolved");
    }

    [Fact]
    public async Task Update_TenantNotResolved_ShouldFail()
    {
        _tenantContext.IsResolved.Returns(false);
        var service = CreateService();

        var result = await service.UpdateAsync(
            Guid.NewGuid(), "Office", null, null, null, null, null, null,
            "UTC", null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Tenant context is not resolved");
    }

    [Fact]
    public async Task Deactivate_TenantNotResolved_ShouldFail()
    {
        _tenantContext.IsResolved.Returns(false);
        var service = CreateService();

        var result = await service.DeactivateAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Tenant context is not resolved");
    }

    [Fact]
    public async Task GetById_TenantNotResolved_ShouldFail()
    {
        _tenantContext.IsResolved.Returns(false);
        var service = CreateService();

        var result = await service.GetByIdAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Tenant context is not resolved");
    }

    // -- Time zone validation (tested at validator level, but service stores correctly) --

    [Fact]
    public async Task Create_WithAmericaNewYork_ShouldStoreCorrectly()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            "NYC Office", null, null, "New York", "NY", "USA", "10001",
            "America/New_York", null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TimeZone.Should().Be("America/New_York");
    }

    public void Dispose()
    {
        // InMemory databases are cleaned up when the last connection closes
    }
}
