// ============================================================================
// ISSUE-063 regression (US-AUTH-010 FR-8, TC-AUTH-100): the account-lockout notification
// CONTENT must be fully built (recipient name, lockout duration, when access is restored,
// wait/contact instructions, support-contact link) — pre-fix it was a bare `[LOCKOUT-EMAIL-STUB]`
// TODO. Real SMTP delivery itself stays deferred to US-NTF-006; this asserts the assembled
// payload so enabling delivery is a one-class swap.
//
// The content builder is pure/deterministic, so it is asserted directly.
// ============================================================================

using FluentAssertions;
using HRM.Infrastructure.Services;

namespace HRM.Tests.Unit;

public sealed class LockoutNotificationContentTests
{
    private static readonly DateTime LockedUntil = new(2026, 7, 18, 14, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void BuildContent_IncludesAllRequiredFields()
    {
        var content = LockoutNotificationService.BuildContent(
            userEmail: "qa-lockout-1@acme.test",
            displayName: "QA Lockout One",
            lockedUntilUtc: LockedUntil,
            lockoutDurationMinutes: 15,
            supportContact: "support@acme.test");

        content.Subject.Should().Be("Your account has been temporarily locked");

        // FR-8 Data Requirements: recipient name, duration, restore time, wait/contact instructions, support link.
        content.BodyText.Should().Contain("QA Lockout One", "the recipient's name must appear");
        content.BodyText.Should().Contain("15 minute", "the lockout duration must appear");
        content.BodyText.Should().Contain("UTC", "the restore time must be stated");
        content.BodyText.Should().Contain("support@acme.test", "the support-contact link must be present");
        content.BodyText.Should().Contain("reset your password", "the wait/contact recovery instruction must be present");
        content.BodyText.Should().NotContain("TODO", "the content must be assembled, not a placeholder");
    }

    [Fact]
    public void BuildContent_FallsBackToEmailAndDefaultSupport_WhenNamesAndContactMissing()
    {
        var content = LockoutNotificationService.BuildContent(
            userEmail: "noname@acme.test",
            displayName: null,
            lockedUntilUtc: LockedUntil,
            lockoutDurationMinutes: 30,
            supportContact: null);

        content.BodyText.Should().Contain("noname@acme.test", "with no display name, greet by email");
        content.BodyText.Should().Contain("30 minute");
        content.BodyText.Should().Contain("your system administrator", "a missing support link falls back to a default contact");
    }

    // ── ISSUE-063 (FR-8): the lockout email is branded with the tenant name when it is known ──
    [Fact]
    public void BuildContent_BrandsWithTenantName_WhenPresent()
    {
        var content = LockoutNotificationService.BuildContent(
            userEmail: "qa@acme.test",
            displayName: "QA User",
            lockedUntilUtc: LockedUntil,
            lockoutDurationMinutes: 15,
            supportContact: "support@acme.test",
            tenantName: "Acme Corporation");

        content.BodyText.Should().Contain("Acme Corporation", "FR-8: the tenant name must appear when resolved");
    }

    [Fact]
    public void BuildContent_DegradesGracefully_WhenTenantNameNull()
    {
        // A cross-tenant login whose tenant could not be resolved → null tenant name must not break the email.
        var content = LockoutNotificationService.BuildContent(
            userEmail: "qa@acme.test",
            displayName: "QA User",
            lockedUntilUtc: LockedUntil,
            lockoutDurationMinutes: 15,
            supportContact: "support@acme.test",
            tenantName: null);

        content.Subject.Should().NotBeNullOrWhiteSpace();
        content.BodyText.Should().Contain("QA User").And.NotContain("TODO");
    }
}
