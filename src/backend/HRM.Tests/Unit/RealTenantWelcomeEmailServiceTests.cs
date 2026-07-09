// ============================================================================
// US-NTF-006 Phase 2b — RealTenantWelcomeEmailService (US-ADM-001 FR-4).
// Replaces the LogOnly stub. Email-only (raw owner address), INFORMATIONAL:
//   • IsTrial=true  → event "tenant_welcome_trial"
//   • IsTrial=false → event "tenant_welcome_active"
// Payload carries a forgotPassword.url (self-service first-password) and NO
// set-password token/link. Recipient == raw OwnerEmail. Never throws.
//
// The INotificationDispatcher is substituted (NSubstitute); we capture the
// NotificationRequest and inspect EventKey / RecipientEmail / PayloadJson.
// ============================================================================

using System.Text.Json;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class RealTenantWelcomeEmailServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly INotificationDispatcher _dispatcher = Substitute.For<INotificationDispatcher>();

    private RealTenantWelcomeEmailService CreateService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Platform:BaseDomain"] = "yourhrm.test" })
            .Build();
        return new RealTenantWelcomeEmailService(
            _dispatcher, config, NullLogger<RealTenantWelcomeEmailService>.Instance);
    }

    private TenantWelcomeEmailMessage Message(bool isTrial) => new(
        TenantId: _tenantId,
        TenantName: "Acme Corporation",
        Subdomain: "acme",
        OwnerEmail: "owner@example.com",
        OwnerName: "Sam Owner",
        OwnerExists: false,
        IsTrial: isTrial);

    // ── #7 trial vs active selects the event key; recipient is the RAW owner email; payload has
    //        forgotPassword.url and NO set-password token ──────────────────────────────────────────
    [Theory]
    [InlineData(true, "tenant_welcome_trial")]
    [InlineData(false, "tenant_welcome_active")]
    public async Task SendWelcome_SelectsEventByTrialFlag_ToRawOwnerEmail_WithForgotPasswordUrl_NoToken(
        bool isTrial, string expectedEventKey)
    {
        NotificationRequest? captured = null;
        _dispatcher
            .SendEmailAsync(Arg.Do<NotificationRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await CreateService().SendWelcomeAsync(Message(isTrial));

        await _dispatcher.Received(1).SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
        captured.Should().NotBeNull();
        captured!.EventKey.Should().Be(expectedEventKey);
        captured.RecipientEmail.Should().Be("owner@example.com", "email-only: raw owner address, no in-app leg");
        captured.RecipientUserId.Should().BeNull();
        captured.TenantId.Should().Be(_tenantId);

        using var doc = JsonDocument.Parse(captured.PayloadJson);
        var forgotUrl = doc.RootElement.GetProperty("forgotPassword").GetProperty("url").GetString();
        forgotUrl.Should().NotBeNullOrWhiteSpace();
        forgotUrl!.Should().Contain("/forgot-password");

        // INFORMATIONAL: no set-password token/link is embedded — the owner uses self-service Forgot Password.
        captured.PayloadJson.Should().NotContain("token", "the welcome email carries no set-password token");
        captured.PayloadJson.Should().NotContain("set-password", "no set-password link is embedded (informational)");
    }

    // ── #7 never-throw: a dispatcher failure must not block tenant provisioning (§11 partial-failure) ──
    [Fact]
    public async Task SendWelcome_WhenDispatcherThrows_DoesNotThrow()
    {
        _dispatcher
            .When(d => d.SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("smtp down"));

        var act = async () => await CreateService().SendWelcomeAsync(Message(isTrial: true));

        await act.Should().NotThrowAsync();
    }
}
