// ============================================================================
// ISSUE-291 — DEC-1 continuity backfill of report row-scope onto CUSTOM roles.
//
// Verifies DbInitializer.BackfillCustomRoleReportScopeAsync: a custom (tenant-defined) role that
// held the Reports.View endpoint gate plus a cross-module *.View.All / *.View.Team signal is given
// the matching dedicated report-scope permission (Reports.View.All / Reports.View.Team), preserving
// the row scope the old borrowed-perm resolver gave it. "All" wins over "Team" and the two grants are
// mutually exclusive. Roles without Reports.View, and built-in roles, are untouched. Idempotent.
//
// PROVIDER: EF InMemory through the real AppDbContext (the verify gate runs `dotnet test` with no
// PostgreSQL / Docker). The backfill only ADDS rows and reads via IgnoreQueryFilters, so InMemory is
// faithful here.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRM.Tests.Unit;

public sealed class CustomRoleReportScopeBackfillTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantId = Guid.NewGuid();

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
        public string Subdomain => "acme";
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

    private AppDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, new FixedTenantContext(_tenantId));
    }

    /// <summary>Seeds a role in the tenant with the given held permissions and returns its id.</summary>
    private async Task<Guid> SeedRoleAsync(bool isBuiltIn, params string[] permissions)
    {
        using var db = Db();
        var role = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = isBuiltIn ? "Some Built-In" : "Custom Role " + Guid.NewGuid().ToString("N")[..6],
            IsBuiltIn = isBuiltIn,
            RolePermissions = permissions
                .Select(p => new RolePermission { Permission = p })
                .ToList(),
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    /// <summary>Seeds a role belonging to a DIFFERENT tenant (for cross-tenant isolation checks).</summary>
    private async Task<Guid> SeedRoleForTenantAsync(Guid tenantId, bool isBuiltIn, params string[] permissions)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, new FixedTenantContext(tenantId));
        var role = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Other-Tenant Custom Role " + Guid.NewGuid().ToString("N")[..6],
            IsBuiltIn = isBuiltIn,
            RolePermissions = permissions.Select(p => new RolePermission { Permission = p }).ToList(),
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    private async Task RunBackfillAsync()
    {
        using var db = Db();
        await DbInitializer.BackfillCustomRoleReportScopeAsync(
            db, _tenantId, NullLogger.Instance, CancellationToken.None);
    }

    private async Task<HashSet<string>> HeldPermsAsync(Guid roleId)
    {
        using var db = Db();
        var perms = await db.Set<RolePermission>()
            .IgnoreQueryFilters()
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.Permission)
            .ToListAsync();
        return perms.ToHashSet();
    }

    [Fact]
    public async Task CustomRole_WithReportsView_AndEmployeeViewTeam_Gets_ReportsViewTeam_NotAll()
    {
        var roleId = await SeedRoleAsync(false,
            PermissionCatalog.Reports.View, PermissionCatalog.Employee.ViewTeam);

        await RunBackfillAsync();

        var held = await HeldPermsAsync(roleId);
        held.Should().Contain(PermissionCatalog.Reports.ViewTeam);
        held.Should().NotContain(PermissionCatalog.Reports.ViewAll);
    }

    [Fact]
    public async Task CustomRole_WithReportsView_AndEmployeeViewAll_Gets_ReportsViewAll()
    {
        var roleId = await SeedRoleAsync(false,
            PermissionCatalog.Reports.View, PermissionCatalog.Employee.ViewAll);

        await RunBackfillAsync();

        var held = await HeldPermsAsync(roleId);
        held.Should().Contain(PermissionCatalog.Reports.ViewAll);
        held.Should().NotContain(PermissionCatalog.Reports.ViewTeam);
    }

    [Fact]
    public async Task CustomRole_WithBothAllAndTeamSignals_Gets_ReportsViewAll_Only()
    {
        var roleId = await SeedRoleAsync(false,
            PermissionCatalog.Reports.View,
            PermissionCatalog.Employee.ViewAll,
            PermissionCatalog.Leave.ViewTeam);

        await RunBackfillAsync();

        var held = await HeldPermsAsync(roleId);
        held.Should().Contain(PermissionCatalog.Reports.ViewAll);
        held.Should().NotContain(PermissionCatalog.Reports.ViewTeam);
    }

    [Fact]
    public async Task CustomRole_WithoutReportsView_IsUntouched()
    {
        var roleId = await SeedRoleAsync(false, PermissionCatalog.Employee.ViewAll);

        await RunBackfillAsync();

        var held = await HeldPermsAsync(roleId);
        held.Should().NotContain(PermissionCatalog.Reports.ViewAll);
        held.Should().NotContain(PermissionCatalog.Reports.ViewTeam);
        held.Should().BeEquivalentTo(new[] { PermissionCatalog.Employee.ViewAll });
    }

    [Fact]
    public async Task BuiltInRole_IsUntouched_ByThisMethod()
    {
        var roleId = await SeedRoleAsync(true,
            PermissionCatalog.Reports.View, PermissionCatalog.Employee.ViewAll);

        await RunBackfillAsync();

        var held = await HeldPermsAsync(roleId);
        held.Should().NotContain(PermissionCatalog.Reports.ViewAll);
        held.Should().NotContain(PermissionCatalog.Reports.ViewTeam);
    }

    [Fact]
    public async Task Backfill_IsIdempotent_NoDuplicateGrant_OnSecondRun()
    {
        var roleId = await SeedRoleAsync(false,
            PermissionCatalog.Reports.View, PermissionCatalog.Employee.ViewTeam);

        await RunBackfillAsync();
        await RunBackfillAsync();

        using var db = Db();
        var teamGrants = await db.Set<RolePermission>()
            .IgnoreQueryFilters()
            .CountAsync(rp => rp.RoleId == roleId && rp.Permission == PermissionCatalog.Reports.ViewTeam);
        teamGrants.Should().Be(1);
    }

    [Fact]
    public async Task Backfill_ForOneTenant_LeavesAnotherTenantsQualifyingRoleUntouched()
    {
        // Tenant isolation (this platform's #1 invariant, BUG-003 class): a fully-qualifying custom role
        // in ANOTHER tenant must NOT be granted anything when the backfill runs for _tenantId. This arm
        // catches a mutation that drops the `r.TenantId == tenantId` filter (a cross-tenant over-grant).
        var otherTenantId = Guid.NewGuid();
        var otherRoleId = await SeedRoleForTenantAsync(otherTenantId, isBuiltIn: false,
            PermissionCatalog.Reports.View, PermissionCatalog.Employee.ViewAll);

        await RunBackfillAsync(); // runs for _tenantId only

        var held = await HeldPermsAsync(otherRoleId);
        held.Should().NotContain(PermissionCatalog.Reports.ViewAll);
        held.Should().NotContain(PermissionCatalog.Reports.ViewTeam);
        held.Should().BeEquivalentTo(new[]
        {
            PermissionCatalog.Reports.View, PermissionCatalog.Employee.ViewAll,
        });
    }
}
