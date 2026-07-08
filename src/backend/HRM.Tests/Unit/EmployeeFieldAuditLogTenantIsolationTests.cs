// ============================================================================
// Phase 6 "Theme-L" cleanup — EmployeeFieldAuditLog read-isolation.
//
// A defense-in-depth global query filter was added to AppDbContext:
//     modelBuilder.Entity<EmployeeFieldAuditLog>()
//         .HasQueryFilter(e => !_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId);
//
// EmployeeFieldAuditLog is NOT a BaseEntity and is NOT interceptor-stamped, so
// its TenantId is written manually by the profile-audit code. This test proves
// the new read filter genuinely isolates rows by the resolved tenant, and that
// the `!IsResolved ||` escape hatch still exposes all rows to unresolved
// (system/unit-test) contexts — the branch that keeps other unit tests green.
//
// Provider: EF Core InMemory. The InMemory provider evaluates global query
// filters as LINQ-to-objects against the live per-context ITenantContext (same
// mechanism the existing TestDbContextFactory filter tests rely on), so it
// exercises this filter faithfully with no Docker/Postgres needed.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class EmployeeFieldAuditLogTenantIsolationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    /// <summary>
    /// With a context resolved to tenant A, reading EmployeeFieldAuditLogs must return ONLY
    /// tenant A's rows — tenant B's rows are filtered out by the new global query filter.
    /// </summary>
    [Fact]
    public async Task Query_ResolvedToTenantA_ReturnsOnlyTenantARows()
    {
        // Arrange: seed two rows for A and three for B in the same InMemory store. Writes are
        // not query-filtered, so an unresolved seeding context can persist both tenants' rows.
        await SeedAsync(
            (_tenantA, "PersonalInfo"),
            (_tenantA, "Contact"),
            (_tenantB, "PersonalInfo"),
            (_tenantB, "Employment"),
            (_tenantB, "EmergencyContacts"));

        // Act: read through a context resolved to tenant A.
        using var db = TestDbContextFactory.Create(ResolvedContext(_tenantA), _dbName);
        var rows = await db.EmployeeFieldAuditLogs.AsNoTracking().ToListAsync();

        // Assert: only A's two rows are visible; B's three are filtered out entirely.
        rows.Should().HaveCount(2, "the query filter must scope reads to the resolved tenant (A)");
        rows.Should().OnlyContain(r => r.TenantId == _tenantA,
            "no tenant B row may leak into a tenant-A-resolved read");
        rows.Should().NotContain(r => r.TenantId == _tenantB,
            "tenant B's audit rows must be invisible under a tenant-A context");
    }

    /// <summary>
    /// With an UNRESOLVED context (IsResolved == false), the `!IsResolved ||` escape hatch must
    /// expose ALL rows across every tenant — the branch that keeps system/unit-test reads working.
    /// </summary>
    [Fact]
    public async Task Query_UnresolvedContext_ReturnsAllTenantsRows()
    {
        // Arrange: same 2 (A) + 3 (B) = 5 rows.
        await SeedAsync(
            (_tenantA, "PersonalInfo"),
            (_tenantA, "Contact"),
            (_tenantB, "PersonalInfo"),
            (_tenantB, "Employment"),
            (_tenantB, "EmergencyContacts"));

        // Act: read through an unresolved context.
        using var db = TestDbContextFactory.Create(UnresolvedContext(), _dbName);
        var rows = await db.EmployeeFieldAuditLogs.AsNoTracking().ToListAsync();

        // Assert: the escape hatch exposes every tenant's rows.
        rows.Should().HaveCount(5, "an unresolved context must bypass the tenant filter (escape hatch)");
        rows.Should().Contain(r => r.TenantId == _tenantA);
        rows.Should().Contain(r => r.TenantId == _tenantB);
    }

    // ---- Helpers ----------------------------------------------------------

    private async Task SeedAsync(params (Guid tenantId, string section)[] rows)
    {
        // Seed via an unresolved context so both tenants' rows persist regardless of the filter.
        using var db = TestDbContextFactory.Create(UnresolvedContext(), _dbName);
        foreach (var (tenantId, section) in rows)
        {
            db.EmployeeFieldAuditLogs.Add(new EmployeeFieldAuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = Guid.NewGuid(),
                Section = section,
                BeforeSnapshot = "{}",
                AfterSnapshot = "{}",
                ChangedBy = "hr@test.com",
                CreatedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
    }

    private static ITenantContext ResolvedContext(Guid tenantId)
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenantId);
        ctx.IsResolved.Returns(true);
        ctx.IsSystemContext.Returns(false);
        return ctx;
    }

    private static ITenantContext UnresolvedContext()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(Guid.Empty);
        ctx.IsResolved.Returns(false);
        ctx.IsSystemContext.Returns(false);
        return ctx;
    }
}
