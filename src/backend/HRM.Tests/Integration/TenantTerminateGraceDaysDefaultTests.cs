// ============================================================================
// BUG-002 regression (tenant TERMINATE grace-days default).
//
// INVERSE-shape finding: terminating a tenant with an OMITTED grace period must now
// SUCCEED with the default (30 days) applied — pre-fix an omitted grace period was
// rejected with 400. The fix models "omitted" as a nullable `int? GraceDays` (null ⇒
// apply the 30-day default; a supplied value is still range-checked 7-90). This test
// asserts SUCCESS + that the default was applied (TerminationScheduledAt ≈ now + 30
// days), not merely "no 400". Pre-fix (HEAD, non-nullable `int GraceDays`) an omitted
// body deserialized to 0 and was rejected by the `GraceDays < 7` guard, so this case
// FAILS pre-fix and PASSES post-fix.
//
// SEAM NOTE: the only "terminate + graceDays (default 30, range 7-90)" seam in the
// codebase is TenantLifecycleService.TerminateAsync (US-ADM-004). The employee
// EmployeeStatusService (US-CHR-009, the id the finding is tagged with) terminate has
// no graceDays concept — this regression is bound to the real tenant-lifecycle seam.
//
// InMemory, system/admin context — mirrors TenantLifecycleIntegrationTests.
// Traceability: @BUG-002.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Tenants.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class TenantTerminateGraceDaysDefaultTests
{
    private const int DefaultGraceDays = 30;
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _adminId = Guid.NewGuid();

    // ── Omitted grace period → default (30) applied, SUCCEEDS ────────────────

    [Fact]
    public async Task TerminateTenant_OmittedGraceDays_AppliesDefault_BUG002()
    {
        var tenantId = await SeedActiveTenantAsync("acme");
        var service = Service();

        // GraceDays: null == an omitted grace period (the service applies the plan default).
        var result = await service.TerminateAsync(
            new TerminateTenantInput(tenantId, "Non-payment after dunning cycle.", GraceDays: null));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Status.Should().Be(nameof(TenantStatus.Terminating));
        result.Value.TerminationScheduledAt.Should().NotBeNull();

        using var db = Db();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenantId);
        tenant.Status.Should().Be(TenantStatus.Terminating);
        // The default 30-day grace was applied — deletion is scheduled ~now + 30 days.
        tenant.TerminationScheduledAt.Should()
            .BeCloseTo(DateTime.UtcNow.AddDays(DefaultGraceDays), TimeSpan.FromMinutes(2));
    }

    // ── Control: an explicit valid grace period still works ──────────────────

    [Fact]
    public async Task TerminateTenant_ExplicitValidGraceDays_Succeeds_BUG002()
    {
        var tenantId = await SeedActiveTenantAsync("beta");
        var service = Service();

        var result = await service.TerminateAsync(
            new TerminateTenantInput(tenantId, "Non-payment after dunning cycle.", GraceDays: 14));

        result.IsSuccess.Should().BeTrue(result.Error);

        using var db = Db();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenantId);
        tenant.Status.Should().Be(TenantStatus.Terminating);
        tenant.TerminationScheduledAt.Should()
            .BeCloseTo(DateTime.UtcNow.AddDays(14), TimeSpan.FromMinutes(2));
    }

    // ── Helpers (mirror TenantLifecycleIntegrationTests) ─────────────────────

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

    private TenantLifecycleService Service()
    {
        var notify = Substitute.For<ITenantLifecycleNotificationService>();
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(_adminId);
        user.IsAuthenticated.Returns(true);
        var config = new ConfigurationBuilder().Build(); // system tenant subdomain defaults to "platform"
        return new TenantLifecycleService(
            Db(), user, notify, config, NullLogger<TenantLifecycleService>.Instance, scheduler: null);
    }

    private async Task<Guid> SeedActiveTenantAsync(string subdomain)
    {
        var id = BaseEntity.NewUuidV7();
        using var db = Db();
        db.Tenants.Add(new Tenant
        {
            Id = id,
            Subdomain = subdomain,
            Name = subdomain,
            Status = TenantStatus.Active,
            PlanId = "starter",
            BillingEmail = $"billing@{subdomain}.test",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }
}
