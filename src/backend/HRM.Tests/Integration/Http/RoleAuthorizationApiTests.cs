using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// Real HTTP regression tests for role-based authorization (<c>[Authorize(Roles=...)]</c>).
///
/// <para><b>BUG-240:</b> access tokens emit roles under the custom <c>"roles"</c> claim
/// (<c>JwtService.GenerateAccessToken</c>) and <c>MapInboundClaims=false</c> (BUG-001) keeps claim names
/// 1:1, so the framework never populates the default <c>ClaimTypes.Role</c>. Without
/// <c>TokenValidationParameters.RoleClaimType = "roles"</c>, every <c>[Authorize(Roles=...)]</c> endpoint
/// returned <b>403 even for the correct role</b>. This test drives the role-gated
/// <c>PUT /api/v1/tenant/auth-settings</c> as a Tenant Owner (one of the allowed roles) and asserts it is
/// NOT forbidden — it must succeed (200), proving the role claim now resolves.</para>
/// </summary>
[Collection("HttpApi")]
public sealed class RoleAuthorizationApiTests
{
    // The DEV-only E2E tenant + Tenant Owner seeded by DbInitializer (see ApiTestFactory).
    private const string Subdomain = "e2e";
    private const string OwnerEmail = "owner@e2e.test";
    private const string OwnerPassword = "E2ePass@123!";

    private readonly ApiTestFactory _factory;

    public RoleAuthorizationApiTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TenantOwner_CanReach_RoleGated_UpdateAuthSettings()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, OwnerEmail, OwnerPassword);

        // PUT is [Authorize(Roles = "Tenant Admin,Tenant Owner,System Admin")]. A minimal valid body
        // (MFA off) keeps the request well-formed so the only thing under test is the role gate.
        var response = await client.PutAsJsonAsync("/api/v1/tenant/auth-settings", new
        {
            mfaPolicy = "off",
            mfaRequiredRoles = Array.Empty<string>(),
        });

        // Pre-fix this was 403 (role claim unread). It must now succeed and must never be Forbidden.
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
