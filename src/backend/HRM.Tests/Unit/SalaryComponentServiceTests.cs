// ============================================================================
// US-PAY-001: SalaryComponentService unit tests.
// Covers FR-1 CRUD, BR-2 unique code per tenant (two tenants may share a code),
// FR-4/BR-6 formula syntax validation, AC-5 delete-guard when in use, AC-6 tenant isolation.
// Uses the EF Core InMemory provider with tenant-scoped global query filters.
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

public sealed class SalaryComponentServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ICurrentUser _currentUser;

    public SalaryComponentServiceTests()
    {
        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("hr@test.com");
        _currentUser.UserId.Returns(Guid.NewGuid());
    }

    private ITenantContext Tenant(Guid tenantId)
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenantId);
        ctx.IsResolved.Returns(true);
        ctx.IsSystemContext.Returns(false);
        return ctx;
    }

    private SalaryComponentService Service(Guid tenantId)
    {
        var ctx = Tenant(tenantId);
        var db = TestDbContextFactory.Create(ctx, _dbName);
        return new SalaryComponentService(db, ctx, _currentUser, Substitute.For<IPayrollAuditLogger>(), Substitute.For<ILogger<SalaryComponentService>>());
    }

    private static SalaryComponentInput Earning(string code = "BASIC", string name = "Basic Salary")
        => new(name, code, SalaryComponentType.Earning, CalculationMethod.Fixed, 1000m, null, true, false, true, 1);

    [Fact]
    public async Task Create_PersistsComponent_WithUpperCasedCode()
    {
        var result = await Service(_tenantId).CreateAsync(Earning(code: "basic"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Code.Should().Be("BASIC");
        result.Value.Type.Should().Be(SalaryComponentType.Earning);
        result.Value.TypeName.Should().Be("Earning");
    }

    [Fact]
    public async Task Create_DuplicateCode_SameTenant_IsRejected()
    {
        var svc = Service(_tenantId);
        await svc.CreateAsync(Earning());

        var second = await svc.CreateAsync(Earning(name: "Basic Again"));
        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("duplicate_code");
    }

    [Fact]
    public async Task Create_SameCode_DifferentTenants_IsAllowed()
    {
        var other = Guid.NewGuid();
        var a = await Service(_tenantId).CreateAsync(Earning());
        var b = await Service(other).CreateAsync(Earning());

        a.IsSuccess.Should().BeTrue();
        b.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_FixedMethod_WithoutDefaultValue_IsRejected()
    {
        var input = new SalaryComponentInput("X", "X", SalaryComponentType.Earning, CalculationMethod.Fixed,
            null, null, true, false, true, 0);
        var result = await Service(_tenantId).CreateAsync(input);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("default_value_required");
    }

    [Fact]
    public async Task Create_FormulaMethod_WithInvalidSyntax_IsRejected()
    {
        var input = new SalaryComponentInput("PF", "PF", SalaryComponentType.Statutory, CalculationMethod.Formula,
            null, "basic *", true, true, true, 5);
        var result = await Service(_tenantId).CreateAsync(input);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_formula");
    }

    [Fact]
    public async Task Create_FormulaMethod_ValidSyntax_Succeeds_AndClearsDefaultValue()
    {
        var input = new SalaryComponentInput("PF", "PF", SalaryComponentType.Statutory, CalculationMethod.Formula,
            999m /* should be discarded */, "basic * 0.12", false, true, true, 5);
        var result = await Service(_tenantId).CreateAsync(input);

        result.IsSuccess.Should().BeTrue();
        result.Value!.FormulaExpression.Should().Be("basic * 0.12");
        result.Value.DefaultValue.Should().BeNull();
    }

    [Fact]
    public async Task Create_PercentageOverHundred_IsRejected()
    {
        var input = new SalaryComponentInput("HRA", "HRA", SalaryComponentType.Earning, CalculationMethod.PercentageOfBasic,
            150m, null, true, false, true, 2);
        var result = await Service(_tenantId).CreateAsync(input);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_percentage");
    }

    [Fact]
    public async Task Delete_ComponentInUse_IsBlockedWithConflict()
    {
        var svc = Service(_tenantId);
        var created = await svc.CreateAsync(Earning());
        var componentId = created.Value!.Id;

        // Link it to a structure directly via the DbContext.
        var ctx = Tenant(_tenantId);
        using (var db = TestDbContextFactory.Create(ctx, _dbName))
        {
            var structureId = BaseEntity.NewUuidV7();
            db.SalaryStructures.Add(new SalaryStructure
            {
                Id = structureId, TenantId = _tenantId, Name = "S", Code = "S",
                EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true,
            });
            db.SalaryStructureComponents.Add(new SalaryStructureComponent
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId,
                SalaryStructureId = structureId, SalaryComponentId = componentId, ProcessingOrder = 1,
            });
            await db.SaveChangesAsync();
        }

        var del = await svc.DeleteAsync(componentId);
        del.IsFailure.Should().BeTrue();
        del.StatusCode.Should().Be(409);
        del.ErrorCode.Should().Be("component_in_use");
        del.Error.Should().Contain("1");
    }

    [Fact]
    public async Task Delete_UnusedComponent_SoftDeletes()
    {
        var svc = Service(_tenantId);
        var created = await svc.CreateAsync(Earning());

        var del = await svc.DeleteAsync(created.Value!.Id);
        del.IsSuccess.Should().BeTrue();

        var fetched = await svc.GetByIdAsync(created.Value.Id);
        fetched.IsFailure.Should().BeTrue();
        fetched.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task List_IsTenantScoped()
    {
        await Service(_tenantId).CreateAsync(Earning());
        await Service(Guid.NewGuid()).CreateAsync(Earning());

        var list = await Service(_tenantId).ListAsync(null, null, null, 1, 25);
        list.Value!.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task List_ClampsPageSizeToMax100()
    {
        var list = await Service(_tenantId).ListAsync(null, null, null, 1, 500);
        list.Value!.PageSize.Should().Be(100);
    }
}
