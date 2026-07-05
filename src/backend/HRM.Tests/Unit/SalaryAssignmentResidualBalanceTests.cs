// ============================================================================
// BUG-070: SalaryAssignmentService integration for the residual balancer.
// TC-PAY-070-10/11. Clones the SalaryAssignmentServiceTests harness (real service,
// InMemory provider, tenant-scoped query filters) and seeds a structure with FIXED
// BASIC + HRA + SPECIAL lines — a SPECIAL that CANNOT self-absorb (unlike the
// formula-driven SPECIAL in SeedStandardStructure). A partial HRA override therefore
// used to fail `ctc_sum_mismatch`; with the balancer wired into BuildBreakdown it now
// succeeds and the persisted breakdown ties to CTC. A companion test proves the
// balancer is gated on overrides.Count > 0 (a NO-override mismatch still rejects).
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

public sealed class SalaryAssignmentResidualBalanceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ICurrentUser _currentUser;

    public SalaryAssignmentResidualBalanceTests()
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
    /// A structure whose SPECIAL residual is a FIXED amount (NOT a formula) — so it does NOT self-absorb an
    /// override. BASIC 240,000 + HRA 48,000 + SPECIAL 312,000 = 600,000. Overriding HRA breaks the sum unless
    /// the residual balancer (BUG-070) soaks the delta into SPECIAL.
    /// </summary>
    private async Task<(Guid StructureId, Guid BasicId, Guid HraId, Guid SpecialId)> SeedFixedResidualStructure()
    {
        var basic = await SeedComponent("BASIC", SalaryComponentType.Earning, CalculationMethod.Fixed, 240_000m);
        var hra = await SeedComponent("HRA", SalaryComponentType.Earning, CalculationMethod.Fixed, 48_000m);
        var special = await SeedComponent("SPECIAL", SalaryComponentType.Earning, CalculationMethod.Fixed, 312_000m);

        var structure = await Structures(_tenantId).CreateAsync(new SalaryStructureInput(
            "Fixed Residual", "FIXRES", null, new DateOnly(2026, 1, 1), false, true,
            new[]
            {
                new SalaryStructureComponentInput(basic, null, null, 1, false),
                new SalaryStructureComponentInput(hra, null, null, 2, false),
                new SalaryStructureComponentInput(special, null, null, 3, false),
            }));
        structure.IsSuccess.Should().BeTrue();
        return (structure.Value!.Id, basic, hra, special);
    }

    // ── BUG-070: a partial override now succeeds and ties to CTC ─────────────
    [Fact]
    public async Task Assign_PartialOverride_ResidualBalances_Succeeds_BUG070()
    {
        var employee = await SeedEmployee(_tenantId);
        var (structureId, _, hraId, specialId) = await SeedFixedResidualStructure();

        // Override HRA up to 60,000 (was fixed 48,000). Without the balancer, earnings would total 612,000
        // against a declared 600,000 CTC → ctc_sum_mismatch. The fixed SPECIAL cannot self-absorb.
        var overrides = new[] { new SalaryOverrideInput(hraId, 60_000m) };

        var result = await Assignments(_tenantId).AssignAsync(new AssignSalaryStructureInput(
            employee, structureId, new DateOnly(2026, 1, 1), 600_000m, "Partial override", overrides));

        result.IsSuccess.Should().BeTrue("the residual balancer absorbs the delta instead of rejecting");
        result.Value!.TotalAnnualEarnings.Should().Be(600_000m);   // ties exactly to CTC

        // The returned breakdown: SPECIAL absorbed −12,000 → 300,000; HRA kept its override + flag.
        result.Value.Components.Single(c => c.SalaryComponentId == specialId).AnnualAmount.Should().Be(300_000m);
        var hraLine = result.Value.Components.Single(c => c.SalaryComponentId == hraId);
        hraLine.AnnualAmount.Should().Be(60_000m);
        hraLine.IsOverride.Should().BeTrue();

        // Persisted breakdown ties to CTC and reflects the balanced residual.
        var comp = await Assignments(_tenantId).GetCurrentCompensationAsync(employee);
        comp.IsSuccess.Should().BeTrue();
        comp.Value!.AnnualCtc.Should().Be(600_000m);
        comp.Value.Components.Single(c => c.ComponentCode == "SPECIAL").AnnualAmount.Should().Be(300_000m);
        comp.Value.Components.Single(c => c.ComponentCode == "SPECIAL").MonthlyAmount.Should().Be(25_000m);
        comp.Value.Components.Single(c => c.ComponentCode == "HRA").AnnualAmount.Should().Be(60_000m);
        comp.Value.Components.Single(c => c.ComponentCode == "BASIC").AnnualAmount.Should().Be(240_000m);
    }

    // ── Balancer is gated on overrides.Count > 0: NO-override mismatch still rejects ──
    [Fact]
    public async Task Assign_NoOverride_MismatchedCtc_StillRejected_BUG070()
    {
        var employee = await SeedEmployee(_tenantId);
        var (structureId, _, _, _) = await SeedFixedResidualStructure();

        // Same fixed structure sums to 600,000; declare 700,000 with NO overrides → the balancer must NOT run,
        // so FR-6 still fails.
        var result = await Assignments(_tenantId).AssignAsync(new AssignSalaryStructureInput(
            employee, structureId, new DateOnly(2026, 1, 1), 700_000m, null, Array.Empty<SalaryOverrideInput>()));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("ctc_sum_mismatch");
    }
}
