// ============================================================================
// US-NTF-006 Phase 3 — NotificationEventCatalog integrity for the seven new events:
//   leave_requested / leave_rejected / leave_cancelled / leave_lop  → LeaveUpdates, NOT mandatory
//   onboarding_checklist_assigned / onboarding_task_completed
//     / onboarding_task_overdue                                     → OnboardingOffboarding, NOT mandatory
//
// Guards:
//   (C) Catalog integrity: each event is present via Get/All (which also exercises the eager
//       static BuildCatalog — a bad static-init ordering would throw a TypeInitializationException
//       here), with non-empty default Subject/BodyHtml/BodyText and the expected Category/mandatory.
//   (B) No-blank-placeholders: every {{placeholder}} token referenced in a template's
//       Subject/BodyHtml/BodyText must be a declared Placeholder for that event — otherwise the
//       renderer would blank it out (the blank-body class). For the onboarding events the declared
//       (flat) placeholders must also match the payload fields, proxied by SampleData keys.
// ============================================================================

using System.Text.RegularExpressions;
using FluentAssertions;
using HRM.Domain.Enums;
using HRM.Domain.Notifications;

namespace HRM.Tests.Unit;

public sealed class NotificationEventCatalogPhase3Tests
{
    private static readonly string[] LeaveEvents =
        ["leave_requested", "leave_rejected", "leave_cancelled", "leave_lop"];

    private static readonly string[] OnboardingEvents =
        ["onboarding_checklist_assigned", "onboarding_task_completed", "onboarding_task_overdue"];

    // ── (C) Catalog integrity: present, non-empty templates, expected Category + mandatory flag ──
    [Theory]
    [InlineData("leave_requested", NotificationCategory.LeaveUpdates, false)]
    [InlineData("leave_rejected", NotificationCategory.LeaveUpdates, false)]
    [InlineData("leave_cancelled", NotificationCategory.LeaveUpdates, false)]
    [InlineData("leave_lop", NotificationCategory.LeaveUpdates, false)]
    [InlineData("onboarding_checklist_assigned", NotificationCategory.OnboardingOffboarding, false)]
    [InlineData("onboarding_task_completed", NotificationCategory.OnboardingOffboarding, false)]
    [InlineData("onboarding_task_overdue", NotificationCategory.OnboardingOffboarding, false)]
    public void Phase3Event_IsPresent_WithDefaultTemplate_AndExpectedCategoryAndMandatoryFlag(
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
    public void AllSevenPhase3Events_AreListedInTheCatalog()
    {
        var keys = NotificationEventCatalog.All.Select(e => e.EventKey);
        keys.Should().Contain(LeaveEvents.Concat(OnboardingEvents));
    }

    // ── (B) No-blank-placeholders: every {{token}} in the templates is a declared placeholder ──
    [Theory]
    [InlineData("leave_requested")]
    [InlineData("leave_rejected")]
    [InlineData("leave_cancelled")]
    [InlineData("leave_lop")]
    [InlineData("onboarding_checklist_assigned")]
    [InlineData("onboarding_task_completed")]
    [InlineData("onboarding_task_overdue")]
    public void Phase3Event_TemplateTokens_AreAllDeclaredPlaceholders(string eventKey)
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

    // ── (B') Onboarding placeholders match the payload fields (proxied by the flat SampleData keys) ──
    [Theory]
    [InlineData("onboarding_checklist_assigned")]
    [InlineData("onboarding_task_completed")]
    [InlineData("onboarding_task_overdue")]
    public void OnboardingEvent_DeclaredPlaceholders_MatchPayloadFields(string eventKey)
    {
        var def = NotificationEventCatalog.Get(eventKey)!;
        // Onboarding payloads are FLAT; SampleData mirrors the outbox payload shape.
        var payloadFields = def.SampleData.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        def.Placeholders.Should().OnlyContain(
            p => payloadFields.Contains(p),
            $"every declared placeholder for '{eventKey}' must be provided by the outbox payload");
    }

    private static IEnumerable<string> TemplateTokens(string template) =>
        Regex.Matches(template, @"\{\{\s*([^}]+?)\s*\}\}").Select(m => m.Groups[1].Value.Trim());
}
