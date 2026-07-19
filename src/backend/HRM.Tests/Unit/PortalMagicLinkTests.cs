using FluentAssertions;
using HRM.Domain.Recruitment;

namespace HRM.Tests.Unit;

/// <summary>
/// Unit tests for the pure candidate-portal magic-link codec (US-REC-008 FR-1/NFR-4): sign/verify roundtrip,
/// expiry surfacing, and rejection of tampered / cross-secret / malformed tokens.
/// </summary>
public sealed class PortalMagicLinkTests
{
    private const string Secret = "unit-test-portal-signing-secret-0123456789";
    private readonly Guid _tenant = Guid.NewGuid();

    [Fact]
    [Trait("TC", "TC-REC-008-14")]
    public void Issue_ThenVerify_Roundtrips_TenantEmailExpiry()
    {
        var expiry = DateTime.UtcNow.AddDays(30);
        var token = PortalMagicLink.Issue(_tenant, "Ada@Example.com", expiry, Secret);

        var ok = PortalMagicLink.TryVerify(token, Secret, out var tenantId, out var email, out var expiresAt);

        ok.Should().BeTrue();
        tenantId.Should().Be(_tenant);
        email.Should().Be("ada@example.com"); // lower-cased on issue
        // Unix-second precision roundtrip.
        expiresAt.Should().BeCloseTo(expiry, TimeSpan.FromSeconds(1));
    }

    [Fact]
    [Trait("TC", "TC-REC-008-14")]
    public void Verify_ExposesExpiry_SoCallerCanRejectExpiredToken()
    {
        var expiry = DateTime.UtcNow.AddMinutes(-5); // already past
        var token = PortalMagicLink.Issue(_tenant, "ada@example.com", expiry, Secret);

        var ok = PortalMagicLink.TryVerify(token, Secret, out _, out _, out var expiresAt);

        // The codec verifies the signature (token is authentic) but surfaces the past expiry — the service
        // is what enforces it. This proves the expiry is carried correctly for rejection.
        ok.Should().BeTrue();
        expiresAt.Should().BeBefore(DateTime.UtcNow);
    }

    [Fact]
    [Trait("TC", "TC-REC-008-14")]
    public void Verify_TamperedPayload_IsRejected()
    {
        var token = PortalMagicLink.Issue(_tenant, "ada@example.com", DateTime.UtcNow.AddDays(30), Secret);

        // Flip a character in the payload (before the '.') — the signature no longer matches.
        var dot = token.IndexOf('.');
        var tampered = (token[0] == 'A' ? 'B' : 'A') + token[1..dot] + token[dot..];

        var ok = PortalMagicLink.TryVerify(tampered, Secret, out _, out _, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    [Trait("TC", "TC-REC-008-14")]
    public void Verify_TamperedSignature_IsRejected()
    {
        var token = PortalMagicLink.Issue(_tenant, "ada@example.com", DateTime.UtcNow.AddDays(30), Secret);

        // Flip the FIRST signature char (immediately after the '.'). Flipping the LAST char is unreliable:
        // base64url's final char of a 32-byte signature carries only 4 meaningful bits + 2 padding bits,
        // so an A<->B swap there can decode to the same bytes and leave the signature valid. The first
        // signature char always carries 6 meaningful bits, so this swap is guaranteed to alter the signature.
        var sigStart = token.IndexOf('.') + 1;
        var tampered = token[..sigStart] + (token[sigStart] == 'A' ? 'B' : 'A') + token[(sigStart + 1)..];

        var ok = PortalMagicLink.TryVerify(tampered, Secret, out _, out _, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    [Trait("TC", "TC-REC-008-14")]
    public void Verify_WithWrongSecret_IsRejected()
    {
        var token = PortalMagicLink.Issue(_tenant, "ada@example.com", DateTime.UtcNow.AddDays(30), Secret);

        var ok = PortalMagicLink.TryVerify(token, "a-different-secret", out _, out _, out _);

        ok.Should().BeFalse();
    }

    [Theory]
    [Trait("TC", "TC-REC-008-14")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-dot-here")]
    [InlineData("too.many.dots")]
    public void Verify_MalformedToken_IsRejected(string token)
    {
        var ok = PortalMagicLink.TryVerify(token, Secret, out _, out _, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    [Trait("TC", "TC-REC-008-14")]
    public void HashToken_IsDeterministic_AndHidesRawToken()
    {
        var token = PortalMagicLink.Issue(_tenant, "ada@example.com", DateTime.UtcNow.AddDays(30), Secret);

        var hash1 = PortalMagicLink.HashToken(token);
        var hash2 = PortalMagicLink.HashToken(token);

        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(64); // SHA-256 hex
        hash1.Should().NotContain(token);
    }
}
