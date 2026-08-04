// ============================================================================
// BUG-294: the invitation-token transformation is now defined once, in
// InvitationToken. These arms pin the WIRE FORMAT rather than round-tripping
// through the helper.
//
// A round-trip test would pass even if the encoding changed, because both sides
// would change together — while every invitation row ALREADY IN THE DATABASE
// (written by the pre-extraction mint) would stop verifying. The stored hash is
// persisted state, so its shape is a contract with existing data, not an
// implementation detail.
//
// The specific landmine: Convert.ToHexString returns UPPERCASE, but the
// functionally identical reset-token hasher in AuthService appends
// ToLowerInvariant(). Copying that one would silently break every live
// invitation without failing to compile.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Security;

namespace HRM.Tests.Unit;

public sealed class InvitationTokenTests
{
    // SHA-256("the-real-token") — computed independently of the implementation, so this fails if the digest,
    // the encoding, or the CASE ever changes.
    private const string KnownToken = "the-real-token";
    private const string KnownHashUpper = "777A06D3DD298599D07B6110D07E7FB5B8C051E411B74DFCFEB1002C04D9BEA8";

    [Fact]
    public void Hash_is_uppercase_hex_matching_the_form_already_stored_in_the_database()
    {
        var hash = InvitationToken.Hash(KnownToken);

        // Computed independently (sha256 of the UTF-8 token bytes, hex-encoded), so this pins the exact wire
        // form rather than merely agreeing with whatever the implementation currently produces.
        hash.Should().Be(KnownHashUpper);
        hash.Should().MatchRegex("^[0-9A-F]{64}$",
            "invitation hashes already persisted were written as UPPERCASE hex; lowercasing this (as the " +
            "reset-token hasher does) would silently invalidate every outstanding invitation");
    }

    [Fact]
    public void Hash_is_stable_for_the_same_input()
    {
        InvitationToken.Hash(KnownToken).Should().Be(InvitationToken.Hash(KnownToken));
    }

    [Fact]
    public void Generate_produces_a_url_safe_token_whose_hash_verifies()
    {
        var (raw, hash) = InvitationToken.Generate();

        raw.Should().MatchRegex("^[A-Za-z0-9_-]+$",
            "the token travels in a query string, so base64url with no padding — '+', '/' or '=' would be " +
            "mangled in transit and the link would not verify");
        hash.Should().Be(InvitationToken.Hash(raw), "mint and verify must agree by construction");
        InvitationToken.Verify(raw, hash).Should().BeTrue();
    }

    [Fact]
    public void Generate_never_repeats_a_token()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => InvitationToken.Generate().RawToken).ToList();

        tokens.Distinct().Should().HaveCount(tokens.Count, "each invitation must carry its own secret");
    }

    [Fact]
    public void Verify_rejects_a_different_token()
    {
        var (_, hash) = InvitationToken.Generate();

        InvitationToken.Verify("not-the-token", hash).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_treats_an_absent_token_as_a_failed_check_rather_than_throwing(string? rawToken)
    {
        var (_, hash) = InvitationToken.Generate();

        InvitationToken.Verify(rawToken, hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_treats_an_absent_stored_hash_as_a_failed_check()
    {
        InvitationToken.Verify("any-token", null).Should().BeFalse();
        InvitationToken.Verify("any-token", "").Should().BeFalse();
    }

    [Fact]
    public void Verify_accepts_a_hash_stored_in_either_case()
    {
        // Defensive: verification must not depend on which convention wrote the row, so a legacy or
        // differently-cased hash still redeems.
        var (raw, hash) = InvitationToken.Generate();

        InvitationToken.Verify(raw, hash.ToLowerInvariant()).Should().BeTrue();
        InvitationToken.Verify(raw, hash.ToUpperInvariant()).Should().BeTrue();
    }
}
