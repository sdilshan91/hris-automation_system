// ============================================================================
// US-NTF-006 Phase 4 — NotificationEventCatalog integrity for the seven new events:
//   data_export_ready                                              → SystemAnnouncements, NOT mandatory
//   payroll_run_ready / payroll_approval_submitted|approved
//     |rejected|returned / payroll_finalized                       → PayrollNotifications, NOT mandatory
//
// Guards (mirror the Phase 3 catalog tests):
//   (C) Catalog integrity: each event is present via Get/All (which also exercises the eager static BuildCatalog —
//       a bad static-init ordering would throw a TypeInitializationException here), with non-empty default
//       Subject/BodyHtml/BodyText and the expected Category / mandatory flag.
//   (B) No-blank-placeholders: every {{placeholder}} token referenced in a template's Subject/BodyHtml/BodyText
//       must be a declared Placeholder for that event — otherwise the renderer would blank it out (blank-body class).
// ============================================================================

using System.Text.RegularExpressions;
using FluentAssertions;
using HRM.Domain.Enums;
using HRM.Domain.Notifications;

namespace HRM.Tests.Unit;

public sealed class NotificationEventCatalogPhase4Tests
{
    private static readonly string[] Phase4Events =
    [
        "data_export_ready",
        "payroll_run_ready",
        "payroll_approval_submitted",
        "payroll_approval_approved",
        "payroll_approval_rejected",
        "payroll_approval_returned",
        "payroll_finalized",
    ];

    // ── (C) Catalog integrity: present, non-empty templates, expected Category + mandatory flag ──
    [Theory]
    [InlineData("data_export_ready", NotificationCategory.SystemAnnouncements, false)]
    [InlineData("payroll_run_ready", NotificationCategory.PayrollNotifications, false)]
    [InlineData("payroll_approval_submitted", NotificationCategory.PayrollNotifications, false)]
    [InlineData("payroll_approval_approved", NotificationCategory.PayrollNotifications, false)]
    [InlineData("payroll_approval_rejected", NotificationCategory.PayrollNotifications, false)]
    [InlineData("payroll_approval_returned", NotificationCategory.PayrollNotifications, false)]
    [InlineData("payroll_finalized", NotificationCategory.PayrollNotifications, false)]
    public void Phase4Event_IsPresent_WithDefaultTemplate_AndExpectedCategoryAndMandatoryFlag(
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
        def.Placeholders.Should().NotBeEmpty();

        def.Category.Should().Be(expectedCategory);
        def.IsMandatory.Should().Be(expectedMandatory);
    }

    [Fact]
    public void AllSevenPhase4Events_AreListedInTheCatalog()
    {
        // Exercises the static All accessor (guards static-init ordering) + confirms every Phase 4 event is present.
        var keys = NotificationEventCatalog.All.Select(e => e.EventKey);
        keys.Should().Contain(Phase4Events);
    }

    // ── (B) No-blank-placeholders: every {{token}} in the templates is a declared placeholder ──
    [Theory]
    [InlineData("data_export_ready")]
    [InlineData("payroll_run_ready")]
    [InlineData("payroll_approval_submitted")]
    [InlineData("payroll_approval_approved")]
    [InlineData("payroll_approval_rejected")]
    [InlineData("payroll_approval_returned")]
    [InlineData("payroll_finalized")]
    public void Phase4Event_TemplateTokens_AreAllDeclaredPlaceholders(string eventKey)
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
