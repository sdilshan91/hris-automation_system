// ============================================================================
// ISSUE-220 regression (US-AUTH-011): the SSO challenge must NOT return the misleading
// `not_configured` error code when the request simply carried no/unresolved tenant.
// `not_configured` is reserved for "SSO is genuinely disabled"; a missing tenant now
// returns the distinct `tenant_required` code so the two cases are distinguishable.
//
// Provider: the real EntraSsoService — both branches under test return BEFORE any OIDC
// discovery/network call, so the ConfigurationManager is constructed but never invoked.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class EntraSsoChallengeErrorCodeTests
{
    private static EntraSsoService CreateService(bool configured)
    {
        var options = Options.Create(new EntraSsoOptions
        {
            Enabled = configured,
            ClientId = configured ? "client-id" : string.Empty,
            ClientSecret = configured ? "client-secret" : string.Empty,
            RedirectUri = configured ? "https://app.test/api/v1/auth/sso/callback" : string.Empty,
        });

        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            "https://login.microsoftonline.com/organizations/v2.0/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());

        return new EntraSsoService(
            options,
            DataProtectionProvider.Create("tests"),
            Substitute.For<IHttpClientFactory>(),
            configManager,
            Substitute.For<IAuthService>(),
            Substitute.For<ILogger<EntraSsoService>>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Challenge_ConfiguredButNoTenant_Returns_TenantRequired_Not_NotConfigured(string? subdomain)
    {
        var result = await CreateService(configured: true)
            .BuildAuthorizeUrlAsync(subdomain, "/dashboard", "https://app.test");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("tenant_required",
            "an unresolved tenant must be distinguishable from SSO being disabled");
        result.Error.Should().NotBe("not_configured");
    }

    [Fact]
    public async Task Challenge_WhenSsoNotConfigured_Returns_NotConfigured()
    {
        var result = await CreateService(configured: false)
            .BuildAuthorizeUrlAsync("acme", "/dashboard", "https://app.test");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("not_configured", "genuinely-disabled SSO keeps the not_configured code");
    }
}
