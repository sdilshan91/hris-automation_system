// ============================================================================
// ISSUE-002 (US-ADM-002 AC-2 / US-ADM-001 AC-4) — regression.
//
// The System Admin tenant directory (GET /api/v1/system/tenants) must honor an
// optional case-insensitive ?search= over tenant name/subdomain. PRE-FIX the
// service signature was ListTenantsAsync(CancellationToken) and the query/handler
// dispatched a parameterless ListTenantsQuery(), so any ?search= was silently
// discarded and the FULL tenant list was returned. POST-FIX ListTenantsAsync takes
// an optional search that filters on name/subdomain with ToLower().Contains(...).
//
// This test drives the REAL TenantProvisioningService over a real AppDbContext
// (InMemory) in the system/admin context (IgnoreQueryFilters, no resolved tenant) —
// the ToLower()/Contains filter translates on both Npgsql and the InMemory provider.
//
// RED PRE-FIX: with search="acme" the pre-fix service returns BOTH tenants → the
// "only Acme" assertion fails. GREEN POST-FIX: only Acme is returned.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class TenantDirectorySearchIssue002Tests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    // The tenant directory is read in the system/admin context: no tenant is resolved and the service
    // uses IgnoreQueryFilters to list ACROSS tenants.
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

    private AppDbContext Db() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options,
        new SystemTenantContext());

    private TenantProvisioningService Service()
    {
        var email = Substitute.For<ITenantWelcomeEmailService>();
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(Guid.NewGuid());
        user.IsAuthenticated.Returns(true);
        user.Email.Returns("sysadmin@platform.test");
        var config = new ConfigurationBuilder().Build();
        return new TenantProvisioningService(
            Db(), user, email, config, NullLogger<TenantProvisioningService>.Instance);
    }

    private async Task SeedTwoTenantsAsync()
    {
        using var db = Db();
        db.Tenants.AddRange(
            new Tenant
            {
                Id = Guid.NewGuid(), Name = "Acme", Subdomain = "acme",
                Status = TenantStatus.Active, IsDeleted = false, CreatedAt = DateTime.UtcNow,
            },
            new Tenant
            {
                Id = Guid.NewGuid(), Name = "Globex", Subdomain = "globex",
                Status = TenantStatus.Active, IsDeleted = false, CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            });
        await db.SaveChangesAsync();
    }

    // ── Focused regression: search filters the list (case-insensitive) ──────────

    [Fact]
    public async Task ListTenants_Search_FiltersCaseInsensitive_ISSUE002()
    {
        await SeedTwoTenantsAsync();

        // Documented ?search= — lowercase term against the "Acme" tenant proves case-insensitivity.
        var result = await Service().ListTenantsAsync("acme");

        result.IsSuccess.Should().BeTrue(result.Error);
        // PRE-FIX: search ignored → both tenants returned → this fails.
        result.Value!.Should().ContainSingle();
        result.Value![0].Name.Should().Be("Acme");
        result.Value.Select(t => t.Subdomain).Should().NotContain("globex");
    }

    // ── Control: empty search returns ALL (proves the seed has both rows) ────────

    [Fact]
    public async Task ListTenants_EmptySearch_ReturnsAll_ISSUE002()
    {
        await SeedTwoTenantsAsync();

        var result = await Service().ListTenantsAsync(null);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Select(t => t.Subdomain).Should().BeEquivalentTo(new[] { "acme", "globex" });
    }
}
