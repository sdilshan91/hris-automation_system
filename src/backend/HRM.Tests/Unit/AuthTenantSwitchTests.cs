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
        // Two rows by design — one audited against the SOURCE tenant, one against the TARGET. Reading them
        // needs IgnoreQueryFilters for the same reason the two RefreshToken assertions above do: this context
        // resolves to a single tenant, so a tenant-scoped read sees only half a cross-tenant switch. Explicit
        // since GAP-006 gave audit_logs the query filter it had always been missing; the assertion itself is
        // unchanged (still exactly 2), and it now verifies what it always meant to.
        assertDb.AuditLogs
            .IgnoreQueryFilters()
            .Count(log => log.EventType == "tenant_switch").Should().Be(2);
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

    /// <summary>
    /// ISSUE-055 (US-AUTH-008, AC audit): a DENIED tenant switch must leave a security-audit trail, not fail
    /// silently. Pre-fix (see <c>git show HEAD:AuthService.cs</c>) the non-member denial path returned 403 and
    /// wrote NO audit row, so the assertion below found zero rows and failed. Post-fix the denial path calls
    /// <c>WriteTenantSwitchDeniedAuditAsync</c>, emitting a <c>tenant_switch_denied</c> row attributed to the
    /// requesting user with the attempted target tenant in the detail.
    /// </summary>
    [Fact]
    public async Task SwitchTenant_Denied_WritesSecurityAudit_ISSUE055()
    {
        // Arrange: the user belongs to the SOURCE tenant but has NO membership in the TARGET (a denied switch).
        await SeedUserWithTenantsAsync(TenantStatus.Active, includeTargetMembership: false);
        var service = CreateService();

        // Act
        var result = await service.SwitchTenantAsync(
            _userId, _sourceTenantId, _targetTenantId, "203.0.113.10", "unit-test");

        // Assert: the switch is denied ...
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);

        // ... AND a security-audit row is recorded for the requesting user (the crux of ISSUE-055).
        using var db = CreateDbContext();
        var denialAudit = db.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.EventType.Contains("tenant_switch_denied") && a.UserId == _userId)
            .ToList();

        denialAudit.Should().ContainSingle(
            "a denied tenant switch must leave exactly one security-audit row for the requesting user");
        denialAudit[0].Detail.Should().NotBeNullOrWhiteSpace();
        denialAudit[0].Detail!.Should().Contain(
            _targetTenantId.ToString(),
            "the audited denial should record which target tenant was attempted");
    }

    /// <summary>
    /// ISSUE-057 (US-AUTH-008): a MEMBER of a suspended target is correctly denied, but the caller-facing
    /// message must stay generic and NOT disclose the precise <see cref="TenantStatus"/> enum value. (Updated
    /// from the pre-fix assertion, which asserted the leaking "(Suspended)" message — the very disclosure this
    /// fix removes; the audit reason retains the status for forensics, verified separately.)
    /// </summary>
    [Fact]
    public async Task SwitchTenantAsync_SuspendedTargetTenant_MemberGetsGenericMessage_NoStatusLeak_ISSUE057()
    {
        await SeedUserWithTenantsAsync(TenantStatus.Suspended); // member of the suspended target
        var service = CreateService();

        var result = await service.SwitchTenantAsync(_userId, _sourceTenantId, _targetTenantId, null, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Be("The target organization is currently unavailable.");
        result.Error.Should().NotContain(nameof(TenantStatus.Suspended));
        result.Error.Should().NotContain(nameof(TenantStatus.Terminated));
    }

    /// <summary>
    /// ISSUE-057 (US-AUTH-008): the CORE info-disclosure case. A NON-member of a suspended target must get the
    /// same generic membership error an active non-member gets — never the status-bearing message — so a
    /// caller cannot enumerate other tenants' lifecycle states. Pre-fix the status check ran BEFORE the
    /// membership check and leaked "(Suspended)" to a non-member; post-fix the membership check runs first.
    /// </summary>
    [Fact]
    public async Task SwitchTenantAsync_NonMemberOfSuspendedTenant_GenericMembershipError_NoStatusLeak_ISSUE057()
    {
        await SeedUserWithTenantsAsync(TenantStatus.Suspended, includeTargetMembership: false);
        var service = CreateService();

        var result = await service.SwitchTenantAsync(_userId, _sourceTenantId, _targetTenantId, null, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Be("You do not have an active membership in this organization.");
        result.Error.Should().NotContain(nameof(TenantStatus.Suspended));

        // The denial is audited as a membership failure (not a status-based one).
        using var db = CreateDbContext();
        var denial = db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EventType.Contains("tenant_switch_denied") && a.UserId == _userId)
            .ToList();
        denial.Should().ContainSingle("a denied switch must leave exactly one security-audit row");
        denial[0].Detail!.Should().Contain("not_a_member");
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
