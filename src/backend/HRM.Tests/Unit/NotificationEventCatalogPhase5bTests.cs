// ============================================================================
// US-NTF-006 Phase 5b (the FINAL seam) — NotificationEventCatalog integrity for the seventeen new performance
// events (US-PRF-001..009). All are Category=PerformanceReviews, NOT mandatory:
//   goal_assigned / goal_modified / goal_removed
//   self_assessment_submitted / self_assessment_reminder
//   manager_review_submitted / cycle_event
//   reviewer_assigned / reviewer_reminder
//   review_signoff_requested / review_disputed / review_auto_closed
//   pip_event
//   goal_progress_updated / goal_blocked / goal_stale_nudge / goal_comment_added
//
// Guards (mirror the Phase 3 / 4 / 5a catalog tests):
//   (C) Catalog integrity: each event is present via Get/All (which also exercises the eager static BuildCatalog —
//       a bad static-init ordering would throw a TypeInitializationException here), with non-empty default
//       Subject/BodyHtml/BodyText, non-empty Placeholders, and the expected Category + mandatory flag.
//   (B) No-blank-placeholders: every {{token}} referenced in a template's Subject/BodyHtml/BodyText must be a
//       declared Placeholder for that event — otherwise the renderer blanks it out (the blank-body class that the
//       Phase 5a guard caught a miss on).
// ============================================================================

using System.Text.RegularExpressions;
using FluentAssertions;
using HRM.Domain.Enums;
using HRM.Domain.Notifications;

namespace HRM.Tests.Unit;

public sealed class NotificationEventCatalogPhase5bTests
{
    private static readonly string[] Phase5bEvents =
    [
        "goal_assigned",
        "goal_modified",
        "goal_removed",
        "self_assessment_submitted",
        "self_assessment_reminder",
        "manager_review_submitted",
        "cycle_event",
        "reviewer_assigned",
        "reviewer_reminder",
        "review_signoff_requested",
        "review_disputed",
        "review_auto_closed",
        "pip_event",
        "goal_progress_updated",
        "goal_blocked",
        "goal_stale_nudge",
        "goal_comment_added",
    ];

    // ── (C) Catalog integrity: present, non-empty templates, PerformanceReviews + not mandatory ──
    [Theory]
    [InlineData("goal_assigned")]
    [InlineData("goal_modified")]
    [InlineData("goal_removed")]
    [InlineData("self_assessment_submitted")]
    [InlineData("self_assessment_reminder")]
    [InlineData("manager_review_submitted")]
    [InlineData("cycle_event")]
    [InlineData("reviewer_assigned")]
    [InlineData("reviewer_reminder")]
    [InlineData("review_signoff_requested")]
    [InlineData("review_disputed")]
    [InlineData("review_auto_closed")]
    [InlineData("pip_event")]
    [InlineData("goal_progress_updated")]
    [InlineData("goal_blocked")]
    [InlineData("goal_stale_nudge")]
    [InlineData("goal_comment_added")]
    public void Phase5bEvent_IsPresent_WithDefaultTemplate_PerformanceReviews_NotMandatory(string eventKey)
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

        def.Category.Should().Be(NotificationCategory.PerformanceReviews);
        def.IsMandatory.Should().BeFalse();
    }

    [Fact]
    public void AllSeventeenPhase5bEvents_AreListedInTheCatalog()
    {
        // Exercises the static All accessor (guards static-init ordering) + confirms every Phase 5b event is present.
        var keys = NotificationEventCatalog.All.Select(e => e.EventKey);
        keys.Should().Contain(Phase5bEvents);
    }

    // ── (B) No-blank-placeholders: every {{token}} in the templates is a declared placeholder ──
    [Theory]
    [InlineData("goal_assigned")]
    [InlineData("goal_modified")]
    [InlineData("goal_removed")]
    [InlineData("self_assessment_submitted")]
    [InlineData("self_assessment_reminder")]
    [InlineData("manager_review_submitted")]
    [InlineData("cycle_event")]
    [InlineData("reviewer_assigned")]
    [InlineData("reviewer_reminder")]
    [InlineData("review_signoff_requested")]
    [InlineData("review_disputed")]
    [InlineData("review_auto_closed")]
    [InlineData("pip_event")]
    [InlineData("goal_progress_updated")]
    [InlineData("goal_blocked")]
    [InlineData("goal_stale_nudge")]
    [InlineData("goal_comment_added")]
    public void Phase5bEvent_TemplateTokens_AreAllDeclaredPlaceholders(string eventKey)
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

    private static IEnumerable<string> TemplateTokens(string template) =>
        Regex.Matches(template, @"\{\{\s*([^}]+?)\s*\}\}").Select(m => m.Groups[1].Value.Trim());
}
