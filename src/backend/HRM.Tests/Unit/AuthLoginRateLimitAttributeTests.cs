// ============================================================================
// AUTH-001 — auth-login rate-limit attribute guard (US-AUTH-001 / US-AUTH-005 NFR-4).
//
// The credential-stuffing / MFA-brute-force mitigation is a fixed-window limiter
// policy "auth-login" (10/min/IP) registered in Program.cs, PLUS an
// [EnableRateLimiting("auth-login")] attribute on AuthController.Login and
// AuthController.MfaChallenge. The named policy is inert on an action unless the
// attribute names it, so the attribute is the load-bearing wiring.
//
// The limiter's runtime 429 behavior is exercised end-to-end for the OTHER
// policies by RateLimitClusterApiTests (live TestServer). A live 429 test on
// auth-login is deliberately NOT added here: driving 11 real logins couples to
// account-lockout side effects and the shared in-memory limiter counter across
// the HttpApi collection. The meaningful, side-effect-free unit assertion is that
// both action methods still carry the attribute naming the correct policy — a
// reflection guard that fails the moment the attribute is dropped or its policy
// name drifts. (Pre-fix the attribute did not exist on these methods, so both
// asserts fail; post-fix they pass.)
// ============================================================================

using System.Reflection;
using FluentAssertions;
using HRM.Api.Controllers;
using Microsoft.AspNetCore.RateLimiting;

namespace HRM.Tests.Unit;

public sealed class AuthLoginRateLimitAttributeTests
{
    private const string ExpectedPolicy = "auth-login";

    // Binds @TC-AUTH-RL-001.
    [Fact]
    public void AuthLogin_HasRateLimitAttribute_AUTH001()
    {
        var attribute = GetRateLimitingAttribute(nameof(AuthController.Login));

        attribute.Should().NotBeNull(
            "POST /api/v1/auth/login must be rate-limited against credential stuffing (US-AUTH-001 NFR-4)");
        attribute!.PolicyName.Should().Be(ExpectedPolicy,
            "the login action must bind the registered auth-login policy (10/min/IP)");
    }

    // Binds @TC-AUTH-RL-001.
    [Fact]
    public void MfaChallenge_HasRateLimitAttribute_AUTH001()
    {
        var attribute = GetRateLimitingAttribute(nameof(AuthController.MfaChallenge));

        attribute.Should().NotBeNull(
            "POST /api/v1/auth/mfa/challenge must be rate-limited against MFA-code brute force (US-AUTH-005 NFR-4)");
        attribute!.PolicyName.Should().Be(ExpectedPolicy,
            "the MFA-challenge action must bind the same auth-login policy");
    }

    private static EnableRateLimitingAttribute? GetRateLimitingAttribute(string methodName)
    {
        var method = typeof(AuthController).GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull($"AuthController.{methodName} must exist");
        return method!.GetCustomAttributes<EnableRateLimitingAttribute>(inherit: true).SingleOrDefault();
    }
}
