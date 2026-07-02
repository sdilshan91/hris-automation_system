// ============================================================================
// BUG-121 regression: the FE-hydration path (/auth/my-tenants) must survive a
// Redis outage. GetMyTenantsAsync caches the membership list; pre-fix an
// unguarded cache read/write threw RedisConnectionException and 500'd hydration.
// Post-fix a cache failure is treated as a miss and the DB is used.
//
// Provider: EF InMemory + a deliberately-throwing IDistributedCache.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Identity;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class AuthMyTenantsResilienceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly JwtService _jwtService;

    public AuthMyTenantsResilienceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "hrm-api-test",
                ["Jwt:Audience"] = "hrm-client-test",
                ["Platform:BaseDomain"] = "yourhrm.test",
            })
            .Build();

        _jwtService = new JwtService(_configuration);
    }

    /// <summary>An IDistributedCache that fails on every operation — simulates a Redis outage.</summary>
    private sealed class ThrowingCache : IDistributedCache
    {
        private static InvalidOperationException Down() => new("simulated redis outage");
        public byte[]? Get(string key) => throw Down();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw Down();
        public void Refresh(string key) => throw Down();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw Down();
        public void Remove(string key) => throw Down();
        public Task RemoveAsync(string key, CancellationToken token = default) => throw Down();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Down();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw Down();
    }

    [Fact]
    public async Task GetMyTenants_WhenCacheIsDown_FallsBackToDatabase()
    {
        await SeedUserWithMembershipAsync();
        var service = CreateService(new ThrowingCache());

        var result = await service.GetMyTenantsAsync(_userId, _tenantId);

        // Pre-fix: the throwing cache surfaced as an unhandled exception (500). Post-fix: DB fallback.
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(t => t.TenantId == _tenantId);
    }

    private async Task SeedUserWithMembershipAsync()
    {
        using var db = CreateDbContext();

        db.Users.Add(new User { Id = _userId, Email = "u@acme.com", IsActive = true });
        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme Corp", Status = TenantStatus.Active });

        var role = new Role { Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = "HR Officer" };
        db.Roles.Add(role);

        var membership = new UserTenant
        {
            Id = BaseEntity.NewUuidV7(),
            UserId = _userId,
            TenantId = _tenantId,
            Status = UserTenantStatus.Active,
        };
        db.UserTenants.Add(membership);
        db.UserTenantRoles.Add(new UserTenantRole
        {
            UserTenantId = membership.Id,
            RoleId = role.Id,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = "unit-test",
        });

        await db.SaveChangesAsync();
    }

    private AuthService CreateService(IDistributedCache cache)
    {
        return new AuthService(
            CreateDbContext(),
            _jwtService,
            _tenantContext,
            Substitute.For<ITotpService>(),
            _configuration,
            cache,
            Substitute.For<ILogger<AuthService>>(),
            Substitute.For<Hangfire.IBackgroundJobClient>());
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);
}
