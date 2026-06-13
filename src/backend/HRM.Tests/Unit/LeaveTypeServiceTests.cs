// ============================================================================
// US-LV-001: Leave Type Configuration Unit Tests
// Tests leave type CRUD, case-insensitive name uniqueness (BR-1), zero
// entitlement for unpaid leave (BR-3), deactivate/reactivate (AC-4, FR-5),
// reorder (FR-3), tenant isolation (NFR-2), validation (color/gender/entitlement),
// and default seeding (FR-4).
// Uses EF Core InMemory provider for lightweight database testing.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.LeaveTypes.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class LeaveTypeServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<LeaveTypeService> _logger;

    public LeaveTypeServiceTests()
    {
        _dbName = Guid.NewGuid().ToString();
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("admin@test.com");
        _currentUser.UserId.Returns(Guid.NewGuid());

        _logger = Substitute.For<ILogger<LeaveTypeService>>();
    }

    private LeaveTypeService CreateService()
    {
        var dbContext = TestDbContextFactory.Create(_tenantContext, _dbName);
        return new LeaveTypeService(dbContext, _tenantContext, _currentUser, _logger);
    }

    private Infrastructure.Persistence.AppDbContext CreateDbContext()
    {
        return TestDbContextFactory.Create(_tenantContext, _dbName);
    }

    private CreateLeaveTypeRequest MakeCreateRequest(
        string name = "Annual Leave",
        decimal entitlement = 14,
        string accrualFrequency = "Monthly",
        string gender = "All",
        string? color = "#4CAF50",
        int? displayOrder = null) => new()
    {
        Name = name,
        Code = "AL",
        Color = color,
        Description = "Test leave type",
        AnnualEntitlement = entitlement,
        AccrualFrequency = accrualFrequency,
        ProbationEligible = false,
        DocumentsRequired = false,
        Encashable = false,
        HalfDayAllowed = true,
        HourlyAllowed = false,
        Gender = gender,
        NegativeBalanceAllowed = false,
        DisplayOrder = displayOrder,
    };

    private async Task<Guid> SeedLeaveType(
        string name, decimal entitlement = 14, bool isActive = true,
        int displayOrder = 1, Guid? tenantId = null)
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
        var leaveType = new LeaveType
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tid,
            Name = name,
            Code = "TST",
            AnnualEntitlement = entitlement,
            AccrualFrequency = AccrualFrequency.Upfront,
            Gender = LeaveTypeGender.All,
            DisplayOrder = displayOrder,
            IsActive = isActive,
            IsDeleted = false,
        };
        db.LeaveTypes.Add(leaveType);
        await db.SaveChangesAsync();
        return leaveType.Id;
    }

    // -----------------------------------------------------------------------
    // AC-1: Create leave type with full config
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Create_FullConfig_ShouldSucceed()
    {
        var service = CreateService();
        var request = new CreateLeaveTypeRequest
        {
            Name = "Annual Leave",
            Code = "AL",
            Color = "#4CAF50",
            Description = "Paid annual leave",
            AnnualEntitlement = 14,
            AccrualFrequency = "Monthly",
            CarryForwardLimit = 5,
            CarryForwardExpiryMonths = 3,
            ProbationEligible = false,
            DocumentsRequired = true,
            DocumentDayThreshold = 2,
            Encashable = true,
            MaxEncashDays = 5,
            HalfDayAllowed = true,
            HourlyAllowed = false,
            Gender = "All",
            MaxConsecutiveDays = 14,
            NegativeBalanceAllowed = true,
            NegativeBalanceLimit = 5,
            DisplayOrder = 1,
        };

        var result = await service.CreateAsync(request);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.Name.Should().Be("Annual Leave");
        dto.Code.Should().Be("AL");
        dto.Color.Should().Be("#4CAF50");
        dto.Description.Should().Be("Paid annual leave");
        dto.AnnualEntitlement.Should().Be(14);
        dto.AccrualFrequency.Should().Be("Monthly");
        dto.CarryForwardLimit.Should().Be(5);
        dto.CarryForwardExpiryMonths.Should().Be(3);
        dto.ProbationEligible.Should().BeFalse();
        dto.DocumentsRequired.Should().BeTrue();
        dto.DocumentDayThreshold.Should().Be(2);
        dto.Encashable.Should().BeTrue();
        dto.MaxEncashDays.Should().Be(5);
        dto.HalfDayAllowed.Should().BeTrue();
        dto.HourlyAllowed.Should().BeFalse();
        dto.Gender.Should().Be("All");
        dto.MaxConsecutiveDays.Should().Be(14);
        dto.NegativeBalanceAllowed.Should().BeTrue();
        dto.NegativeBalanceLimit.Should().Be(5);
        dto.DisplayOrder.Should().Be(1);
        dto.IsActive.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // AC-3 / BR-1: Duplicate name (case-insensitive) rejected
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Create_DuplicateNameSameTenant_ShouldFail()
    {
        await SeedLeaveType("Annual Leave");
        var service = CreateService();

        var result = await service.CreateAsync(MakeCreateRequest("Annual Leave"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("A leave type with this name already exists.");
    }

    [Fact]
    public async Task Create_DuplicateNameCaseInsensitive_ShouldFail()
    {
        await SeedLeaveType("Annual Leave");
        var service = CreateService();

        var result = await service.CreateAsync(MakeCreateRequest("annual leave"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("A leave type with this name already exists.");
    }

    [Fact]
    public async Task Create_DuplicateNameMixedCase_ShouldFail()
    {
        await SeedLeaveType("Annual Leave");
        var service = CreateService();

        var result = await service.CreateAsync(MakeCreateRequest("ANNUAL LEAVE"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("A leave type with this name already exists.");
    }

    // -----------------------------------------------------------------------
    // BR-3: Zero entitlement allowed (for unpaid leave)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Create_ZeroEntitlement_ShouldSucceed()
    {
        var service = CreateService();

        var result = await service.CreateAsync(MakeCreateRequest("Unpaid Leave", entitlement: 0));

        result.IsSuccess.Should().BeTrue();
        result.Value!.AnnualEntitlement.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // AC-4 / FR-5: Deactivate hides but retains
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Deactivate_ActiveLeaveType_ShouldSucceed()
    {
        var id = await SeedLeaveType("Annual Leave");
        var service = CreateService();

        var result = await service.DeactivateAsync(id);

        result.IsSuccess.Should().BeTrue();

        // Verify it's deactivated in the database
        using var db = CreateDbContext();
        var lt = db.LeaveTypes.FirstOrDefault(x => x.Id == id);
        lt.Should().NotBeNull();
        lt!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Deactivate_AlreadyDeactivated_ShouldFail()
    {
        var id = await SeedLeaveType("Old Leave", isActive: false);
        var service = CreateService();

        var result = await service.DeactivateAsync(id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("already deactivated");
    }

    [Fact]
    public async Task Deactivate_NonExistent_ShouldReturn404()
    {
        var service = CreateService();

        var result = await service.DeactivateAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Deactivate_ShouldHideFromActiveOnlyList()
    {
        var id = await SeedLeaveType("Will Deactivate");
        await SeedLeaveType("Stays Active", displayOrder: 2);
        var service = CreateService();

        await service.DeactivateAsync(id);

        // Re-create service (fresh DbContext) to pick up changes
        var service2 = CreateService();
        var result = await service2.GetAllAsync(activeOnly: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(1);
        result.Value![0].Name.Should().Be("Stays Active");
    }

    [Fact]
    public async Task Deactivate_ShouldStillAppearInFullList()
    {
        var id = await SeedLeaveType("Will Deactivate");
        await SeedLeaveType("Stays Active", displayOrder: 2);
        var service = CreateService();

        await service.DeactivateAsync(id);

        var service2 = CreateService();
        var result = await service2.GetAllAsync(activeOnly: false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Reactivate
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Reactivate_InactiveLeaveType_ShouldSucceed()
    {
        var id = await SeedLeaveType("Deactivated Leave", isActive: false);
        var service = CreateService();

        var result = await service.ReactivateAsync(id);

        result.IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        var lt = db.LeaveTypes.FirstOrDefault(x => x.Id == id);
        lt.Should().NotBeNull();
        lt!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Reactivate_AlreadyActive_ShouldFail()
    {
        var id = await SeedLeaveType("Active Leave");
        var service = CreateService();

        var result = await service.ReactivateAsync(id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("already active");
    }

    // -----------------------------------------------------------------------
    // FR-3: Reorder
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Reorder_ShouldUpdateDisplayOrder()
    {
        var id1 = await SeedLeaveType("First", displayOrder: 1);
        var id2 = await SeedLeaveType("Second", displayOrder: 2);
        var id3 = await SeedLeaveType("Third", displayOrder: 3);
        var service = CreateService();

        // Reverse the order
        var result = await service.ReorderAsync(new List<Guid> { id3, id1, id2 });

        result.IsSuccess.Should().BeTrue();

        var service2 = CreateService();
        var listResult = await service2.GetAllAsync();
        listResult.IsSuccess.Should().BeTrue();
        listResult.Value![0].Name.Should().Be("Third");
        listResult.Value![1].Name.Should().Be("First");
        listResult.Value![2].Name.Should().Be("Second");
    }

    [Fact]
    public async Task Reorder_WrongCount_ShouldFail()
    {
        var id1 = await SeedLeaveType("First");
        await SeedLeaveType("Second", displayOrder: 2);
        var service = CreateService();

        var result = await service.ReorderAsync(new List<Guid> { id1 });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("number of leave type IDs must match");
    }

    [Fact]
    public async Task Reorder_UnknownId_ShouldFail()
    {
        var id1 = await SeedLeaveType("First");
        await SeedLeaveType("Second", displayOrder: 2);
        var service = CreateService();

        var result = await service.ReorderAsync(new List<Guid> { id1, Guid.NewGuid() });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not found");
    }

    // -----------------------------------------------------------------------
    // Tenant isolation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAll_ShouldOnlyReturnCurrentTenantLeaveTypes()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await SeedLeaveType("Tenant A Leave", tenantId: tenantA);
        await SeedLeaveType("Tenant B Leave", tenantId: tenantB);

        var ctxA = Substitute.For<ITenantContext>();
        ctxA.TenantId.Returns(tenantA);
        ctxA.IsResolved.Returns(true);
        var serviceA = new LeaveTypeService(
            TestDbContextFactory.Create(ctxA, _dbName), ctxA, _currentUser, _logger);

        var resultA = await serviceA.GetAllAsync();
        resultA.IsSuccess.Should().BeTrue();
        resultA.Value!.Should().HaveCount(1);
        resultA.Value![0].Name.Should().Be("Tenant A Leave");

        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);
        var serviceB = new LeaveTypeService(
            TestDbContextFactory.Create(ctxB, _dbName), ctxB, _currentUser, _logger);

        var resultB = await serviceB.GetAllAsync();
        resultB.IsSuccess.Should().BeTrue();
        resultB.Value!.Should().HaveCount(1);
        resultB.Value![0].Name.Should().Be("Tenant B Leave");
    }

    [Fact]
    public async Task GetById_CrossTenant_ShouldReturn404()
    {
        var tenantA = Guid.NewGuid();
        var locId = await SeedLeaveType("Secret Leave", tenantId: tenantA);

        var tenantB = Guid.NewGuid();
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);
        var serviceB = new LeaveTypeService(
            TestDbContextFactory.Create(ctxB, _dbName), ctxB, _currentUser, _logger);

        var result = await serviceB.GetByIdAsync(locId);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Create_SameNameDifferentTenant_ShouldSucceed()
    {
        var tenantA = Guid.NewGuid();
        await SeedLeaveType("Annual Leave", tenantId: tenantA);

        var tenantB = Guid.NewGuid();
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);
        var serviceB = new LeaveTypeService(
            TestDbContextFactory.Create(ctxB, _dbName), ctxB, _currentUser, _logger);

        var result = await serviceB.CreateAsync(MakeCreateRequest("Annual Leave"));
        result.IsSuccess.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Validation: color hex
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Create_ValidHexColor_ShouldSucceed()
    {
        var service = CreateService();
        var result = await service.CreateAsync(MakeCreateRequest(color: "#FF5733"));
        result.IsSuccess.Should().BeTrue();
        result.Value!.Color.Should().Be("#FF5733");
    }

    // -----------------------------------------------------------------------
    // Validation: gender enum
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Create_FemaleGender_ShouldSucceed()
    {
        var service = CreateService();
        var result = await service.CreateAsync(MakeCreateRequest("Maternity", gender: "Female"));
        result.IsSuccess.Should().BeTrue();
        result.Value!.Gender.Should().Be("Female");
    }

    [Fact]
    public async Task Create_MaleGender_ShouldSucceed()
    {
        var service = CreateService();
        var result = await service.CreateAsync(MakeCreateRequest("Paternity", gender: "Male"));
        result.IsSuccess.Should().BeTrue();
        result.Value!.Gender.Should().Be("Male");
    }

    [Fact]
    public async Task Create_InvalidGender_ShouldFail()
    {
        var service = CreateService();
        var result = await service.CreateAsync(MakeCreateRequest(gender: "Other"));
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Invalid gender");
    }

    // -----------------------------------------------------------------------
    // Validation: accrual frequency enum
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Create_InvalidAccrualFrequency_ShouldFail()
    {
        var service = CreateService();
        var result = await service.CreateAsync(MakeCreateRequest(accrualFrequency: "Weekly"));
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Invalid accrual frequency");
    }

    [Theory]
    [InlineData("Monthly")]
    [InlineData("Quarterly")]
    [InlineData("Yearly")]
    [InlineData("Upfront")]
    public async Task Create_ValidAccrualFrequencies_ShouldSucceed(string frequency)
    {
        var service = CreateService();
        var result = await service.CreateAsync(
            MakeCreateRequest($"Leave-{frequency}", accrualFrequency: frequency));
        result.IsSuccess.Should().BeTrue();
        result.Value!.AccrualFrequency.Should().Be(frequency);
    }

    // -----------------------------------------------------------------------
    // Update
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Update_ChangeName_ShouldSucceed()
    {
        var id = await SeedLeaveType("Old Name");
        var service = CreateService();

        var result = await service.UpdateAsync(id, new UpdateLeaveTypeRequest
        {
            Name = "New Name",
            AnnualEntitlement = 10,
            AccrualFrequency = "Yearly",
            Gender = "All",
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("New Name");
        result.Value.AnnualEntitlement.Should().Be(10);
        result.Value.AccrualFrequency.Should().Be("Yearly");
    }

    [Fact]
    public async Task Update_SameNameAsSelf_ShouldSucceed()
    {
        var id = await SeedLeaveType("Annual Leave");
        var service = CreateService();

        var result = await service.UpdateAsync(id, new UpdateLeaveTypeRequest
        {
            Name = "Annual Leave",
            AnnualEntitlement = 20,
            AccrualFrequency = "Upfront",
            Gender = "All",
        });

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Update_DuplicateNameCaseInsensitive_ShouldFail()
    {
        await SeedLeaveType("Annual Leave");
        var id2 = await SeedLeaveType("Sick Leave", displayOrder: 2);
        var service = CreateService();

        var result = await service.UpdateAsync(id2, new UpdateLeaveTypeRequest
        {
            Name = "annual leave",
            AnnualEntitlement = 7,
            AccrualFrequency = "Upfront",
            Gender = "All",
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("A leave type with this name already exists.");
    }

    [Fact]
    public async Task Update_NonExistent_ShouldReturn404()
    {
        var service = CreateService();

        var result = await service.UpdateAsync(Guid.NewGuid(), new UpdateLeaveTypeRequest
        {
            Name = "Ghost",
            AnnualEntitlement = 10,
            AccrualFrequency = "Upfront",
            Gender = "All",
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // -----------------------------------------------------------------------
    // Auto display order
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Create_AutoDisplayOrder_ShouldIncrementFromMax()
    {
        await SeedLeaveType("First", displayOrder: 5);
        var service = CreateService();

        var result = await service.CreateAsync(MakeCreateRequest("Second", displayOrder: null));

        result.IsSuccess.Should().BeTrue();
        result.Value!.DisplayOrder.Should().Be(6);
    }

    [Fact]
    public async Task Create_NoExistingLeaveTypes_AutoDisplayOrder_ShouldBe1()
    {
        var service = CreateService();

        var result = await service.CreateAsync(MakeCreateRequest(displayOrder: null));

        result.IsSuccess.Should().BeTrue();
        result.Value!.DisplayOrder.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // GetAll ordering
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAll_ShouldOrderByDisplayOrder()
    {
        await SeedLeaveType("Third", displayOrder: 3);
        await SeedLeaveType("First", displayOrder: 1);
        await SeedLeaveType("Second", displayOrder: 2);
        var service = CreateService();

        var result = await service.GetAllAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(3);
        result.Value![0].Name.Should().Be("First");
        result.Value![1].Name.Should().Be("Second");
        result.Value![2].Name.Should().Be("Third");
    }

    // -----------------------------------------------------------------------
    // Default seeding (FR-4)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SeedDefaults_NewTenant_ShouldCreateDefaultLeaveTypes()
    {
        var newTenantId = Guid.NewGuid();
        var service = CreateService();

        var result = await service.SeedDefaultsForTenantAsync(newTenantId);

        result.IsSuccess.Should().BeTrue();

        // Verify defaults were created
        var ctxNew = Substitute.For<ITenantContext>();
        ctxNew.TenantId.Returns(newTenantId);
        ctxNew.IsResolved.Returns(true);
        var serviceNew = new LeaveTypeService(
            TestDbContextFactory.Create(ctxNew, _dbName), ctxNew, _currentUser, _logger);

        var listResult = await serviceNew.GetAllAsync();
        listResult.IsSuccess.Should().BeTrue();
        listResult.Value!.Count.Should().Be(7); // 7 default leave types
        listResult.Value!.Select(lt => lt.Name).Should().Contain("Annual Leave");
        listResult.Value!.Select(lt => lt.Name).Should().Contain("Sick Leave");
        listResult.Value!.Select(lt => lt.Name).Should().Contain("Unpaid Leave");
    }

    [Fact]
    public async Task SeedDefaults_ExistingLeaveTypes_ShouldSkip()
    {
        var tenantId = Guid.NewGuid();
        await SeedLeaveType("Existing Leave", tenantId: tenantId);
        var service = CreateService();

        var result = await service.SeedDefaultsForTenantAsync(tenantId);

        result.IsSuccess.Should().BeTrue();

        // Should still only have the one we seeded
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenantId);
        ctx.IsResolved.Returns(true);
        var svc = new LeaveTypeService(
            TestDbContextFactory.Create(ctx, _dbName), ctx, _currentUser, _logger);

        var listResult = await svc.GetAllAsync();
        listResult.IsSuccess.Should().BeTrue();
        listResult.Value!.Count.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Tenant context not resolved
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Create_TenantNotResolved_ShouldFail()
    {
        _tenantContext.IsResolved.Returns(false);
        var service = CreateService();

        var result = await service.CreateAsync(MakeCreateRequest());

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

        var result = await service.UpdateAsync(Guid.NewGuid(), new UpdateLeaveTypeRequest
        {
            Name = "Test",
            AnnualEntitlement = 10,
            AccrualFrequency = "Upfront",
            Gender = "All",
        });

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
    public async Task Reactivate_TenantNotResolved_ShouldFail()
    {
        _tenantContext.IsResolved.Returns(false);
        var service = CreateService();

        var result = await service.ReactivateAsync(Guid.NewGuid());

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

    [Fact]
    public async Task Reorder_TenantNotResolved_ShouldFail()
    {
        _tenantContext.IsResolved.Returns(false);
        var service = CreateService();

        var result = await service.ReorderAsync(new List<Guid> { Guid.NewGuid() });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Tenant context is not resolved");
    }

    // -----------------------------------------------------------------------
    // Name trimming
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Create_ShouldTrimName()
    {
        var service = CreateService();
        var result = await service.CreateAsync(MakeCreateRequest("  Trimmed Name  "));
        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Trimmed Name");
    }

    public void Dispose()
    {
        // InMemory databases are cleaned up when the last connection closes
    }
}
