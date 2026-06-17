// ============================================================================
// US-ADM-004 (AC-4 / FR-3 / NFR-4): tenant data-deletion service integration tests.
//
//   - Hard-deletes a terminating tenant's per-tenant rows, retains the tenant row as Terminated with PII
//     redacted, and retains audit/lifecycle rows.
//   - TENANT ISOLATION (Test Hints): deleting Tenant A leaves Tenant B's data completely intact.
//   - Idempotency/guard: a tenant NOT in Terminating status is not deleted (no-op failure).
//
// PROVIDER: InMemory — the service's provider-aware path uses load+RemoveRange here (ExecuteDelete/transactions
// are relational-only), which is exactly the core logic we assert.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRM.Tests.Integration;

public sealed class TenantDataDeletionIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private sealed class SystemTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public string Subdomain => "admin";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => true;
        public bool IsResolved => false;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }

    private AppDbContext Db()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options,
            new SystemTenantContext());

    private TenantDataDeletionService Service()
        => new(Db(), NullLogger<TenantDataDeletionService>.Instance);

    private async Task<Guid> SeedTenantWithDataAsync(string subdomain, TenantStatus status, int leaveTypeCount)
    {
        var tenantId = BaseEntity.NewUuidV7();
        using var db = Db();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Subdomain = subdomain, Name = subdomain, Status = status,
            PlanId = "starter", ContactEmail = $"contact@{subdomain}.test",
            BillingEmail = $"billing@{subdomain}.test", CreatedAt = DateTime.UtcNow,
        });
        for (var i = 0; i < leaveTypeCount; i++)
        {
            db.LeaveTypes.Add(new LeaveType
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
                Name = $"Leave {i}", Code = $"L{i}", Color = "#4CAF50",
                AnnualEntitlement = 10m, AccrualFrequency = AccrualFrequency.Upfront,
                Gender = LeaveTypeGender.All, SystemCategory = LeaveTypeSystemCategory.None,
                DisplayOrder = i, IsActive = true, CreatedAt = DateTime.UtcNow, CreatedBy = "seed",
            });
        }
        await db.SaveChangesAsync();
        return tenantId;
    }

    [Fact]
    public async Task Delete_TerminatingTenant_HardDeletesDataRedactsTenantAndIsolatesOthers()
    {
        var tenantA = await SeedTenantWithDataAsync("acme", TenantStatus.Terminating, leaveTypeCount: 3);
        var tenantB = await SeedTenantWithDataAsync("globex", TenantStatus.Active, leaveTypeCount: 2);

        var result = await Service().DeleteTenantDataAsync(tenantA);

        result.IsSuccess.Should().BeTrue(result.Error);

        using var db = Db();
        // Tenant A's per-tenant data is gone.
        (await db.LeaveTypes.IgnoreQueryFilters().CountAsync(lt => lt.TenantId == tenantA)).Should().Be(0);
        // Tenant A row retained, Terminated, PII redacted.
        var a = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenantA);
        a.Status.Should().Be(TenantStatus.Terminated);
        a.Name.Should().Be("[redacted]");
        a.ContactEmail.Should().BeNull();
        a.BillingEmail.Should().BeNull();
        // "terminated" lifecycle event + audit retained.
        (await db.TenantLifecycleEvents.IgnoreQueryFilters()
            .AnyAsync(e => e.TenantId == tenantA && e.EventType == "terminated")).Should().BeTrue();
        (await db.AuditLogs.IgnoreQueryFilters()
            .AnyAsync(l => l.TenantId == tenantA && l.Action == "Tenant.Terminated")).Should().BeTrue();

        // ISOLATION: Tenant B is completely untouched.
        (await db.LeaveTypes.IgnoreQueryFilters().CountAsync(lt => lt.TenantId == tenantB)).Should().Be(2);
        var b = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenantB);
        b.Status.Should().Be(TenantStatus.Active);
        b.Name.Should().Be("globex");
    }

    [Fact]
    public async Task Delete_NonTerminatingTenant_IsNoOp()
    {
        var tenantId = await SeedTenantWithDataAsync("acme", TenantStatus.Active, leaveTypeCount: 2);

        var result = await Service().DeleteTenantDataAsync(tenantId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("not_terminating");
        // Data untouched.
        (await Db().LeaveTypes.IgnoreQueryFilters().CountAsync(lt => lt.TenantId == tenantId)).Should().Be(2);
    }
}
