// ============================================================================
// US-REC-008 NFR-6: ApplicantPortalTokenService rate limiting.
// ISSUE-130: the per-email guard misses enumeration via rotating emails; a
// per-IP throttle caps how many tokens one IP may issue (across all emails).
// Uses EF Core InMemory through the real service.
// ============================================================================

using System.Net;
using System.Text.Json;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class ApplicantPortalTokenServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _config;

    public ApplicantPortalTokenServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.Subdomain.Returns("acme");
        _tenantContext.IsResolved.Returns(true);

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Recruitment:PortalTokenSecret"] = "unit-test-portal-secret-0123456789abcdef",
                ["Platform:BaseDomain"] = "yourhrm.com",
            })
            .Build();
    }

    /// <summary>Records the email legs dispatched via the canonical templated path (DF-41 magic-link send).</summary>
    private sealed class RecordingDispatcher : INotificationDispatcher
    {
        public List<NotificationRequest> Email { get; } = new();
        public Task SendInAppAsync(NotificationRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task SendEmailAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            Email.Add(request);
            return Task.CompletedTask;
        }
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private ApplicantPortalTokenService Service(string? ip, INotificationDispatcher? dispatcher = null)
    {
        IHttpContextAccessor? accessor = null;
        if (ip is not null)
        {
            var ctx = new DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip);
            accessor = Substitute.For<IHttpContextAccessor>();
            accessor.HttpContext.Returns(ctx);
        }
        return new ApplicantPortalTokenService(
            Db(), _tenantContext, _config, dispatcher ?? new RecordingDispatcher(),
            NullLogger<ApplicantPortalTokenService>.Instance, accessor);
    }

    private async Task SeedApplicantAsync(string email)
    {
        using var db = Db();
        db.Applicants.Add(new Applicant
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, FirstName = "Jordan", LastName = "Rivera",
            Email = email, ApplicationReferenceNumber = "APP-2026-000123",
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Issue_throttles_by_IP_across_rotating_emails_issue130()
    {
        const string ip = "203.0.113.7";

        // 10 issues with DISTINCT emails from one IP all succeed (the per-email guard never fires).
        for (int i = 0; i < 10; i++)
            (await Service(ip).IssueAsync($"cand{i}@x.test")).IsSuccess.Should().BeTrue();

        // The 11th from the SAME IP (a fresh email) is throttled by the per-IP cap.
        var blocked = await Service(ip).IssueAsync("cand-extra@x.test");

        blocked.IsFailure.Should().BeTrue();
        blocked.StatusCode.Should().Be(429);
        blocked.ErrorCode.Should().Be("rate_limited");
    }

    [Fact]
    public async Task Issue_from_a_different_IP_is_not_throttled_issue130()
    {
        for (int i = 0; i < 10; i++)
            await Service("203.0.113.7").IssueAsync($"cand{i}@x.test");

        // A different IP is unaffected by the first IP's count.
        var other = await Service("198.51.100.9").IssueAsync("cand-extra@x.test");

        other.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Issue_without_httpcontext_skips_the_ip_throttle_issue130()
    {
        // No HttpContext (e.g. a background path) → the per-IP throttle is skipped; distinct-email issues all pass.
        for (int i = 0; i < 11; i++)
            (await Service(null).IssueAsync($"cand{i}@x.test")).IsSuccess.Should().BeTrue();
    }

    // ── DF-41: RequestLinkAsync now EMAILS the magic link (was a log-only seam). ──

    [Fact]
    public async Task RequestLink_WithApplication_DispatchesExactlyOneMagicLinkEmail_ContainingIssuedToken()
    {
        const string email = "jordan.rivera@example.com";
        await SeedApplicantAsync(email);
        var dispatcher = new RecordingDispatcher();

        var result = await Service(null, dispatcher).RequestLinkAsync(email);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();

        // Exactly one email, to the applicant, on the new catalog event, carrying the portal magic link.
        dispatcher.Email.Should().ContainSingle();
        var sent = dispatcher.Email.Single();
        sent.EventKey.Should().Be("applicant_portal_link");
        sent.RecipientEmail.Should().Be(email);
        sent.TenantId.Should().Be(_tenantId);

        // The payload embeds the /portal?token= link with a GENUINE, working token (round-trips through ValidateAsync).
        var portalUrl = JsonDocument.Parse(sent.PayloadJson)
            .RootElement.GetProperty("portal").GetProperty("url").GetString();
        portalUrl.Should().StartWith("https://acme.yourhrm.com/portal?token=");

        var token = portalUrl!.Substring(portalUrl.IndexOf("token=", StringComparison.Ordinal) + "token=".Length);
        token.Should().NotBeNullOrWhiteSpace();
        var validated = await Service(null).ValidateAsync(token);
        validated.IsSuccess.Should().BeTrue("the emailed token must be the issued, valid magic-link token");
        validated.Value!.ApplicantEmail.Should().Be(email);
    }

    [Fact]
    public async Task RequestLink_WithoutApplication_DoesNotDispatch_ButStillReturnsSuccess_BR5()
    {
        // BR-5 anti-enumeration: no application for this email → no token, NO email, yet the caller still sees success
        // (so it cannot infer whether the email exists).
        var dispatcher = new RecordingDispatcher();

        var result = await Service(null, dispatcher).RequestLinkAsync("nobody@nowhere.test");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
        dispatcher.Email.Should().BeEmpty("no application exists → the magic-link email must not be sent (BR-5)");
    }
}
