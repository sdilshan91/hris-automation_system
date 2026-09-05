using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HRM.ArchitectureTests;

// ============================================================================
// ISSUE-454 — a shared Postgres fixture silently changes the meaning of a cross-tenant assertion.
//
// WHY THIS RULE EXISTS, AND WHY IT EXISTS *BEFORE* THE ROLLOUT IT GUARDS.
//
// Every `*PostgresTests` class today implements `IAsyncLifetime`, which xUnit runs PER TEST — a fresh
// container and a full 149-migration replay for every single [Fact]. Measured: 398 container starts per
// run across 93 classes, which is most of a ~40-minute backend gate (ISSUE-453). Converting them to
// `IClassFixture<PostgresContainerFixture>` collapses that to 93 starts.
//
// That conversion is safe for TENANT-SCOPED assertions and unsafe for cross-tenant ones. Sharing a
// database means sibling classes' rows are visible to each other, so an arm that counts rows ACROSS
// tenants — which is precisely what `IgnoreQueryFilters()` enables — changes meaning the moment it shares.
//
// This is not hypothetical. E3 slice 1 caught one by reading:
// `CleanupService_ExpiresOverdueCompletedExports` asserted a cross-tenant count, and a sibling seeded its
// own overdue export. On a shared database that count becomes 2, and the "fix" a reader would reach for
// is to SCOPE the assertion — i.e. to weaken the very isolation check it exists to make. It was split
// into its own class instead, assertions byte-identical.
//
// So the guard lands first. A conversion campaign run without it would have no mechanical way to tell a
// safe class from an unsafe one, and the failure mode is a test that still passes while asserting less.
//
// WHY SYNTAX AND NOT grep. The first draft of this rule was a text search, and it produced an immediate
// FALSE POSITIVE: `HrReportExportPostgresTests` contains the string `IgnoreQueryFilters` in a header
// COMMENT explaining that the cleanup arm was moved out for exactly this reason. A grep-based guard would
// have failed on a file documenting its own compliance. This walks the syntax tree, so comments and
// strings are structurally invisible.
// ============================================================================

public sealed class SharedPostgresFixtureIsolationTests
{
    private const string SharedFixture = "PostgresContainerFixture";
    private const string EscapeHatch = "IgnoreQueryFilters";

    /// <summary>
    /// A test class that shares a Postgres database must not use <c>IgnoreQueryFilters()</c>.
    /// </summary>
    [Fact]
    [Trait("Category", "Architecture")]
    public void No_class_sharing_a_postgres_database_bypasses_the_tenant_query_filter()
    {
        var violations = new List<string>();

        foreach (var file in BackendSource.TestSources)
        {
            foreach (var type in file.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!SharesTheFixture(type))
                    continue;

                foreach (var call in InvocationsNamed(type, EscapeHatch))
                {
                    violations.Add(
                        $"{file.RelativePath}:{file.LineOf(call)} — {type.Identifier.ValueText} shares a " +
                        $"database via IClassFixture<{SharedFixture}> and calls {EscapeHatch}()");
                }
            }
        }

        violations.Should().BeEmpty(
            "a class sharing a Postgres database sees its siblings' rows, so an assertion that bypasses "
            + "the tenant query filter no longer measures what it did in isolation. The fix is to SPLIT the "
            + "arm into its own class with its own container (as E3 did for the export-cleanup arm) — NOT "
            + "to scope the assertion, which would weaken the isolation check itself. Violations:\n{0}",
            string.Join("\n", violations));
    }

    /// <summary>
    /// The rule must actually be looking at something. A scan that matches nothing passes everything.
    /// </summary>
    [Fact]
    [Trait("Category", "Architecture")]
    public void The_rule_is_actually_scanning_classes_that_use_the_shared_fixture()
    {
        var sharing = BackendSource.TestSources
            .SelectMany(f => f.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Count(SharesTheFixture);

        sharing.Should().BeGreaterThan(0,
            "if no class uses IClassFixture<{0}>, this rule is inert and would report success over any "
            + "amount of drift. That is the failure mode the whole architecture-test project exists to "
            + "avoid — a guard that reports safety it does not provide. If the fixture was renamed, update "
            + "SharedFixture here.", SharedFixture);
    }

    /// <summary>
    /// Guards the guard: the escape hatch must still exist SOMEWHERE in the test tree.
    /// </summary>
    [Fact]
    [Trait("Category", "Architecture")]
    public void The_escape_hatch_this_rule_looks_for_is_still_in_use_somewhere()
    {
        var uses = BackendSource.TestSources
            .SelectMany(f => f.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .SelectMany(t => InvocationsNamed(t, EscapeHatch))
            .Count();

        uses.Should().BeGreaterThan(0,
            "no test calls {0}() anywhere, which almost certainly means the method was renamed rather "
            + "than that every cross-tenant assertion was deleted. A rule searching for a name nothing "
            + "uses is a rule that can never fire.", EscapeHatch);
    }

    private static bool SharesTheFixture(ClassDeclarationSyntax type) =>
        type.BaseList?.Types.Any(b =>
            b.Type is GenericNameSyntax g
            && g.Identifier.ValueText == "IClassFixture"
            && g.TypeArgumentList.Arguments.Any(a => a.ToString().Contains(SharedFixture, StringComparison.Ordinal)))
        == true;

    // Matches `x.IgnoreQueryFilters()` and a bare `IgnoreQueryFilters()`. Deliberately an INVOCATION
    // check: the identifier appearing in a comment, a string, or a `nameof` is not a use.
    private static IEnumerable<InvocationExpressionSyntax> InvocationsNamed(SyntaxNode scope, string name) =>
        scope.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(i =>
            i.Expression is MemberAccessExpressionSyntax m && m.Name.Identifier.ValueText == name
            || i.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == name);
}
