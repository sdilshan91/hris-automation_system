using System.Xml.Linq;
using FluentAssertions;
using NetArchTest.Rules;

namespace HRM.ArchitectureTests;

/// <summary>
/// Layer-direction rules for the Clean Architecture split declared in <c>.claude/rules/backend.md</c>
/// §"Clean Architecture + CQRS" (<c>Api → Application → Domain</c>; <c>Infrastructure → Application</c>).
///
/// <para><b>What is deliberately NOT tested here, and why.</b> Most of the arrows in that diagram are
/// already enforced by the compiler and a test for them would be a tautology that can never fail:</para>
/// <list type="bullet">
///   <item><c>Application ↛ Infrastructure</c> — <c>HRM.Infrastructure</c> references
///         <c>HRM.Application</c>, so the reverse edge is a circular project reference that MSBuild
///         rejects outright. It is unrepresentable, not merely untested.</item>
///   <item><c>Domain ↛ Application</c> — same reason.</item>
/// </list>
/// <para>The rules below are the ones that are <b>not</b> compiler-enforced: each targets a change that
/// compiles cleanly, passes every existing test, and reads as innocuous in review.</para>
/// </summary>
public sealed class LayerDependencyTests
{
    /// <summary>
    /// Namespace roots that mean "persistence or database". <c>HRM.Api</c> references
    /// <c>HRM.Infrastructure</c> (it is the composition root and has to, for
    /// <c>AddInfrastructure</c>), which is exactly why a controller reaching straight into
    /// <c>AppDbContext</c> compiles.
    /// </summary>
    private static readonly string[] PersistenceDependencies =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "HRM.Infrastructure.Persistence",
    ];

    /// <summary>
    /// <b>ARCH-002 — controllers stay thin and never touch persistence directly.</b>
    ///
    /// <para><c>.claude/rules/backend.md</c> specifies "<c>HRM.Api</c> — <b>thin controllers dispatching
    /// via MediatR</b>" and puts EF Core's <c>AppDbContext</c> in <c>HRM.Infrastructure</c>. Nothing
    /// enforces it. Because <c>HRM.Api</c> legitimately project-references <c>HRM.Infrastructure</c>,
    /// injecting <c>AppDbContext</c> into a controller and writing a LINQ query inline builds and runs.
    /// The cost is not stylistic: a query written in a controller bypasses the Application layer's
    /// MediatR pipeline, which is where <c>ValidationBehavior</c> and the tenant-scoping seams live —
    /// so it is also the shortest path to an untenanted query.</para>
    ///
    /// <para>Uses NetArchTest rather than reflection deliberately: this has to see dependencies used
    /// inside <b>method bodies</b> (a local <c>db.Employees.Where(...)</c>), which
    /// <c>Type.GetInterfaces()</c>/<c>GetProperties()</c> cannot observe. NetArchTest reads IL via
    /// Mono.Cecil and can.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "Architecture")]
    public void Api_controllers_do_not_depend_on_persistence()
    {
        var apiAssembly = typeof(HRM.Api.Controllers.EmployeesController).Assembly;

        var controllers = Types.InAssembly(apiAssembly)
            .That().ResideInNamespaceStartingWith("HRM.Api.Controllers");

        // Fail closed. If the namespace is ever renamed, an empty type set would make the rule below
        // pass vacuously — a guard scanning nothing accepts everything.
        controllers.GetTypes().Should().HaveCountGreaterThan(50,
            "HRM.Api.Controllers holds ~80 controllers; a near-empty set means this rule is no longer " +
            "selecting them and is passing vacuously");

        var result = controllers
            .ShouldNot().HaveDependencyOnAny(PersistenceDependencies)
            .GetResult();

        var offenders = result.FailingTypeNames ?? [];

        offenders.Should().BeEmpty(
            "controllers must stay thin and dispatch via MediatR — persistence belongs behind the " +
            "Application layer, whose pipeline is where validation and tenant scoping are applied. " +
            "A controller holding AppDbContext or an EF Core type bypasses both. Move the query into " +
            $"an Application command/query handler. Offending controllers: [{string.Join(", ", offenders)}]");
    }

    /// <summary>
    /// Package/assembly prefixes that would make the Application layer persistence- or transport-aware.
    /// </summary>
    private static readonly string[] ForbiddenApplicationDependencies =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "Microsoft.AspNetCore",
        "Hangfire",
        "StackExchange.Redis",
    ];

    /// <summary>
    /// <b>ARCH-003 — <c>HRM.Application</c> stays persistence- and transport-ignorant.</b>
    ///
    /// <para>The layer is specified as "CQRS handlers, MediatR pipeline behaviors, and
    /// <c>Common/Interfaces</c> abstractions" — its whole contract with the database is an interface
    /// that <c>HRM.Infrastructure</c> implements. Adding
    /// <c>&lt;PackageReference Include="Microsoft.EntityFrameworkCore" /&gt;</c> to
    /// <c>HRM.Application.csproj</c> compiles fine (no cycle — Application does not reference
    /// Infrastructure, it references the ORM directly), and from that point handlers can hold
    /// <c>DbSet</c>s and the abstraction boundary is gone.</para>
    ///
    /// <para>Checked at the csproj level as well as in metadata for the same reason as
    /// <see cref="DomainPurityTests"/>: a declared-but-not-yet-used package never appears in the
    /// compiled assembly's reference table, so metadata alone would not see it arrive.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "Architecture")]
    public void Application_declares_no_persistence_or_web_framework_packages()
    {
        var csprojPath = BackendSource.ProjectFile("HRM.Application");
        File.Exists(csprojPath).Should().BeTrue($"'{csprojPath}' is the file this rule guards");

        var doc = XDocument.Load(csprojPath);

        var declared = doc.Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "FrameworkReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .Where(v => v.Length > 0)
            .ToArray();

        declared.Should().NotBeEmpty(
            "HRM.Application declares several packages (MediatR, FluentValidation, AutoMapper); an empty " +
            "list means this rule is parsing the wrong file and is passing vacuously");

        var offenders = declared.Where(IsForbiddenApplicationDependency).ToArray();

        offenders.Should().BeEmpty(
            "HRM.Application must depend on abstractions, not on the ORM or the web stack — its database " +
            "contract is an interface in Common/Interfaces that HRM.Infrastructure implements. " +
            $"Move this dependency to HRM.Infrastructure. Found: [{string.Join(", ", offenders)}]");
    }

    /// <inheritdoc cref="Application_declares_no_persistence_or_web_framework_packages"/>
    [Fact]
    [Trait("Category", "Architecture")]
    public void Application_assembly_does_not_reference_persistence_or_web_frameworks()
    {
        var applicationAssembly = typeof(HRM.Application.Common.Interfaces.ITenantContext).Assembly;

        var referenced = applicationAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        referenced.Should().NotBeEmpty(
            "a net10.0 assembly always references at least System.Runtime; an empty list means this rule " +
            "is reading the wrong assembly and is passing vacuously");

        var offenders = referenced
            .Where(IsForbiddenApplicationDependency)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty(
            "the compiled HRM.Application assembly must not bind to the ORM or the web stack. " +
            $"Found: [{string.Join(", ", offenders)}]");
    }

    private static bool IsForbiddenApplicationDependency(string name) =>
        ForbiddenApplicationDependencies.Any(
            prefix => name.Equals(prefix, StringComparison.Ordinal)
                   || name.StartsWith(prefix + ".", StringComparison.Ordinal));
}
