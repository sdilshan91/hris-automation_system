// ============================================================================
// US-ADM-011b (Part 4): notification-catalog integrity for the workflow-runtime events.
// Asserts the catalog still type-inits (no NRE from the eager BuildCatalog()), the 3 new
// event keys are known, and every {{token}} in each new event's bodies/subject is a
// declared placeholder (the Phase-2a lesson: an undeclared token renders blank in prod).
// ============================================================================

using System.Text.RegularExpressions;
using FluentAssertions;
using HRM.Domain.Notifications;

namespace HRM.Tests.Unit;

public sealed class WorkflowNotificationCatalogTests
{
    private static readonly string[] WorkflowEventKeys =
    [
        "workflow_step_assigned", "workflow_step_escalated", "workflow_request_decided",
    ];

    [Fact]
    public void Catalog_TypeInitializes_AndAllContainsEntries()
    {
        // Touching All forces the static BuildCatalog() type-init; an NRE here means a placeholder array was
        // declared AFTER _byKey (the Phase-2a lesson). It must not throw.
        NotificationEventCatalog.All.Should().NotBeEmpty();
    }

    [Fact]
    public void WorkflowEvents_AreKnown()
    {
        foreach (var key in WorkflowEventKeys)
            NotificationEventCatalog.IsKnownEvent(key).Should().BeTrue($"'{key}' must be a known catalog event");
    }

    [Fact]
    public void WorkflowEvents_EveryTokenIsADeclaredPlaceholder()
    {
        foreach (var key in WorkflowEventKeys)
        {
            var def = NotificationEventCatalog.Get(key);
            def.Should().NotBeNull();

            var declared = def!.Placeholders.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var body in new[] { def.DefaultSubject, def.DefaultBodyHtml, def.DefaultBodyText })
                foreach (Match m in Regex.Matches(body, @"\{\{\s*([^}]+?)\s*\}\}"))
                {
                    var token = m.Groups[1].Value.Trim();
                    declared.Should().Contain(token,
                        $"token '{{{{{token}}}}}' in event '{key}' must be a declared placeholder");
                }
        }
    }

    [Fact]
    public void DecidedEvent_DeclaresDecisionPlaceholder()
    {
        var def = NotificationEventCatalog.Get("workflow_request_decided");
        def!.Placeholders.Should().Contain("workflow.decision");
    }
}
