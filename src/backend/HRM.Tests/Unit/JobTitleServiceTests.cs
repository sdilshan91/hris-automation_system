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

    // ISSUE-021: seed an active (or inactive) SalaryGrade in the current tenant so JobTitle.GradeId
    // validation can resolve it.
    private async Task<Guid> SeedSalaryGrade(string code = "G1", bool isActive = true)
    {
        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        var grade = new SalaryGrade
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            Code = code,
            Name = $"Grade {code}",
            MinAmount = 1000m,
            MidAmount = 1500m,
            MaxAmount = 2000m,
            Currency = "USD",
            IsActive = isActive,
            IsDeleted = false,
        };
        db.SalaryGrades.Add(grade);
        await db.SaveChangesAsync();
        return grade.Id;
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

    // ── ISSUE-021: GradeId is now FK-validated against SalaryGrade (service-level) ─────────

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Create_WithNonExistentGradeId_ShouldFail_Issue021()
    {
        // Pre-fix this arbitrary GUID SUCCEEDED (asserting the ISSUE-021 bug). Post-fix an unknown
        // grade is rejected because it does not resolve to an active, in-tenant SalaryGrade.
        var service = CreateService();
        var gradeId = Guid.NewGuid(); // not seeded → no matching SalaryGrade

        var result = await service.CreateAsync(
            "Senior Engineer", "Senior-level engineer", gradeId);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("invalid_grade");

        // And no job title row persisted.
        using var db = CreateDbContext();
        db.JobTitles.Count().Should().Be(0);
    }

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Create_WithValidSeededGradeId_ShouldSucceed_Issue021()
    {
        // Positive arm: a real, active, in-tenant SalaryGrade id is accepted.
        var gradeId = await SeedSalaryGrade("G1");
        var service = CreateService();

        var result = await service.CreateAsync(
            "Senior Engineer", "Senior-level engineer", gradeId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.GradeId.Should().Be(gradeId);
    }

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Create_WithInactiveGradeId_ShouldFail_Issue021()
    {
        // An existing but DEACTIVATED grade cannot be linked.
        var gradeId = await SeedSalaryGrade("G9", isActive: false);
        var service = CreateService();

        var result = await service.CreateAsync("Analyst", null, gradeId);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("invalid_grade");
    }

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Create_WithNullGradeId_ShouldSucceed()
    {
        // BR-2: A job title can exist without a linked grade.
        var service = CreateService();

        var result = await service.CreateAsync(
            "Intern", "Internship position", null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.GradeId.Should().BeNull();
    }

    // ISSUE-021 / NFR-2 (BUG-003 class): a grade owned by ANOTHER tenant must NOT satisfy the link.
    // ValidateGradeAsync relies on the tenant global query filter, so tenant A cannot borrow tenant B's grade.
    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Create_WithCrossTenantGradeId_ShouldFail_Issue021()
    {
        var otherTenant = Guid.NewGuid();
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(otherTenant);
        ctxB.IsResolved.Returns(true);

        Guid otherGradeId;
        using (var dbB = TestDbContextFactory.Create(ctxB, _dbName))
        {
            var grade = new SalaryGrade
            {
                Id = BaseEntity.NewUuidV7(), TenantId = otherTenant, Code = "GB", Name = "Grade B",
                MinAmount = 1000m, MaxAmount = 2000m, Currency = "USD", IsActive = true, IsDeleted = false,
            };
            dbB.SalaryGrades.Add(grade);
            await dbB.SaveChangesAsync();
            otherGradeId = grade.Id;
        }

        // Tenant A (the default context) tries to link tenant B's grade → rejected as invalid.
        var result = await CreateService().CreateAsync("Engineer", null, otherGradeId);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("invalid_grade");
    }

    // ISSUE-021: the linked grade's NAME is populated on the read DTO (detail + list) for display.
    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task GetById_And_GetAll_PopulateGradeName_Issue021()
    {
        var gradeId = await SeedSalaryGrade("G1"); // Name = "Grade G1"
        var created = await CreateService().CreateAsync("Engineer", null, gradeId);
        created.IsSuccess.Should().BeTrue(created.Error);

        var byId = await CreateService().GetByIdAsync(created.Value!.Id);
        byId.Value!.GradeName.Should().Be("Grade G1");

        var all = await CreateService().GetAllAsync();
        all.Value!.Single(j => j.Id == created.Value.Id).GradeName.Should().Be("Grade G1");
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

    // ── BUG-016: case-insensitive title_name uniqueness ─────────────
    // Regression for the case-insensitive uniqueness cluster. Pre-fix the duplicate
    // check compared `j.TitleName == titleName` (case-sensitive), so a case-variant
    // slipped past and a second row persisted. Post-fix it is
    // `j.TitleName.ToLower() == titleName.Trim().ToLower()`.

    [Fact]
    public async Task CreateJobTitle_CaseVariantName_IsRejected_BUG016()
    {
        // Arrange: "Software Engineer" already exists in this tenant.
        await SeedJobTitle("Software Engineer");
        var service = CreateService();

        // Act: attempt a case-variant "software engineer".
        var result = await service.CreateAsync("software engineer", "Different description", null);

        // Assert: rejected as a duplicate (pre-fix this SUCCEEDED — the bug).
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("A job title with this name already exists.");

        // And no second row persisted.
        using var db = CreateDbContext();
        db.JobTitles.Count().Should().Be(1);
    }

    [Fact]
    public async Task UpdateJobTitle_CaseVariantOfAnother_IsRejected_BUG016()
    {
        await SeedJobTitle("Software Engineer");
        var hrId = await SeedJobTitle("HR Manager");
        var service = CreateService();

        // Rename "HR Manager" to a case-variant of the existing "Software Engineer".
        var result = await service.UpdateAsync(hrId, "SOFTWARE ENGINEER", null, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("A job title with this name already exists.");
    }

    // ── ISSUE-022: title_name is trimmed on create/update (TC-CHR-046) ───────────

    [Fact]
    public async Task CreateJobTitle_TrimsSurroundingWhitespace_OnPersist_Issue022()
    {
        var service = CreateService();

        var result = await service.CreateAsync("  Trim Probe  ", null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TitleName.Should().Be("Trim Probe");

        // Confirm the persisted row is trimmed, not just the returned DTO.
        using var db = CreateDbContext();
        db.JobTitles.Single().TitleName.Should().Be("Trim Probe");
    }

    [Fact]
    public async Task UpdateJobTitle_TrimsSurroundingWhitespace_OnPersist_Issue022()
    {
        var id = await SeedJobTitle("Original Title");
        var service = CreateService();

        var result = await service.UpdateAsync(id, "  Renamed Title  ", null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TitleName.Should().Be("Renamed Title");

        using var db = CreateDbContext();
        db.JobTitles.Single(j => j.Id == id).TitleName.Should().Be("Renamed Title");
    }

    [Fact]
    public async Task CreateJobTitle_GenuinelyDistinctName_Succeeds_BUG016()
    {
        // Positive control: a truly different name still creates.
        await SeedJobTitle("Software Engineer");
        var service = CreateService();

        var result = await service.CreateAsync("Product Manager", null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TitleName.Should().Be("Product Manager");

        using var db = CreateDbContext();
        db.JobTitles.Count().Should().Be(2);
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
    [Trait("TC", "TC-CHR-337")]
    public async Task Update_ChangeGradeId_ToValidSeededGrade_ShouldSucceed_Issue021()
    {
        // Post-fix: linking to a real, active, in-tenant SalaryGrade succeeds.
        var id = await SeedJobTitle("Engineer", gradeId: null);
        var newGradeId = await SeedSalaryGrade("G2");
        var service = CreateService();

        var result = await service.UpdateAsync(
            id, "Engineer", null, newGradeId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.GradeId.Should().Be(newGradeId);
    }

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Update_ChangeGradeId_ToNonExistentGrade_ShouldFail_Issue021()
    {
        // Pre-fix this arbitrary GUID SUCCEEDED (the ISSUE-021 bug). Post-fix it is rejected.
        var id = await SeedJobTitle("Engineer", gradeId: null);
        var service = CreateService();
        var newGradeId = Guid.NewGuid(); // not seeded

        var result = await service.UpdateAsync(
            id, "Engineer", null, newGradeId);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("invalid_grade");
    }

    [Fact]
    [Trait("TC", "TC-CHR-337")]
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
