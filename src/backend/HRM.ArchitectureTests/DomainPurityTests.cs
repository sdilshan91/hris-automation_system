using System.Xml.Linq;
using FluentAssertions;

namespace HRM.ArchitectureTests;

/// <summary>
/// <b>ARCH-001 — <c>HRM.Domain</c> has no framework dependencies.</b>
///
/// <para>This is a stated invariant with no automated enforcement anywhere else in the repo:
/// <c>CLAUDE.md</c> / <c>.claude/rules/backend.md</c> §"Clean Architecture + CQRS" declares
/// "<c>HRM.Domain</c> — entities, value objects, repository interfaces. <b>No framework deps.</b>"
/// The compiler does not enforce it: adding
/// <c>&lt;PackageReference Include="Microsoft.EntityFrameworkCore" /&gt;</c> to
/// <c>HRM.Domain.csproj</c> is a one-line change that builds clean, passes every existing test, and
/// is easy to wave through in review — after which the domain model is quietly coupled to the ORM
/// and the "pure, fully unit-testable" claim on every calculator in <c>HRM.Domain/Payroll</c> is
/// no longer true.</para>
///
/// <para>Deliberately checked at <b>two</b> levels, because each catches what the other cannot:</para>
/// <list type="bullet">
///   <item><b>Declaration</b> (the csproj) — a package reference that is declared but not yet used by
///         any type never reaches the compiled assembly's reference table, so reflection and
///         NetArchTest are both blind to it. It is still a dependency, and it is the moment the rot
///         starts. This arm is the earliest possible catch.</item>
///   <item><b>Compiled metadata</b> (the assembly) — a dependency could also arrive through a
///         transitive project reference or a <c>FrameworkReference</c> that the csproj arm's element
///         list did not anticipate. This arm is the backstop and is anchored on what actually
///         shipped, not on what was declared.</item>
/// </list>
/// </summary>
public sealed class DomainPurityTests
{
    private const string DomainProject = "HRM.Domain";

    /// <summary>
    /// Base Class Library assembly prefixes. A pure domain layer may use the BCL and nothing else.
    /// Note <c>System.*</c> here is the framework's own set — <c>System.Text.Json</c> is BCL, whereas
    /// <c>Newtonsoft.Json</c>, <c>Microsoft.EntityFrameworkCore</c>, <c>MediatR</c>, <c>Npgsql</c> and
    /// <c>FluentValidation</c> are all outside it and would all be caught.
    /// </summary>
    private static readonly string[] AllowedAssemblyPrefixes =
        ["System", "netstandard", "mscorlib"];

    [Fact]
    [Trait("Category", "Architecture")]
    public void Domain_csproj_declares_no_package_project_or_framework_references()
    {
        var csprojPath = BackendSource.ProjectFile(DomainProject);
        File.Exists(csprojPath).Should().BeTrue($"'{csprojPath}' is the file this rule guards");

        var doc = XDocument.Load(csprojPath);

        // MSBuild project files here carry no XML namespace (SDK-style), but match on local name so a
        // namespaced variant cannot silently make this rule scan nothing.
        var offenders = doc.Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "ProjectReference" or "FrameworkReference")
            .Select(e => $"{e.Name.LocalName} Include=\"{e.Attribute("Include")?.Value ?? "?"}\"")
            .ToArray();

        offenders.Should().BeEmpty(
            $"{DomainProject} is declared framework-free in .claude/rules/backend.md (\"No framework deps.\"). " +
            "It must depend on nothing but the BCL — no NuGet package, no sibling project, no shared framework. " +
            "If a domain rule genuinely needs an external type, the dependency belongs in HRM.Application " +
            "behind an interface that HRM.Domain declares, not in the domain layer itself. " +
            $"Found: [{string.Join(", ", offenders)}]");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Domain_assembly_references_only_the_BCL()
    {
        // Anchored on a real domain type rather than Assembly.Load(string): a typo in a string would
        // throw, but a rename would make a name-based lookup quietly resolve nothing.
        var domainAssembly = typeof(HRM.Domain.Payroll.PayrollOvertimeCalculator).Assembly;

        var referenced = domainAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        // Fail closed: if the domain assembly somehow reports no references at all, the rule is
        // inspecting the wrong thing and would pass vacuously forever.
        referenced.Should().NotBeEmpty(
            "a net10.0 assembly always references at least System.Runtime; an empty list means this rule " +
            "is reading the wrong assembly and is passing vacuously");

        var offenders = referenced
            .Where(name => !AllowedAssemblyPrefixes.Any(
                prefix => name.Equals(prefix, StringComparison.Ordinal)
                       || name.StartsWith(prefix + ".", StringComparison.Ordinal)))
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty(
            $"the compiled {DomainProject} assembly must reference only the BCL. A non-BCL entry here means a " +
            "framework type has been used from inside the domain layer — move it behind an interface owned by " +
            $"the domain and implemented in HRM.Infrastructure. Found: [{string.Join(", ", offenders)}]");
    }
}
