using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HRM.ArchitectureTests;

/// <summary>
/// Locates the real <c>src/backend</c> tree from the test assembly's output directory and hands out
/// parsed syntax trees for it.
///
/// <para>Why source and not just reflection: the two highest-value rules in this project are about
/// things that are <b>invisible in compiled metadata</b>. A <c>PackageReference</c> that has been
/// declared but not yet used never reaches the assembly reference table, and an optional parameter
/// that no caller supplies is compiled into an indistinguishable constant push at every call site.
/// Both are only visible in the source, so these rules read the source.</para>
/// </summary>
internal static class BackendSource
{
    /// <summary>The four production project directory names, in dependency order.</summary>
    internal static readonly string[] ProductionProjects =
        ["HRM.Domain", "HRM.Application", "HRM.Infrastructure", "HRM.Api"];

    private static readonly Lazy<string> BackendRootLazy = new(FindBackendRoot);
    private static readonly Lazy<IReadOnlyList<SourceFile>> ProductionSourcesLazy =
        new(() => LoadProductionSources().ToArray());

    /// <summary>Absolute path of <c>src/backend</c> (the directory holding <c>HRM.sln</c>).</summary>
    internal static string BackendRoot => BackendRootLazy.Value;

    /// <summary>
    /// Every <c>.cs</c> file in the four production projects, parsed. Excludes <c>bin</c>/<c>obj</c>
    /// (which contain generated + copied duplicates) and EF migrations (machine-generated, enormous,
    /// and explicitly out of scope for hand-authored architecture rules).
    /// </summary>
    internal static IReadOnlyList<SourceFile> ProductionSources => ProductionSourcesLazy.Value;

    /// <summary>
    /// Every <c>.cs</c> file in <c>HRM.Tests</c>, parsed. Separate from <see cref="ProductionSources"/>
    /// because a rule about test structure must NOT scan production code, and vice versa — a rule whose
    /// scope quietly widens starts reporting failures the reader cannot act on.
    /// </summary>
    internal static IReadOnlyList<SourceFile> TestSources => TestSourcesLazy.Value;

    private static readonly Lazy<IReadOnlyList<SourceFile>> TestSourcesLazy =
        new(() => LoadProject("HRM.Tests").ToArray());

    internal static string ProjectDir(string projectName) => Path.Combine(BackendRoot, projectName);

    internal static string ProjectFile(string projectName) =>
        Path.Combine(BackendRoot, projectName, projectName + ".csproj");

    private static string FindBackendRoot()
    {
        // Walk up from the test binaries (…/HRM.ArchitectureTests/bin/Debug/net10.0) looking for the
        // solution. Anchoring on HRM.sln rather than a hardcoded "../../../.." keeps this working under
        // both `dotnet test` and an IDE runner, whose output layouts differ.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HRM.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate HRM.sln walking up from '{AppContext.BaseDirectory}'. The architecture " +
            "rules read the real backend source tree; they cannot fall back to a copy without silently " +
            "becoming a guard over stale files. Fix the lookup — do not stub this out.");
    }

    private static IEnumerable<SourceFile> LoadProject(string project)
    {
        var projectDir = ProjectDir(project);
        if (!Directory.Exists(projectDir))
            throw new InvalidOperationException(
                $"Project directory '{projectDir}' does not exist. An architecture rule that scans " +
                "nothing passes everything, so this throws rather than silently succeeding.");

        foreach (var path in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories))
        {
            if (IsExcluded(path, projectDir))
                continue;

            var text = File.ReadAllText(path);
            yield return new SourceFile(
                project,
                path,
                Path.GetRelativePath(BackendRoot, path).Replace('\\', '/'),
                CSharpSyntaxTree.ParseText(text, path: path));
        }
    }

    private static IEnumerable<SourceFile> LoadProductionSources()
    {
        foreach (var project in ProductionProjects)
        {
            var projectDir = ProjectDir(project);
            if (!Directory.Exists(projectDir))
                throw new InvalidOperationException(
                    $"Production project directory '{projectDir}' does not exist. If a layer was renamed, " +
                    "update BackendSource.ProductionProjects — an architecture rule that scans nothing passes " +
                    "everything.");

            foreach (var path in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories))
            {
                if (IsExcluded(path, projectDir))
                    continue;

                var text = File.ReadAllText(path);
                yield return new SourceFile(
                    project,
                    path,
                    Path.GetRelativePath(BackendRoot, path).Replace('\\', '/'),
                    CSharpSyntaxTree.ParseText(text, path: path));
            }
        }
    }

    private static bool IsExcluded(string path, string projectDir)
    {
        var relative = Path.GetRelativePath(projectDir, path).Replace('\\', '/');
        return relative.StartsWith("bin/", StringComparison.Ordinal)
            || relative.StartsWith("obj/", StringComparison.Ordinal)
            || relative.Contains("/Migrations/", StringComparison.Ordinal)
            || relative.StartsWith("Migrations/", StringComparison.Ordinal);
    }

    internal sealed record SourceFile(
        string Project,
        string AbsolutePath,
        string RelativePath,
        SyntaxTree SyntaxTree)
    {
        internal SyntaxNode Root => SyntaxTree.GetRoot();

        /// <summary>1-based line number of a node, for evidence in failure messages.</summary>
        internal int LineOf(SyntaxNode node) =>
            SyntaxTree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
    }
}
