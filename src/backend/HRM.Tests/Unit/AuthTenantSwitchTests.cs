using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Identity;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class AuthTenantSwitchTests
{
    private readonly Guid _sourceTenantId = Guid.NewGuid();
    private readonly Guid _targetTenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly JwtService _jwtService;

    public AuthTenantSwitchTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_sourceTenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

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

    [Fact]
    public async Task GetMyTenantsAsync_ReturnsAllMembershipsWithCurrentTenantFlag()
    {
        await SeedUserWithTenantsAsync(TenantStatus.Suspended);
        var service = CreateService();

        var result = await service.GetMyTenantsAsync(_userId, _sourceTenantId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value!.Single(t => t.TenantId == _sourceTenantId).IsCurrentTenant.Should().BeTrue();
        result.Value!.Single(t => t.TenantId == _targetTenantId).Status.Should().Be(nameof(TenantStatus.Suspended));
        result.Value!.Single(t => t.TenantId == _targetTenantId).Roles.Should().ContainSingle("Target Viewer");
    }

    [Fact]
    public async Task SwitchTenantAsync_ActiveMembership_IssuesTargetScopedTokenAndPreservesSourceSession()
    {
        var sourceRoleId = await SeedUserWithTenantsAsync(TenantStatus.Active);
        using (var db = CreateDbContext())
        {
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = BaseEntity.NewUuidV7(),
                UserId = _userId,
                TenantId = _sourceTenantId,
                TokenHash = _jwtService.HashToken("source-refresh-token"),
                IssuedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddDays(7),
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        var result = await service.SwitchTenantAsync(
            _userId,
            _sourceTenantId,
            _targetTenantId,
            "127.0.0.1",
            "unit-test");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Tenant.TenantId.Should().Be(_targetTenantId);
        result.Value.RedirectUrl.Should().Be("https://target.yourhrm.test/dashboard");
        result.Value.RefreshToken.Should().NotBeNullOrWhiteSpace();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Value.AccessToken);
        jwt.Claims.Single(c => c.Type == "tenant_id").Value.Should().Be(_targetTenantId.ToString());
        jwt.Claims.Where(c => c.Type == "roles").Select(c => c.Value)
            .Should().ContainSingle("Target Viewer")
            .And.NotContain("Source Admin");
        jwt.Claims.Where(c => c.Type == "permissions").Select(c => c.Value)
            .Should().ContainSingle("Reports.View")
            .And.NotContain("Payroll.View");

        using var assertDb = CreateDbContext();
        assertDb.RefreshTokens
            .IgnoreQueryFilters()
            .Single(rt => rt.UserId == _userId && rt.TenantId == _sourceTenantId)
            .RevokedAt.Should().BeNull();
        assertDb.RefreshTokens
            .IgnoreQueryFilters()
            .Any(rt => rt.UserId == _userId && rt.TenantId == _targetTenantId && rt.RevokedAt == null)
            .Should().BeTrue();
        assertDb.AuditLogs.Count(log => log.EventType == "tenant_switch").Should().Be(2);
        assertDb.Roles.Single(r => r.Id == sourceRoleId).Name.Should().Be("Source Admin");
    }

    [Fact]
    public async Task SwitchTenantAsync_WithoutActiveMembership_ReturnsForbidden()
    {
        await SeedUserWithTenantsAsync(TenantStatus.Active, includeTargetMembership: false);
        var service = CreateService();

        var result = await service.SwitchTenantAsync(_userId, _sourceTenantId, _targetTenantId, null, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Be("You do not have an active membership in this organization.");
    }

    [Fact]
    public async Task SwitchTenantAsync_SuspendedTargetTenant_ReturnsUnavailableForbidden()
    {
        await SeedUserWithTenantsAsync(TenantStatus.Suspended);
        var service = CreateService();

        var result = await service.SwitchTenantAsync(_userId, _sourceTenantId, _targetTenantId, null, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("target organization is unavailable");
        result.Error.Should().Contain(nameof(TenantStatus.Suspended));
    }

    private AuthService CreateService()
    {
        return new AuthService(
            CreateDbContext(),
            _jwtService,
            _tenantContext,
            Substitute.For<ITotpService>(),
            _configuration,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Substitute.For<ILogger<AuthService>>(),
            Substitute.For<Hangfire.IBackgroundJobClient>());
    }

    private AppDbContext CreateDbContext()
    {
        return TestDbContextFactory.Create(_tenantContext, _dbName);
    }

    private async Task<Guid> SeedUserWithTenantsAsync(
        TenantStatus targetStatus,
        bool includeTargetMembership = true)
    {
        using var db = CreateDbContext();

        var user = new User
        {
            Id = _userId,
            Email = "cross-tenant@test.local",
            DisplayName = "Cross Tenant User",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        var sourceTenant = new Tenant
        {
            Id = _sourceTenantId,
            Name = "Source Tenant",
            Subdomain = "source",
            Status = TenantStatus.Active,
            CreatedAt = DateTime.UtcNow,
        };

        var targetTenant = new Tenant
        {
            Id = _targetTenantId,
            Name = "Target Tenant",
            Subdomain = "target",
            Status = targetStatus,
            LogoUrl = "https://assets.test/target.png",
            CreatedAt = DateTime.UtcNow,
        };

        var sourceRole = new Role
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _sourceTenantId,
            Name = "Source Admin",
            CreatedAt = DateTime.UtcNow,
        };
        sourceRole.RolePermissions.Add(new RolePermission { RoleId = sourceRole.Id, Permission = "Payroll.View" });

        var targetRole = new Role
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _targetTenantId,
            Name = "Target Viewer",
            CreatedAt = DateTime.UtcNow,
        };
        targetRole.RolePermissions.Add(new RolePermission { RoleId = targetRole.Id, Permission = "Reports.View" });

        db.Users.Add(user);
        db.Tenants.AddRange(sourceTenant, targetTenant);
        db.Roles.AddRange(sourceRole, targetRole);

        var sourceMembership = new UserTenant
        {
            Id = BaseEntity.NewUuidV7(),
            UserId = _userId,
            TenantId = _sourceTenantId,
            Status = UserTenantStatus.Active,
            CreatedAt = DateTime.UtcNow,
        };
        db.UserTenants.Add(sourceMembership);
        db.UserTenantRoles.Add(new UserTenantRole
        {
            UserTenantId = sourceMembership.Id,
            RoleId = sourceRole.Id,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = "unit-test",
        });

        if (includeTargetMembership)
        {
            var targetMembership = new UserTenant
            {
                Id = BaseEntity.NewUuidV7(),
                UserId = _userId,
                TenantId = _targetTenantId,
                Status = UserTenantStatus.Active,
                CreatedAt = DateTime.UtcNow,
            };
            db.UserTenants.Add(targetMembership);
            db.UserTenantRoles.Add(new UserTenantRole
            {
                UserTenantId = targetMembership.Id,
                RoleId = targetRole.Id,
                AssignedAt = DateTime.UtcNow,
                AssignedBy = "unit-test",
            });
        }

        await db.SaveChangesAsync();
        return sourceRole.Id;
    }
}
