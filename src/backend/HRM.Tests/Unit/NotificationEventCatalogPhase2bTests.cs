// ============================================================================
// US-NTF-006 Phase 2b — NotificationEventCatalog integrity for the four new events.
// Every event must be present with non-empty default Subject/BodyHtml/BodyText and
// the expected Category/IsMandatory:
//   • user_invitation        → SystemAnnouncements, NOT mandatory
//   • tenant_welcome_trial   → SystemAnnouncements, NOT mandatory
//   • tenant_welcome_active  → SystemAnnouncements, NOT mandatory
//   • admin_password_reset   → SecurityAlerts,      mandatory
//
// Touching NotificationEventCatalog.Get/All also exercises the eager static
// BuildCatalog() — a bad static-init ordering (a null shared placeholder array
// referenced before declaration) would throw a TypeInitializationException here.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Enums;
using HRM.Domain.Notifications;

namespace HRM.Tests.Unit;

public sealed class NotificationEventCatalogPhase2bTests
{
    [Theory]
    [InlineData("user_invitation", NotificationCategory.SystemAnnouncements, false)]
    [InlineData("tenant_welcome_trial", NotificationCategory.SystemAnnouncements, false)]
    [InlineData("tenant_welcome_active", NotificationCategory.SystemAnnouncements, false)]
    [InlineData("admin_password_reset", NotificationCategory.SecurityAlerts, true)]
    public void Phase2bEvent_IsPresent_WithDefaultTemplate_AndExpectedCategoryAndMandatoryFlag(
        string eventKey, NotificationCategory expectedCategory, bool expectedMandatory)
    {
        NotificationEventCatalog.IsKnownEvent(eventKey).Should().BeTrue();

        var def = NotificationEventCatalog.Get(eventKey);
        def.Should().NotBeNull($"'{eventKey}' must be seeded in the catalog (BR-2: every event has a default)");

        def!.EventKey.Should().Be(eventKey);
        def.EventName.Should().NotBeNullOrWhiteSpace();
        def.DefaultSubject.Should().NotBeNullOrWhiteSpace();
        def.DefaultBodyHtml.Should().NotBeNullOrWhiteSpace();
        def.DefaultBodyText.Should().NotBeNullOrWhiteSpace();

        def.Category.Should().Be(expectedCategory);
        def.IsMandatory.Should().Be(expectedMandatory);
    }

    [Fact]
    public void AllFourPhase2bEvents_AreListedInTheCatalog()
    {
        var keys = NotificationEventCatalog.All.Select(e => e.EventKey);
        keys.Should().Contain(new[]
        {
            "user_invitation", "admin_password_reset", "tenant_welcome_trial", "tenant_welcome_active",
        });
    }
}
