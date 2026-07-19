// ============================================================================
// US-NTF-006 Phase 6 — NotificationEventCatalog integrity for the eight new attendance-family events
// (US-ATT-001 FR-5, US-ATT-004 FR-4/FR-5, US-ATT-006 FR-8). All are Category=AttendanceAlerts, NOT mandatory:
//   attendance_late
//   attendance_regularization_requested / attendance_regularization_approved / attendance_regularization_rejected
//   overtime_maxima_exceeded / overtime_preapproval_requested / overtime_approved / overtime_rejected
//
// Guards (mirror the Phase 3 / 4 / 5a / 5b catalog tests):
//   (C) Catalog integrity: each event is present via Get/All (which also exercises the eager static BuildCatalog —
//       a bad static-init ordering would throw a TypeInitializationException here), with non-empty default
//       Subject/BodyHtml/BodyText, non-empty Placeholders, and the expected Category + mandatory flag.
//   (B) No-blank-placeholders: every {{token}} referenced in a template's Subject/BodyHtml/BodyText must be a
//       declared Placeholder for that event — otherwise the renderer blanks it out.
// ============================================================================

using System.Text.RegularExpressions;
using FluentAssertions;
using HRM.Domain.Enums;
using HRM.Domain.Notifications;

namespace HRM.Tests.Unit;

public sealed class NotificationEventCatalogPhase6Tests
{
    private static readonly string[] Phase6Events =
    [
        "attendance_late",
        // US-ATT-008 FR-7 (DF-33 / ISSUE-087): chronic-lateness HR escalation.
        "attendance_chronic_lateness",
        "attendance_regularization_requested",
        "attendance_regularization_approved",
        "attendance_regularization_rejected",
        "overtime_maxima_exceeded",
        "overtime_preapproval_requested",
        "overtime_approved",
        "overtime_rejected",
    ];

    // ── (C) Catalog integrity: present, non-empty templates, AttendanceAlerts + not mandatory ──
    [Theory]
    [InlineData("attendance_late")]
    [InlineData("attendance_chronic_lateness")]
    [InlineData("attendance_regularization_requested")]
    [InlineData("attendance_regularization_approved")]
    [InlineData("attendance_regularization_rejected")]
    [InlineData("overtime_maxima_exceeded")]
    [InlineData("overtime_preapproval_requested")]
    [InlineData("overtime_approved")]
    [InlineData("overtime_rejected")]
    [Trait("TC", "TC-ATT-161")]
    public void Phase6Event_IsPresent_WithDefaultTemplate_AttendanceAlerts_NotMandatory(string eventKey)
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

        def.Category.Should().Be(NotificationCategory.AttendanceAlerts);
        def.IsMandatory.Should().BeFalse();
    }

    [Fact]
    [Trait("TC", "TC-ATT-161")]
    public void AllEightPhase6Events_AreListedInTheCatalog()
    {
        // Exercises the static All accessor (guards static-init ordering) + confirms every Phase 6 event is present.
        var keys = NotificationEventCatalog.All.Select(e => e.EventKey);
        keys.Should().Contain(Phase6Events);
    }

    // ── (B) No-blank-placeholders: every {{token}} in the templates is a declared placeholder ──
    [Theory]
    [InlineData("attendance_late")]
    [InlineData("attendance_chronic_lateness")]
    [InlineData("attendance_regularization_requested")]
    [InlineData("attendance_regularization_approved")]
    [InlineData("attendance_regularization_rejected")]
    [InlineData("overtime_maxima_exceeded")]
    [InlineData("overtime_preapproval_requested")]
    [InlineData("overtime_approved")]
    [InlineData("overtime_rejected")]
    [Trait("TC", "TC-ATT-161")]
    public void Phase6Event_TemplateTokens_AreAllDeclaredPlaceholders(string eventKey)
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
