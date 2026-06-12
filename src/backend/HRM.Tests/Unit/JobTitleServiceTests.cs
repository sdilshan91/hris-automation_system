// ============================================================================
// US-CHR-005: Job Title Management Unit Tests
// Tests job title CRUD, title_name uniqueness (AC-2, AC-3), grade_id nullable
// (AC-4, BR-2), deactivation rules (AC-5, FR-5), cross-tenant isolation (NFR-2),
// and edit behavior.
// Uses EF Core InMemory provider for lightweight database testing.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class JobTitleServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<JobTitleService> _logger;

    public JobTitleServiceTests()
    {
        _dbName = Guid.NewGuid().ToString();
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("admin@test.com");
        _currentUser.UserId.Returns(Guid.NewGuid());

        _logger = Substitute.For<ILogger<JobTitleService>>();
    }

    private JobTitleService CreateService()
    {
        var dbContext = TestDbContextFactory.Create(_tenantContext, _dbName);
        return new JobTitleService(dbContext, _tenantContext, _currentUser, _logger);
    }

    private Infrastructure.Persistence.AppDbContext CreateDbContext()
    {
        return TestDbContextFactory.Create(_tenantContext, _dbName);
    }

    private async Task<Guid> SeedJobTitle(
        string titleName, string? description = null, Guid? gradeId = null,
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
        var jobTitle = new JobTitle
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tid,
            TitleName = titleName,
            Description = description,
            GradeId = gradeId,
            IsActive = isActive,
            IsDeleted = false,
        };
        db.JobTitles.Add(jobTitle);
        await db.SaveChangesAsync();
        return jobTitle.Id;
    }

    // ── AC-2: Create job title ──────────────────────────────────────

    [Fact]
    public async Task Create_ValidJobTitle_ShouldSucceed()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            "Software Engineer", "Develops software solutions", null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TitleName.Should().Be("Software Engineer");
        result.Value.Description.Should().Be("Develops software solutions");
        result.Value.GradeId.Should().BeNull();
        result.Value.IsActive.Should().BeTrue();
        result.Value.EmployeeCount.Should().Be(0); // Stubbed until US-CHR-001
    }

    [Fact]
    public async Task Create_WithGradeId_ShouldSucceed()
    {
        var service = CreateService();
        var gradeId = Guid.NewGuid(); // No FK constraint (deferred to Payroll module)

        var result = await service.CreateAsync(
            "Senior Engineer", "Senior-level engineer", gradeId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.GradeId.Should().Be(gradeId);
    }

    [Fact]
    public async Task Create_WithNullGradeId_ShouldSucceed()
    {
        // BR-2: A job title can exist without a linked grade.
        var service = CreateService();

        var result = await service.CreateAsync(
            "Intern", "Internship position", null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.GradeId.Should().BeNull();
    }

    // ── AC-3: Duplicate name rejection ──────────────────────────────

    [Fact]
    public async Task Create_DuplicateNameSameTenant_ShouldFail()
    {
        await SeedJobTitle("Software Engineer");
        var service = CreateService();

        var result = await service.CreateAsync(
            "Software Engineer", "Different description", null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("A job title with this name already exists.");
    }

    // ── BR-1: Same name allowed cross-tenant ─────────────────────────

    [Fact]
    public async Task Create_SameNameDifferentTenant_ShouldSucceed()
    {
        // Arrange: create job title in tenant A
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Seed in tenant A
        var tenantAContext = Substitute.For<ITenantContext>();
        tenantAContext.TenantId.Returns(tenantA);
        tenantAContext.IsResolved.Returns(true);
        var dbA = TestDbContextFactory.Create(tenantAContext, _dbName);
        dbA.JobTitles.Add(new JobTitle
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantA,
            TitleName = "Software Engineer",
            IsActive = true,
        });
        await dbA.SaveChangesAsync();

        // Act: create same name in tenant B
        var tenantBContext = Substitute.For<ITenantContext>();
        tenantBContext.TenantId.Returns(tenantB);
        tenantBContext.IsResolved.Returns(true);
        var dbB = TestDbContextFactory.Create(tenantBContext, _dbName);
        var serviceB = new JobTitleService(
            dbB, tenantBContext, _currentUser, _logger);

        var result = await serviceB.CreateAsync(
            "Software Engineer", null, null);

        // Assert: same name succeeds in a different tenant (BR-1)
        result.IsSuccess.Should().BeTrue();
    }

    // ── Edit / Update ─────────────────────────────────────────────────

    [Fact]
    public async Task Update_ChangeName_ShouldSucceed()
    {
        var id = await SeedJobTitle("Old Title");
        var service = CreateService();

        var result = await service.UpdateAsync(
            id, "New Title", "updated description", null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TitleName.Should().Be("New Title");
        result.Value.Description.Should().Be("updated description");
    }

    [Fact]
    public async Task Update_SameNameAsSelf_ShouldSucceed()
    {
        var id = await SeedJobTitle("Software Engineer");
        var service = CreateService();

        var result = await service.UpdateAsync(
            id, "Software Engineer", "updated description", null);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Update_DuplicateNameExcludingSelf_ShouldFail()
    {
        await SeedJobTitle("Software Engineer");
        var id2 = await SeedJobTitle("HR Manager");
        var service = CreateService();

        var result = await service.UpdateAsync(
            id2, "Software Engineer", null, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("A job title with this name already exists.");
    }

    [Fact]
    public async Task Update_NonExistentJobTitle_ShouldFail()
    {
        var service = CreateService();

        var result = await service.UpdateAsync(
            Guid.NewGuid(), "DoesNotExist", null, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Update_ChangeGradeId_ShouldSucceed()
    {
        var id = await SeedJobTitle("Engineer", gradeId: null);
        var service = CreateService();
        var newGradeId = Guid.NewGuid();

        var result = await service.UpdateAsync(
            id, "Engineer", null, newGradeId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.GradeId.Should().Be(newGradeId);
    }

    [Fact]
    public async Task Update_RemoveGradeId_ShouldSucceed()
    {
        var gradeId = Guid.NewGuid();
        var id = await SeedJobTitle("Engineer", gradeId: gradeId);
        var service = CreateService();

        var result = await service.UpdateAsync(
            id, "Engineer", null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.GradeId.Should().BeNull();
    }

    // ── Deactivate ──────────────────────────────────────────────────

    [Fact]
    public async Task Deactivate_ActiveJobTitle_ShouldSucceed()
    {
        var id = await SeedJobTitle("Marketing Manager");
        var service = CreateService();

        var result = await service.DeactivateAsync(id);

        result.IsSuccess.Should().BeTrue();

        // Verify deactivation persisted
        using var db = CreateDbContext();
        var jt = db.JobTitles.FirstOrDefault(j => j.Id == id);
        jt.Should().NotBeNull();
        jt!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Deactivate_AlreadyDeactivated_ShouldFail()
    {
        var id = await SeedJobTitle("Old Title", isActive: false);
        var service = CreateService();

        var result = await service.DeactivateAsync(id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("already deactivated");
    }

    [Fact]
    public async Task Deactivate_NonExistentJobTitle_ShouldFail()
    {
        var service = CreateService();

        var result = await service.DeactivateAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── Cross-tenant isolation (NFR-2) ──────────────────────────────

    [Fact]
    public async Task GetAll_ShouldOnlyReturnCurrentTenantJobTitles()
    {
        // Arrange: seed job titles in two different tenants
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Seed in tenant A
        var ctxA = Substitute.For<ITenantContext>();
        ctxA.TenantId.Returns(tenantA);
        ctxA.IsResolved.Returns(true);
        var dbA = TestDbContextFactory.Create(ctxA, _dbName);
        dbA.JobTitles.Add(new JobTitle
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantA,
            TitleName = "Tenant A Engineer",
            IsActive = true,
        });
        await dbA.SaveChangesAsync();

        // Seed in tenant B
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);
        var dbB = TestDbContextFactory.Create(ctxB, _dbName);
        dbB.JobTitles.Add(new JobTitle
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantB,
            TitleName = "Tenant B Engineer",
            IsActive = true,
        });
        await dbB.SaveChangesAsync();

        // Act: query from tenant A
        var serviceA = new JobTitleService(
            TestDbContextFactory.Create(ctxA, _dbName), ctxA, _currentUser, _logger);
        var resultA = await serviceA.GetAllAsync();

        // Assert: tenant A sees only their own job titles
        resultA.IsSuccess.Should().BeTrue();
        resultA.Value!.Should().HaveCount(1);
        resultA.Value[0].TitleName.Should().Be("Tenant A Engineer");

        // Act: query from tenant B
        var serviceB = new JobTitleService(
            TestDbContextFactory.Create(ctxB, _dbName), ctxB, _currentUser, _logger);
        var resultB = await serviceB.GetAllAsync();

        // Assert: tenant B sees only their own job titles
        resultB.IsSuccess.Should().BeTrue();
        resultB.Value!.Should().HaveCount(1);
        resultB.Value[0].TitleName.Should().Be("Tenant B Engineer");
    }

    [Fact]
    public async Task GetById_CrossTenant_ShouldReturn404()
    {
        // Arrange: job title in tenant A
        var tenantA = Guid.NewGuid();
        var ctxA = Substitute.For<ITenantContext>();
        ctxA.TenantId.Returns(tenantA);
        ctxA.IsResolved.Returns(true);
        var dbA = TestDbContextFactory.Create(ctxA, _dbName);
        var jtA = new JobTitle
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantA,
            TitleName = "Secret Title",
            IsActive = true,
        };
        dbA.JobTitles.Add(jtA);
        await dbA.SaveChangesAsync();

        // Act: try to access from tenant B
        var tenantB = Guid.NewGuid();
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);
        var serviceB = new JobTitleService(
            TestDbContextFactory.Create(ctxB, _dbName), ctxB, _currentUser, _logger);

        var result = await serviceB.GetByIdAsync(jtA.Id);

        // Assert: not found (tenant isolation)
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── GetAll with filter ──────────────────────────────────────────

    [Fact]
    public async Task GetAll_ActiveOnly_ShouldFilterInactive()
    {
        await SeedJobTitle("Active Title");
        await SeedJobTitle("Inactive Title", isActive: false);
        var service = CreateService();

        var result = await service.GetAllAsync(activeOnly: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(1);
        result.Value[0].TitleName.Should().Be("Active Title");
    }

    [Fact]
    public async Task GetAll_NoFilter_ShouldReturnAll()
    {
        await SeedJobTitle("Active Title");
        await SeedJobTitle("Inactive Title", isActive: false);
        var service = CreateService();

        var result = await service.GetAllAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
    }

    // ── Tenant context not resolved ─────────────────────────────────

    [Fact]
    public async Task Create_TenantNotResolved_ShouldFail()
    {
        _tenantContext.IsResolved.Returns(false);
        var service = CreateService();

        var result = await service.CreateAsync("Eng", null, null);

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

        var result = await service.UpdateAsync(Guid.NewGuid(), "Title", null, null);

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

    // ── Employee count stub (FR-4) ──────────────────────────────────

    [Fact]
    public async Task GetById_EmployeeCount_ShouldReturnZeroStub()
    {
        // FR-4: Employee count is stubbed to 0 until US-CHR-001 wires Employee entity.
        var id = await SeedJobTitle("Manager");
        var service = CreateService();

        var result = await service.GetByIdAsync(id);

        result.IsSuccess.Should().BeTrue();
        result.Value!.EmployeeCount.Should().Be(0);
    }

    public void Dispose()
    {
        // InMemory databases are cleaned up when the last connection closes
    }
}
