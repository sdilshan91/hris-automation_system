using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Identity;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace HRM.Tests.Unit;

/// <summary>
/// ISSUE-058 (US-AUTH-009, AC-5 admin revoke): the audit row for an ADMIN-initiated session revoke must be
/// attributed to the ACTING ADMIN, not to the victim (the session owner). The victim + revoked session id are
/// carried in the audit detail so the trail still records WHOSE session was ended.
///
/// <para>Pre-fix (see <c>git show HEAD:AuthService.cs</c>) <c>RevokeSessionAsync</c> called
/// <c>WriteAuditLogAsync(userId, ...)</c> where <c>userId</c> is the victim — so the audit actor was the victim
/// and no detail was recorded. Both assertions below (actor == admin, and victim/session present in detail)
/// therefore failed pre-fix. Post-fix the actor is <c>_currentUser.UserId</c> for admin revokes.</para>
/// </summary>
public sealed class AuthSessionRevokeAuditTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _adminUserId = Guid.NewGuid();  // the acting admin (audit actor)
    private readonly Guid _victimUserId = Guid.NewGuid();  // the user whose session is revoked
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IConfiguration _configuration;
    private readonly JwtService _jwtService;

    public AuthSessionRevokeAuditTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        // The acting admin driving the revoke — a DIFFERENT user from the victim.
        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_adminUserId);
        _currentUser.TenantId.Returns(_tenantId);
        _currentUser.IsAuthenticated.Returns(true);

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
    public async Task RevokeSession_ByAdmin_AuditActorIsAdmin_ISSUE058()
    {
        // Arrange: seed the tenant, the victim user, and one active session owned by the victim.
        await SeedTenantAndVictimAsync();
        var sessionId = await SeedActiveSessionAsync(_victimUserId);
        var service = CreateService();

        // Act: the acting admin (_currentUser) revokes the victim's session.
        var result = await service.RevokeSessionAsync(
            sessionId, _victimUserId, _tenantId, currentSessionId: null, isAdminAction: true, default);

        // Assert: revoke succeeds and the session is actually revoked.
        result.IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        db.RefreshTokens.IgnoreQueryFilters().Single(rt => rt.Id == sessionId)
            .RevokedAt.Should().NotBeNull();

        var audit = db.AuditLogs
            .IgnoreQueryFilters()
            .Single(a => a.EventType == "session_revoked_by_admin");

        // ISSUE-058 core: the audit actor is the ACTING ADMIN, not the victim.
        audit.UserId.Should().Be(_adminUserId, "the admin-revoke audit must be attributed to the acting admin");
        audit.UserId.Should().NotBe(_victimUserId, "the victim must not be recorded as the actor of their own forced revoke");

        // The victim + revoked session must still be identifiable from the audit detail.
        audit.Detail.Should().NotBeNullOrWhiteSpace();
        audit.Detail!.Should().Contain(_victimUserId.ToString(), "the revoked user must remain traceable in the audit detail");
        audit.Detail!.Should().Contain(sessionId.ToString(), "the revoked session must remain traceable in the audit detail");
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
            Substitute.For<Hangfire.IBackgroundJobClient>(),
            _currentUser);
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private async Task SeedTenantAndVictimAsync()
    {
        using var db = CreateDbContext();

        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Test Tenant",
            Subdomain = "testco",
            Status = TenantStatus.Active,
            CreatedAt = DateTime.UtcNow,
        });

        db.Users.Add(new User
        {
            Id = _victimUserId,
            Email = "victim@test.local",
            DisplayName = "Victim User",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedActiveSessionAsync(Guid userId)
    {
        using var db = CreateDbContext();
        var sessionId = BaseEntity.NewUuidV7();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = sessionId,
            UserId = userId,
            TenantId = _tenantId,
            TokenHash = "victim-session-hash-" + Guid.NewGuid(),
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            LastActiveAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return sessionId;
    }
}
