// ============================================================================
// Deferred Performance PDF exports — permission-gate guards.
//
// Each PDF export must carry the SAME [RequirePermission] as the JSON/CSV route it sits beside: a PDF export
// that is less protected than the data it renders is a data leak. RequirePermission maps to a single policy
// string "Permission:<comma-joined perms>", so asserting that string names the expected permissions fails the
// instant a permission is dropped from (or the whole attribute removed from) an export action. The per-row
// tenant/visibility scoping is exercised by the *IntegrationTests service arms.
// ============================================================================

using System.Reflection;
using FluentAssertions;
using HRM.Api.Controllers;
using HRM.Infrastructure.Identity;

namespace HRM.Tests.Unit;

public sealed class PerformancePdfExportPermissionTests
{
    private const string ReadSelf = "Performance.Read.Self";
    private const string ReviewTeam = "Performance.Review.Team";
    private const string ReviewAll = "Performance.Review.All";
    private const string PublishAll = "Performance.Publish.All";

    [Fact]
    public void Feedback360_report_export_is_HR_only()
    {
        var policy = Policy(typeof(Feedback360Controller), nameof(Feedback360Controller.GetReport));
        policy.Should().Contain(ReviewAll, "the 360 report PDF renders HR-only results (FR-7)");
    }

    [Fact]
    public void ReviewSignoff_record_export_requires_manager_or_HR()
    {
        var policy = Policy(typeof(ReviewSignoffController), nameof(ReviewSignoffController.Export));
        policy.Should().Contain(ReviewTeam).And.Contain(ReviewAll,
            "the review-record PDF is not less protected than the JSON export (AC-4/FR-6)");
    }

    [Fact]
    public void Pip_export_carries_the_same_visibility_gate_as_Get()
    {
        var policy = Policy(typeof(PipController), nameof(PipController.Export));
        policy.Should().Contain(ReadSelf).And.Contain(ReviewTeam).And.Contain(ReviewAll,
            "the PIP PDF mirrors Get-by-id's visibility gate (FR-8)");
    }

    [Fact]
    public void Recommendation_summary_export_is_HR_only()
    {
        var policy = Policy(typeof(RecommendationController), nameof(RecommendationController.ExportSummary));
        policy.Should().Contain(PublishAll, "the recommendation summary PDF renders HR-only compensation figures (FR-6)");
    }

    private static string Policy(Type controller, string methodName)
    {
        var method = controller.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull($"{controller.Name}.{methodName} must exist");
        var attribute = method!.GetCustomAttributes<RequirePermissionAttribute>(inherit: true).SingleOrDefault();
        attribute.Should().NotBeNull($"{controller.Name}.{methodName} must carry a [RequirePermission]");
        attribute!.Policy.Should().NotBeNullOrEmpty();
        return attribute.Policy!;
    }
}
