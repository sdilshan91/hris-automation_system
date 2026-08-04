using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HRM.Application.Common.Security;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// BUG-294 — real HTTP + real Postgres arm for invitation redemption.
///
/// <para><b>Why this exists on top of the unit arms.</b> BUG-294's defect was precisely "no endpoint accepts
/// the token". Every other test for this fix calls <c>AuthService</c> directly, which cannot catch a routing
/// regression or an accidental <c>[Authorize]</c> on an endpoint whose whole point is that an invitee — who by
/// definition has no session — can reach the API at all. A request through the genuine pipeline is the only
/// thing that proves that.</para>
///
/// <para>It also exercises what EF InMemory cannot: <c>UserInvitation.InvitedRoleIds</c> is a Postgres
/// <c>uuid[]</c> column, so its round-trip is genuinely covered only here.</para>
/// </summary>
[Collection("HttpApi")]
public sealed class AcceptInvitationApiTests
{
    private const string Subdomain = "platform";
    private const string ChosenPassword = "BrandNewPassw0rd!";

    private readonly ApiTestFactory _factory;

    public AcceptInvitationApiTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AcceptInvitation_IsReachableAnonymously_AndActivatesTheMembership()
    {
        var (rawToken, email) = await SeedInvitationAsync();

        // Deliberately an UNAUTHENTICATED client: the invitee has no session. Only the tenant header is set,
        // which is the dev stand-in for arriving on the tenant's own subdomain.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", Subdomain);

        var response = await client.PostAsJsonAsync("/api/v1/auth/accept-invitation", new
        {
            token = rawToken,
            newPassword = ChosenPassword,
        });

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "the endpoint must be routed and anonymous — a 404 means the route regressed and a 401 means " +
            "[AllowAnonymous] was lost, either of which reproduces BUG-294 exactly. Body: {0}",
            await response.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == email);
        user.PasswordHash.Should().NotBeNullOrEmpty("redemption sets the invitee's first password");

        var membership = await db.UserTenants.IgnoreQueryFilters()
            .SingleOrDefaultAsync(ut => ut.UserId == user.Id);
        membership.Should().NotBeNull("redemption is what creates the membership");

        var invitation = await db.UserInvitations.IgnoreQueryFilters()
            .SingleAsync(i => i.Email == email);
        invitation.Status.Should().Be(InvitationStatus.Accepted);
        // The uuid[] column round-trip — the part EF InMemory cannot exercise.
        invitation.InvitedRoleIds.Should().NotBeEmpty("the invited roles survived the Postgres uuid[] round-trip");
    }

    [Fact]
    public async Task AcceptInvitation_ThenLogin_Succeeds_OverRealHttp()
    {
        var (rawToken, email) = await SeedInvitationAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", Subdomain);

        (await client.PostAsJsonAsync("/api/v1/auth/accept-invitation", new
        {
            token = rawToken,
            newPassword = ChosenPassword,
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // The end-to-end statement of BUG-294: before the fix, an invited user could never sign in.
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = ChosenPassword,
        });

        login.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "an invitee who redeemed their invitation must be able to log in. Body: {0}",
            await login.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AcceptInvitation_WithAnUnknownToken_IsRejected_WithoutRevealingWhy()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", Subdomain);

        var response = await client.PostAsJsonAsync("/api/v1/auth/accept-invitation", new
        {
            token = "a-token-that-was-never-issued",
            newPassword = ChosenPassword,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContainEquivalentOf("not found",
            "the failure message must not distinguish 'no such invitation' from 'expired' or 'already used' — " +
            "that would tell an attacker which guesses were once real invitations");
    }

    /// <summary>
    /// Seeds an invitation the way the invite path does: a passwordless global user plus an Invited row whose
    /// hash is produced by the SAME helper production uses, so a change to the transformation breaks this too.
    /// </summary>
    private async Task<(string RawToken, string Email)> SeedInvitationAsync()
    {
        var (rawToken, tokenHash) = InvitationToken.Generate();
        var email = $"invitee-{Guid.NewGuid():N}@acme.test";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.IgnoreQueryFilters()
            .SingleAsync(t => t.Subdomain == Subdomain);

        // Any real role of this tenant — the point is that a uuid[] with content survives the round-trip.
        var roleId = await db.Roles.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenant.Id)
            .Select(r => r.Id)
            .FirstAsync();

        db.Users.Add(new User
        {
            Id = BaseEntity.NewUuidV7(),
            Email = email,
            PasswordHash = null,
            IsActive = true,
        });

        db.UserInvitations.Add(new UserInvitation
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenant.Id,
            Email = email,
            TokenHash = tokenHash,
            Status = InvitationStatus.Invited,
            ExpiresAt = DateTime.UtcNow.AddHours(72),
            InvitedByUserId = Guid.NewGuid(),
            InvitedRoleIds = new List<Guid> { roleId },
        });

        await db.SaveChangesAsync();

        return (rawToken, email);
    }
}
