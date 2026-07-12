// ============================================================================
// US-PAY-001: SalaryStructureService unit tests.
// Covers FR-2/FR-3 structure + links, BR-1 single default per tenant, FR-5 earning-before-active,
// BR-6 circular references across formula components, FR-6 clone, AC-4 reorder, BR-3 statutory unlink guard.
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

public sealed class SalaryStructureServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ICurrentUser _currentUser;

    public SalaryStructureServiceTests()
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

    private SalaryStructureService Structures(Guid tenantId)
    {
        var ctx = Tenant(tenantId);
        var db = TestDbContextFactory.Create(ctx, _dbName);
        return new SalaryStructureService(db, ctx, _currentUser, Substitute.For<IPayrollAuditLogger>(), Substitute.For<ILogger<SalaryStructureService>>());
    }

    private SalaryComponentService Components(Guid tenantId)
    {
        var ctx = Tenant(tenantId);
        var db = TestDbContextFactory.Create(ctx, _dbName);
        return new SalaryComponentService(db, ctx, _currentUser, Substitute.For<IPayrollAuditLogger>(), Substitute.For<ILogger<SalaryComponentService>>());
    }

    private async Task<Guid> SeedComponent(
        string code, SalaryComponentType type, CalculationMethod method = CalculationMethod.Fixed,
        decimal? value = 1000m, string? formula = null, bool statutory = false)
    {
        var input = new SalaryComponentInput(code, code, type, method, value, formula, true, statutory, true, 1);
        var result = await Components(_tenantId).CreateAsync(input);
        result.IsSuccess.Should().BeTrue();
        return result.Value!.Id;
    }

    private static SalaryStructureInput Structure(
        string code, bool isActive, bool isDefault,
        params SalaryStructureComponentInput[] components)
        => new("Structure " + code, code, null, new DateOnly(2026, 1, 1), isDefault, isActive, components);

    private static SalaryStructureComponentInput Link(Guid componentId, int order = 1, bool mandatory = false, string? overrideFormula = null)
        => new(componentId, null, overrideFormula, order, mandatory);

    // ── FR-5: earning required before active ──────────────────────────

    [Fact]
    public async Task Create_ActiveStructure_WithoutEarning_IsRejected()
    {
        var deduction = await SeedComponent("LOAN", SalaryComponentType.Deduction);

        var result = await Structures(_tenantId).CreateAsync(
            Structure("DEDONLY", isActive: true, isDefault: false, Link(deduction)));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("no_earning_component");
    }

    [Fact]
    public async Task Create_InactiveStructure_WithoutEarning_IsAllowed()
    {
        var deduction = await SeedComponent("LOAN", SalaryComponentType.Deduction);

        var result = await Structures(_tenantId).CreateAsync(
            Structure("DRAFT", isActive: false, isDefault: false, Link(deduction)));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_ActiveStructure_WithEarning_Succeeds()
    {
        var basic = await SeedComponent("BASIC", SalaryComponentType.Earning);

        var result = await Structures(_tenantId).CreateAsync(
            Structure("FT", isActive: true, isDefault: false, Link(basic)));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Components.Should().ContainSingle();
    }

    // ── BR-2: unique code ─────────────────────────────────────────────

    [Fact]
    public async Task Create_DuplicateStructureCode_IsRejected()
    {
        var basic = await SeedComponent("BASIC", SalaryComponentType.Earning);
        var svc = Structures(_tenantId);
        await svc.CreateAsync(Structure("FT", true, false, Link(basic)));

        var dup = await Structures(_tenantId).CreateAsync(Structure("ft", false, false));
        dup.IsFailure.Should().BeTrue();
        dup.ErrorCode.Should().Be("duplicate_code");
    }

    // ── BR-1: single default per tenant ───────────────────────────────

    [Fact]
    public async Task Create_SecondDefault_ClearsFirstDefault()
    {
        var basic = await SeedComponent("BASIC", SalaryComponentType.Earning);

        var first = await Structures(_tenantId).CreateAsync(Structure("D1", true, isDefault: true, Link(basic)));
        first.Value!.IsDefault.Should().BeTrue();

        var second = await Structures(_tenantId).CreateAsync(Structure("D2", true, isDefault: true, Link(basic)));
        second.Value!.IsDefault.Should().BeTrue();

        var firstReloaded = await Structures(_tenantId).GetByIdAsync(first.Value.Id);
        firstReloaded.Value!.IsDefault.Should().BeFalse();
    }

    // ── BR-6: circular references ──────────────────────────────────────

    [Fact]
    public async Task Create_WithCircularFormulaComponents_IsRejected()
    {
        // A references B, B references A — both formula components.
        var a = await SeedComponent("A", SalaryComponentType.Earning, CalculationMethod.Formula, null, "B + 1");
        var b = await SeedComponent("B", SalaryComponentType.Earning, CalculationMethod.Formula, null, "A + 1");

        var result = await Structures(_tenantId).CreateAsync(
            Structure("CYC", isActive: true, isDefault: false, Link(a, 1), Link(b, 2)));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("circular_reference");
    }

    [Fact]
    public async Task Create_FormulaReferencingFixedComponent_IsAllowed()
    {
        var basic = await SeedComponent("BASIC", SalaryComponentType.Earning);
        var pf = await SeedComponent("PF", SalaryComponentType.Statutory, CalculationMethod.Formula, null, "BASIC * 0.12");

        var result = await Structures(_tenantId).CreateAsync(
            Structure("OK", isActive: true, isDefault: false, Link(basic, 1), Link(pf, 2)));

        result.IsSuccess.Should().BeTrue();
    }

    // ── FR-6: clone ───────────────────────────────────────────────────

    [Fact]
    public async Task Clone_CopiesComponents_AndIsNeverDefault()
    {
        var basic = await SeedComponent("BASIC", SalaryComponentType.Earning);
        var source = await Structures(_tenantId).CreateAsync(
            Structure("SRC", isActive: true, isDefault: true, Link(basic)));

        var clone = await Structures(_tenantId).CloneAsync(source.Value!.Id, "Cloned", "CLONE");

        clone.IsSuccess.Should().BeTrue();
        clone.Value!.IsDefault.Should().BeFalse();
        clone.Value.Code.Should().Be("CLONE");
        clone.Value.Components.Should().ContainSingle();
    }

    [Fact]
    public async Task Clone_DuplicateCode_IsRejected()
    {
        var basic = await SeedComponent("BASIC", SalaryComponentType.Earning);
        var source = await Structures(_tenantId).CreateAsync(Structure("SRC", true, false, Link(basic)));

        var clone = await Structures(_tenantId).CloneAsync(source.Value!.Id, "Dup", "SRC");
        clone.IsFailure.Should().BeTrue();
        clone.ErrorCode.Should().Be("duplicate_code");
    }

    // ── AC-4: reorder ─────────────────────────────────────────────────

    [Fact]
    public async Task Reorder_UpdatesProcessingOrder()
    {
        var basic = await SeedComponent("BASIC", SalaryComponentType.Earning);
        var hra = await SeedComponent("HRA", SalaryComponentType.Earning);
        var created = await Structures(_tenantId).CreateAsync(
            Structure("FT", true, false, Link(basic, 1), Link(hra, 2)));

        var links = created.Value!.Components.OrderBy(c => c.ProcessingOrder).ToList();
        var order = new List<(Guid, int)> { (links[0].Id, 5), (links[1].Id, 1) };

        var reordered = await Structures(_tenantId).ReorderComponentsAsync(created.Value.Id, order);
        reordered.IsSuccess.Should().BeTrue();
        reordered.Value!.Components.First().ComponentCode.Should().Be("HRA"); // now lowest order, listed first
    }

    // ── BR-3: statutory mandatory unlink guard ────────────────────────

    [Fact]
    public async Task Unlink_MandatoryStatutoryComponent_IsBlocked()
    {
        var basic = await SeedComponent("BASIC", SalaryComponentType.Earning);
        var epf = await SeedComponent("EPF", SalaryComponentType.Statutory, statutory: true);
        var created = await Structures(_tenantId).CreateAsync(
            Structure("FT", true, false, Link(basic, 1), Link(epf, 2, mandatory: true)));

        var epfLink = created.Value!.Components.Single(c => c.ComponentCode == "EPF");
        var result = await Structures(_tenantId).UnlinkComponentAsync(created.Value.Id, epfLink.Id);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("statutory_mandatory");
    }

    [Fact]
    public async Task LinkComponent_Twice_IsRejected()
    {
        var basic = await SeedComponent("BASIC", SalaryComponentType.Earning);
        var created = await Structures(_tenantId).CreateAsync(Structure("FT", true, false, Link(basic)));

        var dup = await Structures(_tenantId).LinkComponentAsync(created.Value!.Id,
            new SalaryStructureComponentInput(basic, null, null, 3, false));

        dup.IsFailure.Should().BeTrue();
        dup.ErrorCode.Should().Be("component_already_linked");
    }

    // ── AC-6: tenant isolation ────────────────────────────────────────

    [Fact]
    public async Task GetById_FromOtherTenant_ReturnsNotFound()
    {
        var basic = await SeedComponent("BASIC", SalaryComponentType.Earning);
        var created = await Structures(_tenantId).CreateAsync(Structure("FT", true, false, Link(basic)));

        var other = await Structures(Guid.NewGuid()).GetByIdAsync(created.Value!.Id);
        other.IsFailure.Should().BeTrue();
        other.StatusCode.Should().Be(404);
    }
}
