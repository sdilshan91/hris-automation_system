using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HRM.ArchitectureTests;

// ============================================================================
// Every permission string an endpoint demands must exist in PermissionCatalog.
//
// THE TRAP THIS CLOSES (ISSUE-290). A `[RequirePermission("...")]` literal that no catalog entry backs is
// not a compile error and not a 500 — it is a permission nobody can hold, so the gate evaluates false for
// EVERY caller and the endpoint is silently unreachable. US-PRF-011 warned about exactly this shape, and
// the calibration build avoided it only because someone remembered to.
//
// WHY IT LIVES HERE. Controllers alias these as `private const string` literals because an attribute
// argument must be a compile-time constant, so they cannot reference PermissionCatalog directly. That
// duplication is unavoidable — which is precisely why it needs a mechanical check rather than care.
// PermissionCatalogTests asserts things ABOUT the catalog; nothing until now compared the catalog to what
// the endpoints actually demand.
//
// Written 2026-09-05 while adding Performance.Calibrate: the comment beside that literal claimed a test
// like this already existed. It did not. Rather than soften the comment to match reality, the test was
// written to match the comment — a claim in a comment is worth nothing unless something enforces it, and
// this repo has recorded nine cases of a comment outliving its code.
// ============================================================================

public sealed class RequirePermissionLiteralTests
{
    [Fact]
    [Trait("Category", "Architecture")]
    public void Every_RequirePermission_literal_exists_in_the_permission_catalog()
    {
        var catalog = CatalogConstants();
        catalog.Should().NotBeEmpty("the catalog must be readable, or this rule silently passes everything");

        var offenders = new List<string>();

        foreach (var file in BackendSource.ProductionSources.Where(f => f.Project == "HRM.Api"))
        {
            // Resolve the file's own `private const string X = "..."` aliases; attributes reference those.
            var aliases = file.Root.DescendantNodes().OfType<FieldDeclarationSyntax>()
                .Where(f => f.Modifiers.Any(m => m.ValueText == "const"))
                .SelectMany(f => f.Declaration.Variables)
                .Where(v => v.Initializer?.Value is LiteralExpressionSyntax)
                .ToDictionary(
                    v => v.Identifier.ValueText,
                    v => ((LiteralExpressionSyntax)v.Initializer!.Value).Token.ValueText);

            foreach (var attr in file.Root.DescendantNodes().OfType<AttributeSyntax>()
                         .Where(a => a.Name.ToString().Contains("RequirePermission", StringComparison.Ordinal)))
            {
                foreach (var arg in attr.ArgumentList?.Arguments ?? default)
                {
                    var value = arg.Expression switch
                    {
                        LiteralExpressionSyntax lit => lit.Token.ValueText,
                        IdentifierNameSyntax id when aliases.TryGetValue(id.Identifier.ValueText, out var a) => a,
                        MemberAccessExpressionSyntax m
                            when aliases.TryGetValue(m.Name.Identifier.ValueText, out var a) => a,
                        _ => null,   // a PermissionCatalog.* reference is already type-safe
                    };
                    if (value is null || catalog.Contains(value))
                        continue;

                    offenders.Add($"{file.RelativePath}:{file.LineOf(attr)} — [RequirePermission(\"{value}\")]");
                }
            }
        }

        offenders.Should().BeEmpty(
            "a permission string no catalog entry backs is a permission NOBODY can hold, so the gate is "
            + "false for every caller and the endpoint is silently unreachable — not a 500, not a compile "
            + "error, just a 403 forever (ISSUE-290). Add it to PermissionCatalog, or fix the typo. "
            + "Offenders:\n{0}",
            string.Join("\n", offenders));
    }

    /// <summary>The rule must be reading real attributes — a scan that matches nothing passes everything.</summary>
    [Fact]
    [Trait("Category", "Architecture")]
    public void The_rule_actually_finds_RequirePermission_attributes()
    {
        var count = BackendSource.ProductionSources
            .Where(f => f.Project == "HRM.Api")
            .SelectMany(f => f.Root.DescendantNodes().OfType<AttributeSyntax>())
            .Count(a => a.Name.ToString().Contains("RequirePermission", StringComparison.Ordinal));

        count.Should().BeGreaterThan(20,
            "HRM.Api is full of permission-gated endpoints; finding almost none means the scan is broken "
            + "and would report success over any amount of drift. Found {0}.", count);
    }

    private static HashSet<string> CatalogConstants()
    {
        var file = BackendSource.ProductionSources
            .FirstOrDefault(f => f.RelativePath.EndsWith("Authorization/PermissionCatalog.cs", StringComparison.Ordinal));
        file.Should().NotBeNull("PermissionCatalog.cs must be locatable for this rule to mean anything");

        return file!.Root.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .Where(f => f.Modifiers.Any(m => m.ValueText == "const"))
            .SelectMany(f => f.Declaration.Variables)
            .Select(v => v.Initializer?.Value)
            .OfType<LiteralExpressionSyntax>()
            .Select(l => l.Token.ValueText)
            .ToHashSet(StringComparer.Ordinal);
    }
}
