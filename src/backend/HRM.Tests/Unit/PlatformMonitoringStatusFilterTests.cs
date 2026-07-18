// ============================================================================
// US-ADM-002 / ISSUE-003: the tenant-usage dashboard status filter must reject
// an UNPARSEABLE status value with 400 (invalid_status) rather than silently
// dropping the filter and returning the full unfiltered tenant list. A blank/
// absent status returns all; a valid status filters. Same class as ISSUE-184.
// EF Core InMemory through the real service (no Testcontainers).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Monitoring.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class PlatformMonitoringStatusFilterTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    public PlatformMonitoringStatusFilterTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(Guid.NewGuid());
    }

    private AppDbContext Db()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.IsResolved.Returns(true);
        ctx.IsSystemContext.Returns(true);
        return TestDbContextFactory.Create(ctx, _dbName);
    }

    private PlatformMonitoringService Service() => new(
        Db(), _currentUser, Substitute.For<IJobQueueMonitor>(),
        Substitute.For<IConfiguration>(), Substitute.For<ILogger<PlatformMonitoringService>>());

    private async Task SeedTenantsAsync()
    {
        using var db = Db();
        db.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Subdomain = "trial-a", Name = "Trial A", Status = TenantStatus.Trial });
        db.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Subdomain = "trial-b", Name = "Trial B", Status = TenantStatus.Trial });
        db.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Subdomain = "active-a", Name = "Active A", Status = TenantStatus.Active });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetTenantUsage_UnparseableStatus_Returns400InvalidStatus()
    {
        await SeedTenantsAsync();

        var result = await Service().GetTenantUsageAsync(new TenantUsageFilter(Status: "BogusValue"));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("invalid_status");
    }

    [Fact]
    public async Task GetTenantUsage_ValidStatus_FiltersToThatStatus()
    {
        await SeedTenantsAsync();

        var result = await Service().GetTenantUsageAsync(new TenantUsageFilter(Status: "Trial"));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Tenants.Should().HaveCount(2);
        result.Value.Tenants.Should().OnlyContain(t => t.Status == "Trial");
    }

    [Fact]
    public async Task GetTenantUsage_BlankStatus_ReturnsAllTenants()
    {
        await SeedTenantsAsync();

        var result = await Service().GetTenantUsageAsync(new TenantUsageFilter(Status: null));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Tenants.Should().HaveCount(3);
    }
}
