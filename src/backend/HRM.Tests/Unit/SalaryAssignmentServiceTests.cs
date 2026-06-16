// ============================================================================
// US-PAY-002: SalaryAssignmentService unit tests.
// Covers the assignment flow end-to-end on the InMemory provider with tenant-scoped query filters:
//   - AC-1: assigning a structure writes employee_salary_component rows from the CTC breakdown.
//   - AC-3: per-component override is persisted + flagged.
//   - AC-2/BR-1/BR-2: future-dated assignment does NOT supersede the current one; a current/past date does.
//   - BR-3: revision history captures old/new structure + CTC.
//   - FR-6: sum-of-earnings != CTC (beyond ±1) is rejected.
//   - FR-7: assigning an inactive structure returns 400.
//   - Preview (FR-3) does not persist.
//   - AC-5: cross-tenant read isolation.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class SalaryAssignmentServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ICurrentUser _currentUser;

    public SalaryAssignmentServiceTests()
    {
        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("hr@test.com");
        _currentUser.UserId.Returns(_userId);
    }

    private ITenantContext Tenant(Guid tenantId)
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenantId);
        ctx.IsResolved.Returns(true);
        ctx.IsSystemContext.Returns(false);
        return ctx;
    }

    private AppDbContext Db(Guid tenantId) => TestDbContextFactory.Create(Tenant(tenantId), _dbName);

    private SalaryAssignmentService Assignments(Guid tenantId)
    {
        var ctx = Tenant(tenantId);
        var db = TestDbContextFactory.Create(ctx, _dbName);
        return new SalaryAssignmentService(db, ctx, _currentUser, Substitute.For<ILogger<SalaryAssignmentService>>());
    }

    private SalaryComponentService Components(Guid tenantId)
    {
        var ctx = Tenant(tenantId);
        return new SalaryComponentService(TestDbContextFactory.Create(ctx, _dbName), ctx, _currentUser,
            Substitute.For<IPayrollAuditLogger>(), Substitute.For<ILogger<SalaryComponentService>>());
    }

    private SalaryStructureService Structures(Guid tenantId)
    {
        var ctx = Tenant(tenantId);
        return new SalaryStructureService(TestDbContextFactory.Create(ctx, _dbName), ctx, _currentUser,
            Substitute.For<ILogger<SalaryStructureService>>());
    }

    private async Task<Guid> SeedEmployee(Guid tenantId, string no = "EMP-0001")
    {
        var db = Db(tenantId);
        var employee = new Employee
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            EmployeeNo = no,
            FirstName = "Test",
            LastName = "Employee",
            Email = $"{no.ToLower()}@t.com",
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    private async Task<Guid> SeedComponent(string code, SalaryComponentType type, CalculationMethod method, decimal? value, string? formula = null)
    {
        var input = new SalaryComponentInput(code, code, type, method, value, formula, true, false, true, 1);
        var result = await Components(_tenantId).CreateAsync(input);
        result.IsSuccess.Should().BeTrue();
        return result.Value!.Id;
    }

    /// <summary>
    /// Basic 40% of CTC, HRA 20% of Basic, Special Allowance = the residual (formula) so total earnings
    /// always equal the declared CTC at any CTC value (FR-6 passes regardless of CTC). For CTC 600,000:
    /// Basic 240,000; HRA 48,000; Special = 600,000 - 240,000 - 48,000 = 312,000.
    /// </summary>
    private async Task<Guid> SeedStandardStructure(string code = "FT", bool isActive = true)
    {
        var basic = await SeedComponent("BASIC", SalaryComponentType.Earning, CalculationMethod.PercentageOfGross, 40m);
        var hra = await SeedComponent("HRA", SalaryComponentType.Earning, CalculationMethod.PercentageOfBasic, 20m);
        // Special allowance soaks up the remainder so earnings always total the CTC.
        var special = await SeedComponent("SPECIAL", SalaryComponentType.Earning, CalculationMethod.Formula, null, "ctc - BASIC - HRA");

        var structure = await Structures(_tenantId).CreateAsync(new SalaryStructureInput(
            "Structure " + code, code, null, new DateOnly(2026, 1, 1), false, isActive,
            new[]
            {
                new SalaryStructureComponentInput(basic, null, null, 1, false),
                new SalaryStructureComponentInput(hra, null, null, 2, false),
                new SalaryStructureComponentInput(special, null, null, 3, false),
            }));
        structure.IsSuccess.Should().BeTrue();
        return structure.Value!.Id;
    }

    // ── AC-1: assignment writes component rows ────────────────────────

    [Fact]
    public async Task Assign_WritesComponentRows_FromCtcBreakdown()
    {
        var employee = await SeedEmployee(_tenantId);
        var structureId = await SeedStandardStructure();

        var result = await Assignments(_tenantId).AssignAsync(new AssignSalaryStructureInput(
            employee, structureId, new DateOnly(2026, 1, 1), 600_000m, "Initial", Array.Empty<SalaryOverrideInput>()));

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalAnnualEarnings.Should().Be(600_000m);

        var comp = await Assignments(_tenantId).GetCurrentCompensationAsync(employee);
        comp.IsSuccess.Should().BeTrue();
        comp.Value!.Components.Should().HaveCount(3);
        comp.Value.AnnualCtc.Should().Be(600_000m);
        comp.Value.Components.Single(c => c.ComponentCode == "BASIC").AnnualAmount.Should().Be(240_000m);
        comp.Value.Components.Single(c => c.ComponentCode == "HRA").MonthlyAmount.Should().Be(4_000m);
    }

    // ── AC-3: override persisted + flagged ────────────────────────────

    [Fact]
    public async Task Assign_WithOverride_PersistsOverrideValue_AndFlag()
    {
        var employee = await SeedEmployee(_tenantId);
        var structureId = await SeedStandardStructure();

        // Override HRA to a custom fixed amount (AC-3). The SPECIAL residual formula auto-absorbs the
        // difference (ctc - BASIC - HRA), so earnings still total the CTC and FR-6 passes.
        var hraId = (await Assignments(_tenantId).PreviewAsync(new PreviewCtcInput(structureId, 600_000m, Array.Empty<SalaryOverrideInput>())))
            .Value!.Components.Single(c => c.ComponentCode == "HRA").SalaryComponentId;

        var overrides = new[] { new SalaryOverrideInput(hraId, 60_000m) }; // was 48,000

        var result = await Assignments(_tenantId).AssignAsync(new AssignSalaryStructureInput(
            employee, structureId, new DateOnly(2026, 1, 1), 600_000m, null, overrides));

        result.IsSuccess.Should().BeTrue();
        var hraLine = result.Value!.Components.Single(c => c.ComponentCode == "HRA");
        hraLine.AnnualAmount.Should().Be(60_000m);
        hraLine.IsOverride.Should().BeTrue();
        result.Value.Components.Single(c => c.ComponentCode == "BASIC").IsOverride.Should().BeFalse();
    }

    // ── AC-2 / BR-1 / BR-2: future vs immediate supersession ──────────

    [Fact]
    public async Task Assign_FutureDated_DoesNotChangeCurrentCompensation()
    {
        var employee = await SeedEmployee(_tenantId);
        var structureId = await SeedStandardStructure();

        // Current assignment effective in the past.
        var past = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-2);
        (await Assignments(_tenantId).AssignAsync(new AssignSalaryStructureInput(
            employee, structureId, past, 600_000m, "Current", Array.Empty<SalaryOverrideInput>())))
            .IsSuccess.Should().BeTrue();

        // New assignment effective in the future with a different CTC.
        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(3);
        (await Assignments(_tenantId).AssignAsync(new AssignSalaryStructureInput(
            employee, structureId, future, 1_200_000m, "Raise", Array.Empty<SalaryOverrideInput>())))
            .IsSuccess.Should().BeTrue();

        // Current compensation today must still reflect the 600,000 CTC, NOT the future 1,200,000.
        var comp = await Assignments(_tenantId).GetCurrentCompensationAsync(employee);
        comp.Value!.AnnualCtc.Should().Be(600_000m);
        comp.Value.EffectiveFrom.Should().Be(past);
    }

    [Fact]
    public async Task Assign_CurrentDated_SupersedesPriorImmediately()
    {
        var employee = await SeedEmployee(_tenantId);
        var structureId = await SeedStandardStructure();

        var past = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-2);
        (await Assignments(_tenantId).AssignAsync(new AssignSalaryStructureInput(
            employee, structureId, past, 600_000m, null, Array.Empty<SalaryOverrideInput>()))).IsSuccess.Should().BeTrue();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        (await Assignments(_tenantId).AssignAsync(new AssignSalaryStructureInput(
            employee, structureId, today, 1_200_000m, "Raise", Array.Empty<SalaryOverrideInput>()))).IsSuccess.Should().BeTrue();

        var comp = await Assignments(_tenantId).GetCurrentCompensationAsync(employee);
        comp.Value!.AnnualCtc.Should().Be(1_200_000m);
        comp.Value.EffectiveFrom.Should().Be(today);
    }

    // ── BR-3: revision history captures old/new ───────────────────────

    [Fact]
    public async Task Assign_Twice_RevisionHistoryCapturesOldAndNew()
    {
        var employee = await SeedEmployee(_tenantId);
        var structureId = await SeedStandardStructure();

        var past = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-2);
        await Assignments(_tenantId).AssignAsync(new AssignSalaryStructureInput(
            employee, structureId, past, 600_000m, "Initial", Array.Empty<SalaryOverrideInput>()));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await Assignments(_tenantId).AssignAsync(new AssignSalaryStructureInput(
            employee, structureId, today, 900_000m, "Promotion", Array.Empty<SalaryOverrideInput>()));

        var history = await Assignments(_tenantId).GetRevisionHistoryAsync(employee);
        history.Value!.Should().HaveCount(2);

        var latest = history.Value[0]; // newest first
        latest.NewAnnualCtc.Should().Be(900_000m);
        latest.OldAnnualCtc.Should().Be(600_000m);
        latest.OldStructureId.Should().Be(structureId);
        latest.NewStructureId.Should().Be(structureId);
        latest.Reason.Should().Be("Promotion");
        latest.ChangedBy.Should().Be(_userId);

        var first = history.Value[1];
        first.OldAnnualCtc.Should().BeNull(); // first-ever assignment has no prior CTC
        first.OldStructureId.Should().BeNull();
    }

    // ── FR-6: CTC/sum tolerance ───────────────────────────────────────

    [Fact]
    public async Task Assign_WhenEarningsDoNotMatchCtc_IsRejected()
    {
        var employee = await SeedEmployee(_tenantId);

        // A fixed-only structure whose earnings total 600,000 regardless of the declared CTC.
        var basic = await SeedComponent("BASIC", SalaryComponentType.Earning, CalculationMethod.Fixed, 400_000m);
        var hra = await SeedComponent("HRA", SalaryComponentType.Earning, CalculationMethod.Fixed, 200_000m);
        var structure = await Structures(_tenantId).CreateAsync(new SalaryStructureInput(
            "Fixed", "FIXED", null, new DateOnly(2026, 1, 1), false, true,
            new[]
            {
                new SalaryStructureComponentInput(basic, null, null, 1, false),
                new SalaryStructureComponentInput(hra, null, null, 2, false),
            }));
        structure.IsSuccess.Should().BeTrue();

        // Structure sums to 600,000 but we declare a 700,000 CTC → mismatch beyond ±1.
        var result = await Assignments(_tenantId).AssignAsync(new AssignSalaryStructureInput(
            employee, structure.Value!.Id, new DateOnly(2026, 1, 1), 700_000m, null, Array.Empty<SalaryOverrideInput>()));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("ctc_sum_mismatch");
    }

    // ── FR-7: inactive structure ──────────────────────────────────────

    [Fact]
    public async Task Assign_InactiveStructure_Returns400()
    {
        var employee = await SeedEmployee(_tenantId);
        var structureId = await SeedStandardStructure("INACT", isActive: false);

        var result = await Assignments(_tenantId).AssignAsync(new AssignSalaryStructureInput(
            employee, structureId, new DateOnly(2026, 1, 1), 600_000m, null, Array.Empty<SalaryOverrideInput>()));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("structure_inactive");
    }

    // ── FR-3: preview does not persist ────────────────────────────────

    [Fact]
    public async Task Preview_DoesNotPersistAnyRows()
    {
        var employee = await SeedEmployee(_tenantId);
        var structureId = await SeedStandardStructure();

        var preview = await Assignments(_tenantId).PreviewAsync(new PreviewCtcInput(
            structureId, 600_000m, Array.Empty<SalaryOverrideInput>()));

        preview.IsSuccess.Should().BeTrue();
        preview.Value!.TotalAnnualEarnings.Should().Be(600_000m);

        var comp = await Assignments(_tenantId).GetCurrentCompensationAsync(employee);
        comp.IsFailure.Should().BeTrue(); // nothing was persisted
        comp.StatusCode.Should().Be(404);
    }

    // ── AC-5: cross-tenant isolation ──────────────────────────────────

    [Fact]
    public async Task GetCompensation_FromOtherTenant_ReturnsNotFound()
    {
        var employee = await SeedEmployee(_tenantId);
        var structureId = await SeedStandardStructure();
        await Assignments(_tenantId).AssignAsync(new AssignSalaryStructureInput(
            employee, structureId, new DateOnly(2026, 1, 1), 600_000m, null, Array.Empty<SalaryOverrideInput>()));

        var other = await Assignments(Guid.NewGuid()).GetCurrentCompensationAsync(employee);
        other.IsFailure.Should().BeTrue();
        other.StatusCode.Should().Be(404);
    }
}
