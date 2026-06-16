// ============================================================================
// US-PAY-007: PayrollAdjustmentResolver — unit tests.
//
// The resolver is the seam the payroll-run engine calls (FR-3 pickup + FR-4 mark-Applied). These tests
// exercise it directly against an InMemory AppDbContext (no MediatR), covering:
//   - FR-3: only Pending adjustments for the (period) are returned, grouped by employee.
//   - FR-3: Applied/Cancelled adjustments and other-period adjustments are excluded.
//   - FR-3: NO adjustments → empty map (the additive no-op guarantee the run engine relies on).
//   - FR-4: MarkAppliedAsync flips only Pending rows and stamps the run id (idempotent).
//   - AC-5: tenant isolation — a resolver bound to Tenant B never sees Tenant A's adjustments.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace HRM.Tests.Unit;

public sealed class PayrollAdjustmentResolverTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId { get; init; }
        public string Subdomain => "test";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }

    private AppDbContext Db(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, new FixedTenantContext { TenantId = tenantId });
    }

    private PayrollAdjustment Adj(Guid tenantId, Guid empId, AdjustmentType type, decimal amount,
        int month, int year, AdjustmentStatus status = AdjustmentStatus.Pending, bool taxable = false)
        => new()
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = empId,
            AdjustmentType = type, Amount = amount, Description = $"{type}", IsTaxable = taxable,
            ApplicablePayMonth = month, ApplicablePayYear = year, Status = status,
        };

    private async Task Seed(params PayrollAdjustment[] rows)
    {
        using var db = Db(_tenantA);
        db.PayrollAdjustments.AddRange(rows);
        await db.SaveChangesAsync();
    }

    // ── FR-3: only Pending, only the period, grouped by employee ────────────────

    [Fact]
    public async Task ResolveForPeriod_ReturnsOnlyPendingForThePeriod_GroupedByEmployee()
    {
        var emp1 = Guid.NewGuid();
        var emp2 = Guid.NewGuid();
        await Seed(
            Adj(_tenantA, emp1, AdjustmentType.Bonus, 1000m, 6, 2026),
            Adj(_tenantA, emp1, AdjustmentType.Deduction, 200m, 6, 2026),
            Adj(_tenantA, emp2, AdjustmentType.Reimbursement, 300m, 6, 2026),
            Adj(_tenantA, emp1, AdjustmentType.Bonus, 999m, 6, 2026, AdjustmentStatus.Applied),   // excluded (Applied).
            Adj(_tenantA, emp1, AdjustmentType.Bonus, 888m, 6, 2026, AdjustmentStatus.Cancelled), // excluded (Cancelled).
            Adj(_tenantA, emp1, AdjustmentType.Bonus, 777m, 7, 2026));                            // excluded (other period).

        using var db = Db(_tenantA);
        var resolver = new PayrollAdjustmentResolver(db, new FixedTenantContext { TenantId = _tenantA });

        var map = await resolver.ResolveForPeriodAsync(2026, 6);

        map.Should().HaveCount(2);
        map[emp1].Adjustments.Should().HaveCount(2);
        map[emp2].Adjustments.Should().ContainSingle();
    }

    // ── FR-3: no adjustments → empty map (the additive no-op guarantee) ─────────

    [Fact]
    public async Task ResolveForPeriod_NoAdjustments_ReturnsEmptyMap()
    {
        using var db = Db(_tenantA);
        var resolver = new PayrollAdjustmentResolver(db, new FixedTenantContext { TenantId = _tenantA });

        var map = await resolver.ResolveForPeriodAsync(2026, 6);

        map.Should().BeEmpty();
    }

    // ── FR-4: MarkApplied flips only Pending rows + stamps the run id ───────────

    [Fact]
    public async Task MarkApplied_FlipsPendingToApplied_AndStampsRunId()
    {
        var emp = Guid.NewGuid();
        var pending = Adj(_tenantA, emp, AdjustmentType.Bonus, 1000m, 6, 2026);
        var alreadyApplied = Adj(_tenantA, emp, AdjustmentType.Bonus, 500m, 6, 2026, AdjustmentStatus.Applied);
        await Seed(pending, alreadyApplied);

        var runId = Guid.NewGuid();
        using (var db = Db(_tenantA))
        {
            var resolver = new PayrollAdjustmentResolver(db, new FixedTenantContext { TenantId = _tenantA });
            await resolver.MarkAppliedAsync(new[] { pending.Id, alreadyApplied.Id }, runId);
            await db.SaveChangesAsync();
        }

        using var verify = Db(_tenantA);
        var p = await verify.PayrollAdjustments.FirstAsync(a => a.Id == pending.Id);
        p.Status.Should().Be(AdjustmentStatus.Applied);
        p.AppliedInPayrollRunId.Should().Be(runId);

        // The already-Applied row is untouched (it keeps its null run id from seeding — idempotent).
        var a = await verify.PayrollAdjustments.FirstAsync(x => x.Id == alreadyApplied.Id);
        a.AppliedInPayrollRunId.Should().BeNull();
    }

    // ── AC-5: tenant isolation ──────────────────────────────────────────────────

    [Fact]
    public async Task ResolveForPeriod_TenantB_DoesNotSeeTenantAAdjustments()
    {
        var emp = Guid.NewGuid();
        await Seed(Adj(_tenantA, emp, AdjustmentType.Bonus, 1000m, 6, 2026));

        using var db = Db(_tenantB);
        var resolver = new PayrollAdjustmentResolver(db, new FixedTenantContext { TenantId = _tenantB });

        var map = await resolver.ResolveForPeriodAsync(2026, 6);

        map.Should().BeEmpty();
    }
}
