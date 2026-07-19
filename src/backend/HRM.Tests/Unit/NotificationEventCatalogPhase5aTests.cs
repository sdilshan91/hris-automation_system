// ============================================================================
// US-NTF-006 Phase 5a — NotificationEventCatalog integrity for the twelve new recruitment events (US-REC-002/004/
// 005/006/007). All are Category=RecruitmentUpdates, NOT mandatory:
//   application_received / application_new / application_stage_changed
//   interview_scheduled / interview_updated / interview_cancelled / interview_reminder
//   scorecard_submitted
//   offer_sent / offer_withdrawn / offer_expiry_reminder / offer_expired
//
// Guards (mirror the Phase 3 / Phase 4 catalog tests):
//   (C) Catalog integrity: each event is present via Get/All (which also exercises the eager static BuildCatalog —
//       a bad static-init ordering would throw a TypeInitializationException here), with non-empty default
//       Subject/BodyHtml/BodyText, non-empty Placeholders, and the expected Category + mandatory flag.
//   (B) No-blank-placeholders: every {{token}} referenced in a template's Subject/BodyHtml/BodyText must be a
//       declared Placeholder for that event — otherwise the renderer blanks it out (blank-body class).
// ============================================================================

using System.Text.RegularExpressions;
using FluentAssertions;
using HRM.Domain.Enums;
using HRM.Domain.Notifications;

namespace HRM.Tests.Unit;

public sealed class NotificationEventCatalogPhase5aTests
{
    private static readonly string[] Phase5aEvents =
    [
        "application_received",
        "application_new",
        "application_stage_changed",
        "interview_scheduled",
        "interview_updated",
        "interview_cancelled",
        "interview_reminder",
        "scorecard_submitted",
        "offer_sent",
        "offer_withdrawn",
        "offer_expiry_reminder",
        "offer_expired",
    ];

    // ── (C) Catalog integrity: present, non-empty templates, RecruitmentUpdates + not mandatory ──
    [Theory]
    [InlineData("application_received")]
    [InlineData("application_new")]
    [InlineData("application_stage_changed")]
    [InlineData("interview_scheduled")]
    [InlineData("interview_updated")]
    [InlineData("interview_cancelled")]
    [InlineData("interview_reminder")]
    [InlineData("scorecard_submitted")]
    [InlineData("offer_sent")]
    [InlineData("offer_withdrawn")]
    [InlineData("offer_expiry_reminder")]
    [InlineData("offer_expired")]
    public void Phase5aEvent_IsPresent_WithDefaultTemplate_RecruitmentUpdates_NotMandatory(string eventKey)
    {
        NotificationEventCatalog.IsKnownEvent(eventKey).Should().BeTrue();

        var def = NotificationEventCatalog.Get(eventKey);
        def.Should().NotBeNull($"'{eventKey}' must be seeded in the catalog (BR-2: every event has a default)");

        def!.EventKey.Should().Be(eventKey);
        def.EventName.Should().NotBeNullOrWhiteSpace();
        def.DefaultSubject.Should().NotBeNullOrWhiteSpace();
        def.DefaultBodyHtml.Should().NotBeNullOrWhiteSpace();
        def.DefaultBodyText.Should().NotBeNullOrWhiteSpace();
        def.Placeholders.Should().NotBeEmpty();

        def.Category.Should().Be(NotificationCategory.RecruitmentUpdates);
        def.IsMandatory.Should().BeFalse();
    }

    [Fact]
    public void AllTwelvePhase5aEvents_AreListedInTheCatalog()
    {
        // Exercises the static All accessor (guards static-init ordering) + confirms every Phase 5a event is present.
        var keys = NotificationEventCatalog.All.Select(e => e.EventKey);
        keys.Should().Contain(Phase5aEvents);
    }

    // ── (B) No-blank-placeholders: every {{token}} in the templates is a declared placeholder ──
    [Theory]
    [InlineData("application_received")]
    [InlineData("application_new")]
    [InlineData("application_stage_changed")]
    [InlineData("interview_scheduled")]
    [InlineData("interview_updated")]
    [InlineData("interview_cancelled")]
    [InlineData("interview_reminder")]
    [InlineData("scorecard_submitted")]
    [InlineData("offer_sent")]
    [InlineData("offer_withdrawn")]
    [InlineData("offer_expiry_reminder")]
    [InlineData("offer_expired")]
    public void Phase5aEvent_TemplateTokens_AreAllDeclaredPlaceholders(string eventKey)
    {
        var def = NotificationEventCatalog.Get(eventKey)!;
        var declared = def.Placeholders.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var referenced = TemplateTokens(def.DefaultSubject)
            .Concat(TemplateTokens(def.DefaultBodyHtml))
            .Concat(TemplateTokens(def.DefaultBodyText))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        referenced.Should().NotBeEmpty("the templates reference placeholders");
        referenced.Should().OnlyContain(
            token => declared.Contains(token),
            $"every {{{{token}}}} in the '{eventKey}' templates must be a declared placeholder (no blank bodies)");
    }

    // ── DF-42: the offer_sent email must embed the candidate self-service portal magic link ──
    // Presence check (the REVERSE of guard (B), which only proves tokens→declared). The DF-42 payload-embed
    // unit test proves the service populates offer.portalUrl; this proves the template actually *references*
    // it, so a candidate on the primary PDF path receives the link. Without this, a mutation that drops
    // {{offer.portalUrl}} from the offer_sent body would survive the whole suite.
    [Fact]
    public void OfferSent_Template_ReferencesTheCandidatePortalMagicLink()
    {
        var def = NotificationEventCatalog.Get("offer_sent")!;

        def.Placeholders.Should().Contain("offer.portalUrl");

        var referenced = TemplateTokens(def.DefaultSubject)
            .Concat(TemplateTokens(def.DefaultBodyHtml))
            .Concat(TemplateTokens(def.DefaultBodyText))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        referenced.Should().Contain(
            "offer.portalUrl",
            "DF-42: the offer_sent template body must render the candidate portal magic link (offer.portalUrl)");
    }

    private static IEnumerable<string> TemplateTokens(string template) =>
        Regex.Matches(template, @"\{\{\s*([^}]+?)\s*\}\}").Select(m => m.Groups[1].Value.Trim());
}
