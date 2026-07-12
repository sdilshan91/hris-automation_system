// ============================================================================
// US-NTF-006 Phase 8 (tail) — NotificationEventCatalog integrity for the two final events that complete the
// US-NTF-006 delivery arc:
//   bulk_import_completed   (SystemAnnouncements) — US-CHR-010 async import completion, email-only to the initiator
//   leave_report_ready      (LeaveUpdates)        — US-LV-012 FR-5/AC-5 export-ready, in-app + email to the requester
//
// Guards (mirror the Phase 3 / 4 / 5a / 5b / 6 / 7 catalog tests):
//   (C) Catalog integrity: each event is present via Get/All (which also exercises the eager static BuildCatalog —
//       a bad static-init ordering would throw a TypeInitializationException here), with non-empty default
//       Subject/BodyHtml/BodyText, non-empty Placeholders, the expected Category, and NOT mandatory.
//   (B) No-blank-placeholders: every {{token}} referenced in a template's Subject/BodyHtml/BodyText must be a
//       declared Placeholder for that event — otherwise the renderer blanks it out.
// ============================================================================

using System.Text.RegularExpressions;
using FluentAssertions;
using HRM.Domain.Enums;
using HRM.Domain.Notifications;

namespace HRM.Tests.Unit;

public sealed class NotificationEventCatalogPhase8Tests
{
    private static readonly string[] Phase8Events =
    [
        "bulk_import_completed",
        "leave_report_ready",
    ];

    // ── (C) Catalog integrity: present, non-empty templates, expected Category + not mandatory ──
    [Theory]
    [InlineData("bulk_import_completed", NotificationCategory.SystemAnnouncements)]
    [InlineData("leave_report_ready", NotificationCategory.LeaveUpdates)]
    public void Phase8Event_IsPresent_WithDefaultTemplate_ExpectedCategory_NotMandatory(
        string eventKey, NotificationCategory expectedCategory)
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

        def.Category.Should().Be(expectedCategory);
        def.IsMandatory.Should().BeFalse();
    }

    [Fact]
    public void BothPhase8Events_AreListedInTheCatalog()
    {
        // Exercises the static All accessor (guards static-init ordering) + confirms every Phase 8 event is present.
        var keys = NotificationEventCatalog.All.Select(e => e.EventKey);
        keys.Should().Contain(Phase8Events);
    }

    // ── (B) No-blank-placeholders: every {{token}} in the templates is a declared placeholder ──
    [Theory]
    [InlineData("bulk_import_completed")]
    [InlineData("leave_report_ready")]
    public void Phase8Event_TemplateTokens_AreAllDeclaredPlaceholders(string eventKey)
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
