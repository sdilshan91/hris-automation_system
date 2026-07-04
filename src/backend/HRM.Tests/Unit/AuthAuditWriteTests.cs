// ============================================================================
// Auth "missing-audit-write" cluster regression suite.
//
// The AuthService logged these security-relevant events to Serilog only — no
// row was written to audit_logs — so they were invisible to the tenant audit
// trail (US-NTF-005 / admin audit search). This suite pins the audit-row write
// for each so the gap cannot silently reopen:
//
//   BUG-039  (US-AUTH-003) — LogoutAsync            -> "logout"                    [@TC-AUTH-008]
//   ISSUE-048 (US-AUTH-001) — IssueTokensAsync       -> "login_success"             [@TC-AUTH-001]
//                              (the single success exit for every login path)
//   ISSUE-051 (US-AUTH-004) — ForgotPasswordAsync    -> "password_reset_requested"  [@TC-AUTH-010]
//                              ResetPasswordAsync     -> "password_reset_completed"  [@TC-AUTH-011]
//
// Each assertion keys on the PRESENCE + EventType + actor (UserId) of the audit
// row, tenant-scoped — not on incidental state. Pre-fix (git HEAD) these rows do
// not exist (login_failure/account_locked were the only audited auth events), so
// the assertions fail; post-fix they exist and pass.
//
// Provider: EF InMemory through the real AuthService. Audit rows are plain
// inserts, so the defect (a missing insert) reproduces provider-independently —
// no Postgres-only behavior is involved. Mirrors the AccountLockoutTests /
// AuthPasswordResetTests harness (real login / logout / forgot / reset paths,
// then a direct audit_logs query).
// ============================================================================

using System.Security.Cryptography;
using System.Text;
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

public sealed class AuthAuditWriteTests
{
    private const string Email = "test@test.local";
    private const string Password = "Password123!";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly JwtService _jwtService;

    public AuthAuditWriteTests()
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

    // ── BUG-039 (US-AUTH-003): logout must write a "logout" audit row ──────────

    // Binds TC-AUTH-008 step 5 (@TC-AUTH-008).
    [Fact]
    public async Task Logout_WritesAuditRow_BUG039()
    {
        // Arrange: seed a user and log in for real to obtain a live refresh token.
        await SeedUserAndTenantAsync();
        var login = await CreateService().LoginAsync(
            Email, Password, null, "10.0.0.1", "TestAgent", default);
        login.IsSuccess.Should().BeTrue("the arrange step needs a real session to log out of");
        var refreshToken = login.Value!.RefreshToken!;

        // Act: perform the real logout (revokes the token + should audit).
        var logout = await CreateService().LogoutAsync(refreshToken, default);
        logout.IsSuccess.Should().BeTrue();

        // Assert: exactly one "logout" row for this user, tenant-scoped.
        // Pre-fix: LogoutAsync was Serilog-only -> zero rows -> this fails.
        using var db = CreateDbContext();
        var logoutRows = db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EventType == "logout"
                && a.UserId == _userId
                && a.TenantId == _tenantId)
            .ToList();

        logoutRows.Should().HaveCount(1, "logout must be recorded in the tenant audit trail (BUG-039)");
    }

    // ── ISSUE-048 (US-AUTH-001): successful login must write "login_success" ───

    // Binds TC-AUTH-001 step 11 (@TC-AUTH-001).
    [Fact]
    public async Task LoginSuccess_WritesAuditRow_ISSUE048()
    {
        // Arrange
        await SeedUserAndTenantAsync();

        // Act: a genuinely successful login (all login paths converge on IssueTokensAsync).
        var login = await CreateService().LoginAsync(
            Email, Password, null, "10.0.0.2", "TestAgent", default);

        // Assert
        login.IsSuccess.Should().BeTrue();
        login.Value!.AccessToken.Should().NotBeNullOrWhiteSpace();

        // Pre-fix: only login_failure/account_locked were audited -> no login_success row -> fails.
        using var db = CreateDbContext();
        var successRows = db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EventType == "login_success"
                && a.UserId == _userId
                && a.TenantId == _tenantId)
            .ToList();

        successRows.Should().HaveCount(1, "a successful login must be recorded (ISSUE-048)");
    }

    /// <summary>
    /// ISSUE-048 guard: proves the fix ADDED success-auditing without regressing the
    /// pre-existing failure-auditing. A wrong-password attempt must still write a
    /// "login_failure" row (this passes both pre- and post-fix — that is the point:
    /// it isolates the gap to the success path).
    /// </summary>
    [Fact]
    public async Task LoginFailure_StillWritesAuditRow_ISSUE048Guard()
    {
        // Arrange
        await SeedUserAndTenantAsync();

        // Act: a failed login (wrong password), below the lockout threshold.
        var login = await CreateService().LoginAsync(
            Email, "WrongPassword!", null, "10.0.0.3", "TestAgent", default);

        // Assert: rejected, and the failure is still audited.
        login.IsFailure.Should().BeTrue();

        using var db = CreateDbContext();
        var failureRows = db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EventType == "login_failure"
                && a.UserId == _userId
                && a.TenantId == _tenantId)
            .ToList();

        failureRows.Should().HaveCount(1, "failure-auditing must remain intact (ISSUE-048 regression guard)");
    }

    // ── ISSUE-051 (US-AUTH-004): forgot + reset must write audit rows ──────────

    // Binds TC-AUTH-010 step 6 (requested) + TC-AUTH-011 step 9 (completed)
    // (@TC-AUTH-010, @TC-AUTH-011).
    [Fact]
    public async Task PasswordReset_WritesRequestedAndCompletedAuditRows_ISSUE051()
    {
        // Arrange: user with an active membership so ForgotPasswordAsync issues a token.
        await SeedUserAndTenantAsync();

        // Act 1 — forgot password (the real audited request path). This also stamps a
        // random reset-token hash on the user (raw token not retrievable by design).
        var forgot = await CreateService().ForgotPasswordAsync(Email, default);
        forgot.IsSuccess.Should().BeTrue();

        // Assert 1: a "password_reset_requested" row exists for the user.
        // Pre-fix: ForgotPasswordAsync was Serilog-only -> no row -> fails.
        using (var db = CreateDbContext())
        {
            db.AuditLogs.IgnoreQueryFilters()
                .Count(a => a.EventType == "password_reset_requested"
                    && a.UserId == _userId
                    && a.TenantId == _tenantId)
                .Should().Be(1, "a reset request must be recorded (ISSUE-051)");
        }

        // Arrange 2 — install a KNOWN valid single-use token so the real reset path runs
        // fully (hash validation + audit). Seeding the token hash the way the existing
        // reset tests do does NOT bypass the audited code: ResetPasswordAsync still
        // validates the supplied raw token against this stored hash.
        const string rawToken = "the-real-token";
        using (var db = CreateDbContext())
        {
            var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _userId);
            user.PasswordResetTokenHash = Sha256Hex(rawToken);
            user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddHours(1);
            await db.SaveChangesAsync();
        }

        // Act 2 — reset password with the valid token.
        var reset = await CreateService().ResetPasswordAsync(Email, rawToken, "BrandNewPassw0rd!", default);
        reset.IsSuccess.Should().BeTrue();

        // Assert 2: a "password_reset_completed" row exists for the user.
        // Pre-fix: ResetPasswordAsync was Serilog-only -> no row -> fails.
        using (var db = CreateDbContext())
        {
            db.AuditLogs.IgnoreQueryFilters()
                .Count(a => a.EventType == "password_reset_completed"
                    && a.UserId == _userId
                    && a.TenantId == _tenantId)
                .Should().Be(1, "a completed reset must be recorded (ISSUE-051)");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

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

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    /// <summary>
    /// Seeds a login-ready user: active user with a BCrypt password, an Active tenant,
    /// an Employee role, and an Active membership with that role assigned — the minimum
    /// for LoginAsync to reach IssueTokensAsync (mirrors AccountLockoutTests).
    /// </summary>
    private async Task SeedUserAndTenantAsync()
    {
        using var db = CreateDbContext();

        var user = new User
        {
            Id = _userId,
            Email = Email,
            DisplayName = "Test User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 4),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        var tenant = new Tenant
        {
            Id = _tenantId,
            Name = "Test Tenant",
            Subdomain = "testco",
            Status = TenantStatus.Active,
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
}
