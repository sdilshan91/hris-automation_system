// ============================================================================
// US-NTF-006 Phase 2b — RealUserManagementNotificationService (US-ADM-005).
// Replaces the LogOnly stub. Two email seams, both email-only (raw address) and
// both never-throw:
//   • SendInvitationAsync   → event "user_invitation" to the RAW Email, accept
//     link embeds the RAW one-time token (accept-invite?token=...).
//   • SendPasswordResetAsync → event "admin_password_reset" to the RAW Email,
//     INFORMATIONAL only — NO reset link/token in the payload.
//
// The INotificationDispatcher is substituted (NSubstitute); we capture the
// NotificationRequest and inspect its EventKey / RecipientEmail / PayloadJson.
// ============================================================================

using System.Text.Json;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class RealUserManagementNotificationServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly INotificationDispatcher _dispatcher = Substitute.For<INotificationDispatcher>();

    private RealUserManagementNotificationService CreateService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Platform:BaseDomain"] = "yourhrm.test" })
            .Build();
        return new RealUserManagementNotificationService(
            _dispatcher, config, NullLogger<RealUserManagementNotificationService>.Instance);
    }

    // ── #5 invitation → user_invitation, RAW email recipient, accept link embeds the RAW token ──
    [Fact]
    public async Task SendInvitation_DispatchesUserInvitation_ToRawEmail_WithAcceptLinkToken()
    {
        NotificationRequest? captured = null;
        _dispatcher
            .SendEmailAsync(Arg.Do<NotificationRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        const string rawToken = "invite-raw-token-abc123";
        var message = new UserInvitationEmailMessage(
            TenantId: _tenantId,
            TenantName: "Acme Corporation",
            Subdomain: "acme",
            Email: "new.user@example.com",
            RawToken: rawToken,
            ExpiresAt: DateTime.UtcNow.AddHours(72));

        await CreateService().SendInvitationAsync(message);

        await _dispatcher.Received(1).SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
        captured.Should().NotBeNull();
        captured!.EventKey.Should().Be("user_invitation");
        captured.RecipientEmail.Should().Be(message.Email, "the invitee has no User row → raw-address recipient");
        captured.RecipientUserId.Should().BeNull();
        captured.TenantId.Should().Be(_tenantId);

        using var doc = JsonDocument.Parse(captured.PayloadJson);
        var acceptUrl = doc.RootElement.GetProperty("invitation").GetProperty("acceptUrl").GetString();
        acceptUrl.Should().NotBeNullOrWhiteSpace();
        acceptUrl!.Should().Contain($"accept-invite?token={rawToken}");
    }

    // ── #5 never-throw: a dispatcher failure must not break invitation persistence/provisioning ──
    [Fact]
    public async Task SendInvitation_WhenDispatcherThrows_DoesNotThrow()
    {
        _dispatcher
            .When(d => d.SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("smtp down"));

        var act = async () => await CreateService().SendInvitationAsync(new UserInvitationEmailMessage(
            _tenantId, "Acme Corporation", "acme", "new.user@example.com", "tok", DateTime.UtcNow.AddHours(72)));

        await act.Should().NotThrowAsync();
    }

    // ── #6 admin-forced reset → admin_password_reset, RAW email, NO reset link/token (informational) ──
    [Fact]
    public async Task SendPasswordReset_DispatchesAdminPasswordReset_Informational_WithNoTokenOrLink()
    {
        NotificationRequest? captured = null;
        _dispatcher
            .SendEmailAsync(Arg.Do<NotificationRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var message = new PasswordResetEmailMessage(
            TenantId: _tenantId, Subdomain: "acme", Email: "jane.doe@example.com");

        await CreateService().SendPasswordResetAsync(message);

        await _dispatcher.Received(1).SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
        captured.Should().NotBeNull();
        captured!.EventKey.Should().Be("admin_password_reset");
        captured.RecipientEmail.Should().Be(message.Email);
        captured.RecipientUserId.Should().BeNull();

        // INFORMATIONAL: the payload must carry NO reset link and NO token — the user is pointed at self-service.
        captured.PayloadJson.Should().NotContain("token", "an admin-forced reset email is informational — no token");
        captured.PayloadJson.Should().NotContain("reset-password", "no reset link is embedded (informational)");
        using var doc = JsonDocument.Parse(captured.PayloadJson);
        doc.RootElement.TryGetProperty("reset", out _).Should().BeFalse("there is no reset.url node — informational");
    }

    // ── #6 never-throw: a dispatcher failure must not break the admin force-reset flow ──
    [Fact]
    public async Task SendPasswordReset_WhenDispatcherThrows_DoesNotThrow()
    {
        _dispatcher
            .When(d => d.SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("smtp down"));

        var act = async () => await CreateService().SendPasswordResetAsync(
            new PasswordResetEmailMessage(_tenantId, "acme", "jane.doe@example.com"));

        await act.Should().NotThrowAsync();
    }
}
