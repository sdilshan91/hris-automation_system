// ============================================================================
// ISSUE-021: Salary Grade Management Unit Tests
// Tests salary grade CRUD, code uniqueness (per tenant, case-insensitive, 409),
// Min<=Mid<=Max amount ordering (422), soft-delete/deactivate, and cross-tenant
// isolation. Uses EF Core InMemory provider through the real AppDbContext.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.SalaryGrades.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class SalaryGradeServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<SalaryGradeService> _logger;

    public SalaryGradeServiceTests()
    {
        _dbName = Guid.NewGuid().ToString();
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("admin@test.com");
        _currentUser.UserId.Returns(Guid.NewGuid());

        _logger = Substitute.For<ILogger<SalaryGradeService>>();
    }

    private SalaryGradeService CreateService()
    {
        var dbContext = TestDbContextFactory.Create(_tenantContext, _dbName);
        return new SalaryGradeService(dbContext, _tenantContext, _currentUser, _logger);
    }

    private Infrastructure.Persistence.AppDbContext CreateDbContext()
        => TestDbContextFactory.Create(_tenantContext, _dbName);

    private static CreateSalaryGradeRequest NewCreateRequest(
        string code = "G1", string name = "Grade 1",
        decimal min = 1000m, decimal? mid = 1500m, decimal max = 2000m,
        string currency = "USD", string? description = "Associate band")
        => new()
        {
            Code = code,
            Name = name,
            MinAmount = min,
            MidAmount = mid,
            MaxAmount = max,
            Currency = currency,
            Description = description,
        };

    // ── Create ──────────────────────────────────────────────────────

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Create_ValidGrade_PersistsAndReadsBack()
    {
        var service = CreateService();

        var result = await service.CreateAsync(NewCreateRequest());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Code.Should().Be("G1");
        result.Value.Name.Should().Be("Grade 1");
        result.Value.MinAmount.Should().Be(1000m);
        result.Value.MidAmount.Should().Be(1500m);
        result.Value.MaxAmount.Should().Be(2000m);
        result.Value.Currency.Should().Be("USD");
        result.Value.IsActive.Should().BeTrue();

        // Confirm persisted, and tenant-stamped.
        using var db = CreateDbContext();
        var persisted = db.SalaryGrades.Single();
        persisted.Code.Should().Be("G1");
        persisted.TenantId.Should().Be(_tenantId);
    }

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Create_TrimsCode_AndUppercasesCurrency()
    {
        var service = CreateService();

        var result = await service.CreateAsync(NewCreateRequest(code: "  g2  ", currency: "usd"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Code.Should().Be("g2");
        result.Value.Currency.Should().Be("USD");
    }

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Create_NullMidAmount_IsAllowed()
    {
        var service = CreateService();

        var result = await service.CreateAsync(NewCreateRequest(mid: null));

        result.IsSuccess.Should().BeTrue();
        result.Value!.MidAmount.Should().BeNull();
    }

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Create_DuplicateCodeSameTenant_ShouldFail_409()
    {
        var service = CreateService();
        await service.CreateAsync(NewCreateRequest(code: "G1"));

        var result = await service.CreateAsync(NewCreateRequest(code: "G1", name: "Another"));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("duplicate_code");
    }

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Create_DuplicateCodeCaseVariant_ShouldFail_409()
    {
        var service = CreateService();
        await service.CreateAsync(NewCreateRequest(code: "G1"));

        var result = await service.CreateAsync(NewCreateRequest(code: "g1", name: "Another"));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Create_MinGreaterThanMax_ShouldFail_422()
    {
        var service = CreateService();

        var result = await service.CreateAsync(NewCreateRequest(min: 3000m, mid: null, max: 2000m));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("invalid_amount_range");
    }

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Create_MidOutsideRange_ShouldFail_422()
    {
        var service = CreateService();

        var result = await service.CreateAsync(NewCreateRequest(min: 1000m, mid: 5000m, max: 2000m));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("invalid_amount_range");
    }

    // ── Update ──────────────────────────────────────────────────────

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Update_ValidChange_ShouldSucceed()
    {
        var service = CreateService();
        var created = await service.CreateAsync(NewCreateRequest(code: "G1"));
        var id = created.Value!.Id;

        var result = await service.UpdateAsync(id, new UpdateSalaryGradeRequest
        {
            Code = "G1",
            Name = "Grade 1 - Renamed",
            MinAmount = 1200m,
            MidAmount = 1600m,
            MaxAmount = 2200m,
            Currency = "EUR",
            Description = "Updated",
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Grade 1 - Renamed");
        result.Value.Currency.Should().Be("EUR");
        result.Value.MaxAmount.Should().Be(2200m);
    }

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Update_NonExistent_ShouldReturn404()
    {
        var service = CreateService();

        var result = await service.UpdateAsync(Guid.NewGuid(), new UpdateSalaryGradeRequest
        {
            Code = "GX", Name = "X", MinAmount = 1m, MaxAmount = 2m, Currency = "USD",
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Update_DuplicateCodeExcludingSelf_ShouldFail_409()
    {
        var service = CreateService();
        await service.CreateAsync(NewCreateRequest(code: "G1"));
        var second = await service.CreateAsync(NewCreateRequest(code: "G2"));

        var result = await service.UpdateAsync(second.Value!.Id, new UpdateSalaryGradeRequest
        {
            Code = "G1", Name = "Collide", MinAmount = 1m, MaxAmount = 2m, Currency = "USD",
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Update_SameCodeAsSelf_ShouldSucceed()
    {
        var service = CreateService();
        var created = await service.CreateAsync(NewCreateRequest(code: "G1"));

        var result = await service.UpdateAsync(created.Value!.Id, new UpdateSalaryGradeRequest
        {
            Code = "G1", Name = "Grade 1 v2", MinAmount = 1000m, MaxAmount = 2000m, Currency = "USD",
        });

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Update_MinGreaterThanMax_ShouldFail_422()
    {
        var service = CreateService();
        var created = await service.CreateAsync(NewCreateRequest(code: "G1"));

        var result = await service.UpdateAsync(created.Value!.Id, new UpdateSalaryGradeRequest
        {
            Code = "G1", Name = "Grade 1", MinAmount = 9000m, MaxAmount = 2000m, Currency = "USD",
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
    }

    // ── Deactivate (soft-delete) ────────────────────────────────────

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Deactivate_HidesFromActiveList()
    {
        var service = CreateService();
        var created = await service.CreateAsync(NewCreateRequest(code: "G1"));

        var deactivate = await service.DeactivateAsync(created.Value!.Id);
        deactivate.IsSuccess.Should().BeTrue();

        var active = await service.GetAllAsync(includeInactive: false);
        active.Value!.Should().BeEmpty();

        var all = await service.GetAllAsync(includeInactive: true);
        all.Value!.Should().HaveCount(1);
        all.Value[0].IsActive.Should().BeFalse();
    }

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Deactivate_AlreadyInactive_ShouldFail()
    {
        var service = CreateService();
        var created = await service.CreateAsync(NewCreateRequest(code: "G1"));
        await service.DeactivateAsync(created.Value!.Id);

        var result = await service.DeactivateAsync(created.Value.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("already deactivated");
    }

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Deactivate_NonExistent_ShouldReturn404()
    {
        var service = CreateService();

        var result = await service.DeactivateAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── Read ────────────────────────────────────────────────────────

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task GetById_ReturnsGrade()
    {
        var service = CreateService();
        var created = await service.CreateAsync(NewCreateRequest(code: "G1"));

        var result = await service.GetByIdAsync(created.Value!.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Code.Should().Be("G1");
    }

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task GetAll_DefaultExcludesInactive()
    {
        var service = CreateService();
        var g1 = await service.CreateAsync(NewCreateRequest(code: "G1"));
        await service.CreateAsync(NewCreateRequest(code: "G2"));
        await service.DeactivateAsync(g1.Value!.Id);

        var result = await service.GetAllAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(1);
        result.Value[0].Code.Should().Be("G2");
    }

    // ── Cross-tenant isolation ──────────────────────────────────────

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task GetAll_ShouldOnlyReturnCurrentTenantGrades()
    {
        var tenantB = Guid.NewGuid();

        // Seed a grade in tenant A (this service).
        var serviceA = CreateService();
        await serviceA.CreateAsync(NewCreateRequest(code: "A1", name: "Tenant A Grade"));

        // Seed a grade in tenant B, same InMemory database.
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);
        var serviceB = new SalaryGradeService(
            TestDbContextFactory.Create(ctxB, _dbName), ctxB, _currentUser, _logger);
        await serviceB.CreateAsync(NewCreateRequest(code: "B1", name: "Tenant B Grade"));

        var resultA = await CreateService().GetAllAsync();
        resultA.Value!.Should().HaveCount(1);
        resultA.Value[0].Code.Should().Be("A1");

        var resultB = await new SalaryGradeService(
            TestDbContextFactory.Create(ctxB, _dbName), ctxB, _currentUser, _logger).GetAllAsync();
        resultB.Value!.Should().HaveCount(1);
        resultB.Value[0].Code.Should().Be("B1");
    }

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task GetById_CrossTenant_ShouldReturn404()
    {
        var serviceA = CreateService();
        var created = await serviceA.CreateAsync(NewCreateRequest(code: "A1"));

        var tenantB = Guid.NewGuid();
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);
        var serviceB = new SalaryGradeService(
            TestDbContextFactory.Create(ctxB, _dbName), ctxB, _currentUser, _logger);

        var result = await serviceB.GetByIdAsync(created.Value!.Id);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Create_SameCodeDifferentTenant_ShouldSucceed()
    {
        var serviceA = CreateService();
        await serviceA.CreateAsync(NewCreateRequest(code: "G1"));

        var tenantB = Guid.NewGuid();
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);
        var serviceB = new SalaryGradeService(
            TestDbContextFactory.Create(ctxB, _dbName), ctxB, _currentUser, _logger);

        var result = await serviceB.CreateAsync(NewCreateRequest(code: "G1"));

        result.IsSuccess.Should().BeTrue();
    }

    // ── Tenant context not resolved ─────────────────────────────────

    [Fact]
    [Trait("TC", "TC-CHR-337")]
    public async Task Create_TenantNotResolved_ShouldFail()
    {
        _tenantContext.IsResolved.Returns(false);
        var service = CreateService();

        var result = await service.CreateAsync(NewCreateRequest());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Tenant context is not resolved");
    }

    public void Dispose()
    {
        // InMemory databases are cleaned up when the last connection closes.
    }
}
