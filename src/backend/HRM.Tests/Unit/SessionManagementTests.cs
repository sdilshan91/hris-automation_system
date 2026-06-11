using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Infrastructure.Identity;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace HRM.Tests.Unit;

/// <summary>
/// Unit tests for US-AUTH-009 Session Management and Concurrent Session Limits.
/// Covers AC-1 through AC-6 and cross-tenant isolation.
/// </summary>
public sealed class SessionManagementTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _tenantBId = Guid.NewGuid(); // Second tenant for cross-tenant isolation
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly JwtService _jwtService;

    public SessionManagementTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
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

    #region AC-1: Concurrent Session Limits (deny_new strategy)

    [Fact]
    public async Task LoginAsync_ConcurrentSessionDenyNew_DeniesNewSessionWhenLimitReached()
    {
        // Arrange: tenant with max 2 concurrent sessions, deny_new strategy
        await SeedUserAndTenantAsync(maxConcurrentSessions: 2, strategy: "deny_new");
        await SeedActiveSessionsAsync(count: 2);
        var service = CreateService();

        // Act: try to login when limit is reached
        var result = await service.LoginAsync(
            "test@test.local", "Password123!", null, "127.0.0.1", "Chrome", default);

        // Assert: denied with 403
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("Maximum concurrent sessions reached");

        // Verify audit event
        using var db = CreateDbContext();
        db.AuditLogs.IgnoreQueryFilters()
            .Any(a => a.EventType == "concurrent_session_denied" && a.UserId == _userId)
            .Should().BeTrue();
    }

    [Fact]
    public async Task LoginAsync_ConcurrentSessionDenyNew_AllowsLoginWhenBelowLimit()
    {
        // Arrange: tenant with max 3 concurrent sessions, deny_new strategy
        await SeedUserAndTenantAsync(maxConcurrentSessions: 3, strategy: "deny_new");
        await SeedActiveSessionsAsync(count: 2);
        var service = CreateService();

        // Act: login when below limit
        var result = await service.LoginAsync(
            "test@test.local", "Password123!", null, "127.0.0.1", "Chrome", default);

        // Assert: success
        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region AC-1: Concurrent Session Limits (revoke_oldest strategy)

    [Fact]
    public async Task LoginAsync_ConcurrentSessionRevokeOldest_RevokesOldestWhenLimitReached()
    {
        // Arrange: tenant with max 2 concurrent sessions, revoke_oldest strategy
        await SeedUserAndTenantAsync(maxConcurrentSessions: 2, strategy: "revoke_oldest");
        var oldestSessionId = await SeedActiveSessionsAsync(count: 2);
        var service = CreateService();

        // Act: login when limit is reached
        var result = await service.LoginAsync(
            "test@test.local", "Password123!", null, "127.0.0.1", "Chrome", default);

        // Assert: login succeeds
        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().NotBeNullOrWhiteSpace();

        // Assert: oldest session was revoked
        using var db = CreateDbContext();
        var revokedSession = await db.RefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(rt => rt.Id == oldestSessionId);
        revokedSession!.RevokedAt.Should().NotBeNull();

        // Verify audit event
        db.AuditLogs.IgnoreQueryFilters()
            .Any(a => a.EventType == "concurrent_session_oldest_revoked" && a.UserId == _userId)
            .Should().BeTrue();
    }

    #endregion

    #region AC-2: Idle Timeout Expiry

    [Fact]
    public async Task RefreshTokenAsync_IdleTimeout_ExpiredSessionReturns401()
    {
        // Arrange: tenant with 30-minute idle timeout
        await SeedUserAndTenantAsync(idleTimeoutMinutes: 30);
        var rawToken = _jwtService.GenerateRefreshToken();
        await SeedSessionAsync(rawToken, lastActiveAt: DateTime.UtcNow.AddMinutes(-35));
        var service = CreateService();

        // Act
        var result = await service.RefreshTokenAsync(rawToken, "127.0.0.1", "Chrome", default);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(401);
        result.Error.Should().Contain("inactivity");

        // Verify audit event
        using var db = CreateDbContext();
        db.AuditLogs.IgnoreQueryFilters()
            .Any(a => a.EventType == "session_expired_idle" && a.UserId == _userId)
            .Should().BeTrue();
    }

    [Fact]
    public async Task RefreshTokenAsync_IdleTimeout_ActiveSessionSucceeds()
    {
        // Arrange: tenant with 30-minute idle timeout, session active 10 minutes ago
        await SeedUserAndTenantAsync(idleTimeoutMinutes: 30);
        var rawToken = _jwtService.GenerateRefreshToken();
        await SeedSessionAsync(rawToken, lastActiveAt: DateTime.UtcNow.AddMinutes(-10));
        var service = CreateService();

        // Act
        var result = await service.RefreshTokenAsync(rawToken, "127.0.0.1", "Chrome", default);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region AC-3: Absolute Timeout Expiry

    [Fact]
    public async Task RefreshTokenAsync_AbsoluteTimeout_ExpiredSessionReturns401()
    {
        // Arrange: tenant with 12-hour absolute timeout
        await SeedUserAndTenantAsync(absoluteTimeoutHours: 12);
        var rawToken = _jwtService.GenerateRefreshToken();
        await SeedSessionAsync(rawToken, issuedAt: DateTime.UtcNow.AddHours(-13), lastActiveAt: DateTime.UtcNow);
        var service = CreateService();

        // Act
        var result = await service.RefreshTokenAsync(rawToken, "127.0.0.1", "Chrome", default);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(401);
        result.Error.Should().Contain("expired");

        // Verify audit event
        using var db = CreateDbContext();
        db.AuditLogs.IgnoreQueryFilters()
            .Any(a => a.EventType == "session_expired_absolute" && a.UserId == _userId)
            .Should().BeTrue();
    }

    [Fact]
    public async Task RefreshTokenAsync_AbsoluteTimeout_WithinLimitSucceeds()
    {
        // Arrange: tenant with 12-hour absolute timeout, session started 6 hours ago
        await SeedUserAndTenantAsync(absoluteTimeoutHours: 12);
        var rawToken = _jwtService.GenerateRefreshToken();
        await SeedSessionAsync(rawToken, issuedAt: DateTime.UtcNow.AddHours(-6), lastActiveAt: DateTime.UtcNow);
        var service = CreateService();

        // Act
        var result = await service.RefreshTokenAsync(rawToken, "127.0.0.1", "Chrome", default);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region AC-4 & AC-6: Session Listing (Admin and Self)

    [Fact]
    public async Task GetUserSessionsAsync_ReturnsActiveSessionsWithDeviceInfo()
    {
        // Arrange
        await SeedUserAndTenantAsync();
        var sessionId = BaseEntity.NewUuidV7();
        using (var db = CreateDbContext())
        {
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = sessionId,
                UserId = _userId,
                TenantId = _tenantId,
                TokenHash = "hash-session-1",
                IssuedAt = DateTime.UtcNow.AddHours(-2),
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                LastActiveAt = DateTime.UtcNow.AddMinutes(-5),
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36",
                IpAddress = "192.168.1.1",
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act
        var result = await service.GetUserSessionsAsync(_userId, _tenantId, sessionId, default);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);

        var session = result.Value![0];
        session.SessionId.Should().Be(sessionId);
        session.Browser.Should().Contain("Chrome");
        session.Os.Should().Contain("Windows");
        session.Device.Should().Be("Desktop");
        session.IpAddress.Should().Be("192.168.1.1");
        session.IsCurrent.Should().BeTrue();
        session.LastActiveAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetUserSessionsAsync_ExcludesRevokedAndExpiredSessions()
    {
        // Arrange
        await SeedUserAndTenantAsync();
        using (var db = CreateDbContext())
        {
            // Active session
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = BaseEntity.NewUuidV7(),
                UserId = _userId,
                TenantId = _tenantId,
                TokenHash = "active-hash",
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                LastActiveAt = DateTime.UtcNow,
            });
            // Revoked session
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = BaseEntity.NewUuidV7(),
                UserId = _userId,
                TenantId = _tenantId,
                TokenHash = "revoked-hash",
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                RevokedAt = DateTime.UtcNow.AddMinutes(-10),
            });
            // Expired session
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = BaseEntity.NewUuidV7(),
                UserId = _userId,
                TenantId = _tenantId,
                TokenHash = "expired-hash",
                IssuedAt = DateTime.UtcNow.AddDays(-10),
                ExpiresAt = DateTime.UtcNow.AddDays(-3),
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act
        var result = await service.GetUserSessionsAsync(_userId, _tenantId, null, default);

        // Assert: only the active session is returned
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    #endregion

    #region AC-5: Admin Revoke

    [Fact]
    public async Task RevokeSessionAsync_AdminRevokesSpecificSession_Success()
    {
        // Arrange
        await SeedUserAndTenantAsync();
        var sessionId = BaseEntity.NewUuidV7();
        using (var db = CreateDbContext())
        {
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = sessionId,
                UserId = _userId,
                TenantId = _tenantId,
                TokenHash = "admin-revoke-hash",
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                LastActiveAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act
        var result = await service.RevokeSessionAsync(
            sessionId, _userId, _tenantId, currentSessionId: null, isAdminAction: true, default);

        // Assert
        result.IsSuccess.Should().BeTrue();

        using var db2 = CreateDbContext();
        var session = await db2.RefreshTokens.IgnoreQueryFilters().FirstAsync(rt => rt.Id == sessionId);
        session.RevokedAt.Should().NotBeNull();

        db2.AuditLogs.IgnoreQueryFilters()
            .Any(a => a.EventType == "session_revoked_by_admin" && a.UserId == _userId)
            .Should().BeTrue();
    }

    [Fact]
    public async Task RevokeAllSessionsAsync_AdminRevokesAllSessions_Success()
    {
        // Arrange
        await SeedUserAndTenantAsync();
        using (var db = CreateDbContext())
        {
            for (int i = 0; i < 3; i++)
            {
                db.RefreshTokens.Add(new RefreshToken
                {
                    Id = BaseEntity.NewUuidV7(),
                    UserId = _userId,
                    TenantId = _tenantId,
                    TokenHash = $"revoke-all-hash-{i}",
                    IssuedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                    LastActiveAt = DateTime.UtcNow,
                });
            }
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act
        var result = await service.RevokeAllSessionsAsync(_userId, _tenantId, default);

        // Assert
        result.IsSuccess.Should().BeTrue();

        using var db2 = CreateDbContext();
        var activeSessions = db2.RefreshTokens.IgnoreQueryFilters()
            .Count(rt => rt.UserId == _userId && rt.TenantId == _tenantId && rt.RevokedAt == null);
        activeSessions.Should().Be(0);

        db2.AuditLogs.IgnoreQueryFilters()
            .Any(a => a.EventType == "session_revoked_by_admin")
            .Should().BeTrue();
    }

    #endregion

    #region AC-6: Self Revoke (including BR-4 current session block)

    [Fact]
    public async Task RevokeSessionAsync_SelfRevokesNonCurrentSession_Success()
    {
        // Arrange
        await SeedUserAndTenantAsync();
        var currentSessionId = BaseEntity.NewUuidV7();
        var otherSessionId = BaseEntity.NewUuidV7();
        using (var db = CreateDbContext())
        {
            db.RefreshTokens.AddRange(
                new RefreshToken
                {
                    Id = currentSessionId,
                    UserId = _userId,
                    TenantId = _tenantId,
                    TokenHash = "current-hash",
                    IssuedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                    LastActiveAt = DateTime.UtcNow,
                },
                new RefreshToken
                {
                    Id = otherSessionId,
                    UserId = _userId,
                    TenantId = _tenantId,
                    TokenHash = "other-hash",
                    IssuedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                    LastActiveAt = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act: revoke the other session (not the current one)
        var result = await service.RevokeSessionAsync(
            otherSessionId, _userId, _tenantId, currentSessionId, isAdminAction: false, default);

        // Assert
        result.IsSuccess.Should().BeTrue();

        using var db2 = CreateDbContext();
        var revoked = await db2.RefreshTokens.IgnoreQueryFilters().FirstAsync(rt => rt.Id == otherSessionId);
        revoked.RevokedAt.Should().NotBeNull();

        db2.AuditLogs.IgnoreQueryFilters()
            .Any(a => a.EventType == "session_revoked_by_user" && a.UserId == _userId)
            .Should().BeTrue();
    }

    [Fact]
    public async Task RevokeSessionAsync_SelfRevokesCurrentSession_BlockedByBR4()
    {
        // Arrange
        await SeedUserAndTenantAsync();
        var currentSessionId = BaseEntity.NewUuidV7();
        using (var db = CreateDbContext())
        {
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = currentSessionId,
                UserId = _userId,
                TenantId = _tenantId,
                TokenHash = "current-session-hash",
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                LastActiveAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act: try to revoke the current session via self-service
        var result = await service.RevokeSessionAsync(
            currentSessionId, _userId, _tenantId, currentSessionId, isAdminAction: false, default);

        // Assert: blocked
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("current session");
        result.Error.Should().Contain("logout");
    }

    [Fact]
    public async Task RevokeSessionAsync_AdminCanRevokeCurrentSession()
    {
        // Arrange: admin can revoke any session including the current session of the target user
        await SeedUserAndTenantAsync();
        var sessionId = BaseEntity.NewUuidV7();
        using (var db = CreateDbContext())
        {
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = sessionId,
                UserId = _userId,
                TenantId = _tenantId,
                TokenHash = "admin-can-revoke-hash",
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                LastActiveAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act: admin revokes (isAdminAction = true, so BR-4 does not apply)
        var result = await service.RevokeSessionAsync(
            sessionId, _userId, _tenantId, currentSessionId: sessionId, isAdminAction: true, default);

        // Assert: allowed
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Cross-Tenant Isolation

    [Fact]
    public async Task GetUserSessionsAsync_ReturnsOnlySessionsForRequestedTenant()
    {
        // Arrange: user has sessions in two tenants
        await SeedUserAndTenantAsync();
        using (var db = CreateDbContext())
        {
            // Tenant B
            db.Tenants.Add(new Tenant
            {
                Id = _tenantBId,
                Name = "Tenant B",
                Subdomain = "tenantb",
                Status = TenantStatus.Active,
            });

            // Session in Tenant A
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = BaseEntity.NewUuidV7(),
                UserId = _userId,
                TenantId = _tenantId,
                TokenHash = "tenant-a-hash",
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                LastActiveAt = DateTime.UtcNow,
            });

            // Session in Tenant B
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = BaseEntity.NewUuidV7(),
                UserId = _userId,
                TenantId = _tenantBId,
                TokenHash = "tenant-b-hash",
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                LastActiveAt = DateTime.UtcNow,
            });

            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act: query sessions for tenant A only
        var result = await service.GetUserSessionsAsync(_userId, _tenantId, null, default);

        // Assert: only tenant A session returned
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task RevokeSessionAsync_CannotRevokeSessionFromDifferentTenant()
    {
        // Arrange: create a session in Tenant B
        await SeedUserAndTenantAsync();
        var tenantBSessionId = BaseEntity.NewUuidV7();
        using (var db = CreateDbContext())
        {
            db.Tenants.Add(new Tenant
            {
                Id = _tenantBId,
                Name = "Tenant B",
                Subdomain = "tenantb2",
                Status = TenantStatus.Active,
            });
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = tenantBSessionId,
                UserId = _userId,
                TenantId = _tenantBId,
                TokenHash = "cross-tenant-hash",
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                LastActiveAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act: try to revoke Tenant B session using Tenant A context
        var result = await service.RevokeSessionAsync(
            tenantBSessionId, _userId, _tenantId, null, isAdminAction: true, default);

        // Assert: not found because the session belongs to a different tenant
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ConcurrentSessionLimit_CountsOnlyCurrentTenantSessions()
    {
        // Arrange: tenant A has max 2 concurrent sessions, user has 2 sessions in tenant B
        await SeedUserAndTenantAsync(maxConcurrentSessions: 2, strategy: "deny_new");
        using (var db = CreateDbContext())
        {
            db.Tenants.Add(new Tenant
            {
                Id = _tenantBId,
                Name = "Tenant B",
                Subdomain = "tenantb3",
                Status = TenantStatus.Active,
            });

            // 2 sessions in Tenant B
            for (int i = 0; i < 2; i++)
            {
                db.RefreshTokens.Add(new RefreshToken
                {
                    Id = BaseEntity.NewUuidV7(),
                    UserId = _userId,
                    TenantId = _tenantBId,
                    TokenHash = $"tenant-b-session-{i}",
                    IssuedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                    LastActiveAt = DateTime.UtcNow,
                });
            }
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act: login to tenant A (should succeed since tenant A has 0 sessions)
        var result = await service.LoginAsync(
            "test@test.local", "Password123!", null, "127.0.0.1", "Chrome", default);

        // Assert: login succeeds (tenant B sessions don't count toward tenant A limit)
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Session Activity Update

    [Fact]
    public async Task UpdateSessionActivityAsync_UpdatesLastActiveAt()
    {
        // Arrange
        await SeedUserAndTenantAsync();
        var sessionId = BaseEntity.NewUuidV7();
        var oldLastActive = DateTime.UtcNow.AddMinutes(-5);
        using (var db = CreateDbContext())
        {
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = sessionId,
                UserId = _userId,
                TenantId = _tenantId,
                TokenHash = "activity-hash",
                IssuedAt = DateTime.UtcNow.AddHours(-1),
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                LastActiveAt = oldLastActive,
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act
        var result = await service.UpdateSessionActivityAsync(sessionId, default);

        // Assert
        result.IsSuccess.Should().BeTrue();

        using var db2 = CreateDbContext();
        var session = await db2.RefreshTokens.IgnoreQueryFilters().FirstAsync(rt => rt.Id == sessionId);
        session.LastActiveAt.Should().BeAfter(oldLastActive);
    }

    #endregion

    #region GetSessionIdFromToken

    [Fact]
    public async Task GetSessionIdFromTokenAsync_ReturnsSessionIdForValidToken()
    {
        // Arrange
        await SeedUserAndTenantAsync();
        var rawToken = _jwtService.GenerateRefreshToken();
        var tokenHash = _jwtService.HashToken(rawToken);
        var sessionId = BaseEntity.NewUuidV7();
        using (var db = CreateDbContext())
        {
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = sessionId,
                UserId = _userId,
                TenantId = _tenantId,
                TokenHash = tokenHash,
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService();

        // Act
        var result = await service.GetSessionIdFromTokenAsync(rawToken, default);

        // Assert
        result.Should().Be(sessionId);
    }

    [Fact]
    public async Task GetSessionIdFromTokenAsync_ReturnsNullForInvalidToken()
    {
        await SeedUserAndTenantAsync();
        var service = CreateService();

        var result = await service.GetSessionIdFromTokenAsync("nonexistent-token", default);

        result.Should().BeNull();
    }

    #endregion

    #region Tenant Auth Settings (Session Policy)

    [Fact]
    public async Task GetTenantAuthSettingsAsync_IncludesSessionPolicyFields()
    {
        await SeedUserAndTenantAsync(
            idleTimeoutMinutes: 45,
            absoluteTimeoutHours: 16,
            maxConcurrentSessions: 3,
            strategy: "deny_new");
        var service = CreateService();

        var result = await service.GetTenantAuthSettingsAsync(_tenantId, default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IdleTimeoutMinutes.Should().Be(45);
        result.Value.AbsoluteTimeoutHours.Should().Be(16);
        result.Value.MaxConcurrentSessions.Should().Be(3);
        result.Value.ConcurrentSessionStrategy.Should().Be("deny_new");
    }

    [Fact]
    public async Task UpdateTenantAuthSettingsAsync_UpdatesSessionPolicyFields()
    {
        await SeedUserAndTenantAsync();
        var service = CreateService();

        var request = new Application.Features.Auth.DTOs.TenantAuthSettingsRequest
        {
            MfaPolicy = "off",
            IdleTimeoutMinutes = 90,
            AbsoluteTimeoutHours = 48,
            MaxConcurrentSessions = 10,
            ConcurrentSessionStrategy = "deny_new",
        };

        var result = await service.UpdateTenantAuthSettingsAsync(_tenantId, request, default);

        result.IsSuccess.Should().BeTrue();

        // Verify the values were persisted
        using var db = CreateDbContext();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == _tenantId);
        tenant.IdleTimeoutMinutes.Should().Be(90);
        tenant.AbsoluteTimeoutHours.Should().Be(48);
        tenant.MaxConcurrentSessions.Should().Be(10);
        tenant.ConcurrentSessionStrategy.Should().Be("deny_new");
    }

    [Fact]
    public async Task UpdateTenantAuthSettingsAsync_InvalidStrategy_ReturnsBadRequest()
    {
        await SeedUserAndTenantAsync();
        var service = CreateService();

        var request = new Application.Features.Auth.DTOs.TenantAuthSettingsRequest
        {
            MfaPolicy = "off",
            ConcurrentSessionStrategy = "invalid_strategy",
        };

        var result = await service.UpdateTenantAuthSettingsAsync(_tenantId, request, default);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task UpdateTenantAuthSettingsAsync_IdleTimeoutOutOfRange_ReturnsBadRequest()
    {
        await SeedUserAndTenantAsync();
        var service = CreateService();

        var request = new Application.Features.Auth.DTOs.TenantAuthSettingsRequest
        {
            MfaPolicy = "off",
            IdleTimeoutMinutes = 0,
        };

        var result = await service.UpdateTenantAuthSettingsAsync(_tenantId, request, default);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    #endregion

    #region Helpers

    private AuthService CreateService()
    {
        return new AuthService(
            CreateDbContext(),
            _jwtService,
            _tenantContext,
            Substitute.For<ITotpService>(),
            _configuration,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Substitute.For<ILogger<AuthService>>());
    }

    private AppDbContext CreateDbContext()
    {
        return TestDbContextFactory.Create(_tenantContext, _dbName);
    }

    private async Task SeedUserAndTenantAsync(
        int maxConcurrentSessions = 5,
        string strategy = "revoke_oldest",
        int idleTimeoutMinutes = 60,
        int absoluteTimeoutHours = 24)
    {
        using var db = CreateDbContext();

        var user = new User
        {
            Id = _userId,
            Email = "test@test.local",
            DisplayName = "Test User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!", workFactor: 4),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        var tenant = new Tenant
        {
            Id = _tenantId,
            Name = "Test Tenant",
            Subdomain = "testco",
            Status = TenantStatus.Active,
            MaxConcurrentSessions = maxConcurrentSessions,
            ConcurrentSessionStrategy = strategy,
            IdleTimeoutMinutes = idleTimeoutMinutes,
            AbsoluteTimeoutHours = absoluteTimeoutHours,
            CreatedAt = DateTime.UtcNow,
        };

        var role = new Role
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            Name = "Employee",
            CreatedAt = DateTime.UtcNow,
        };

        db.Users.Add(user);
        db.Tenants.Add(tenant);
        db.Roles.Add(role);

        var membership = new UserTenant
        {
            Id = BaseEntity.NewUuidV7(),
            UserId = _userId,
            TenantId = _tenantId,
            Status = UserTenantStatus.Active,
            CreatedAt = DateTime.UtcNow,
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

    /// <summary>
    /// Seeds the specified number of active sessions. Returns the ID of the oldest session.
    /// </summary>
    private async Task<Guid> SeedActiveSessionsAsync(int count)
    {
        Guid oldestId = Guid.Empty;
        using var db = CreateDbContext();

        for (int i = 0; i < count; i++)
        {
            var id = BaseEntity.NewUuidV7();
            if (i == 0) oldestId = id;

            db.RefreshTokens.Add(new RefreshToken
            {
                Id = id,
                UserId = _userId,
                TenantId = _tenantId,
                TokenHash = $"session-hash-{i}-{Guid.NewGuid()}",
                IssuedAt = DateTime.UtcNow.AddMinutes(-count + i), // oldest first
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                LastActiveAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
        return oldestId;
    }

    private async Task SeedSessionAsync(
        string rawToken,
        DateTime? lastActiveAt = null,
        DateTime? issuedAt = null)
    {
        using var db = CreateDbContext();
        var tokenHash = _jwtService.HashToken(rawToken);

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = BaseEntity.NewUuidV7(),
            UserId = _userId,
            TenantId = _tenantId,
            TokenHash = tokenHash,
            IssuedAt = issuedAt ?? DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            LastActiveAt = lastActiveAt,
        });

        await db.SaveChangesAsync();
    }

    #endregion
}
