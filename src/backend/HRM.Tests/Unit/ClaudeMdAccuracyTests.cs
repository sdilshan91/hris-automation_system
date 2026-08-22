// ============================================================================
// The CLAUDE.md accuracy guard.
//
// CLAUDE.md is auto-loaded into every agent and every sub-agent, and the file itself says its
// instructions OVERRIDE default behaviour. That makes it the highest-leverage document in the repo
// and the worst possible place for a stale fact — a wrong line there is not a typo, it is a wrong
// premise handed to every run before any work starts.
//
// Both of the drifts this file guards were live on 2026-08-22, found by a setup scan:
//
//   1. CLAUDE.md stated "There is currently no backend test project" while HRM.Tests held 575 test
//      files (360 unit / 215 integration) with Testcontainers-backed PostgreSQL. An agent reading
//      that does not look for an existing test to extend, and may report "no coverage available"
//      as a constraint on work it was asked to do.
//
//   2. CLAUDE.md documented `npm run lint` as a working frontend command. It was not: angular.json
//      had no `lint` target and @angular-eslint was never installed, so the command failed. When it
//      was finally wired up it surfaced 187 WCAG violations nobody had seen (ISSUE-389).
//
// This is the same S-2 pattern as LedgerTraceabilityTests: a mechanical guard, not a sweep. The
// ledgers already drifted in both directions (36+ contradictions) precisely because keeping prose
// true by hand does not scale. Prose that an agent will act on has to be executable, so here it is
// asserted against the filesystem it describes.
//
// Scope note, because over-reaching would make this test a nuisance rather than a guard: it checks
// only claims that are MECHANICALLY decidable — does this script exist, does this path exist, does
// this file exist. It deliberately does not try to judge whether a prose description is *accurate*;
// that is a human's job, and a test that guesses would get retired the first time it was wrong.
// ============================================================================

using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace HRM.Tests.Unit;

public sealed class ClaudeMdAccuracyTests
{
    /// <summary>
    /// Every `npm run &lt;script&gt;` named in CLAUDE.md must exist in `src/frontend/package.json`.
    /// This is the check that would have caught the documented-but-absent `npm run lint`.
    /// </summary>
    [Fact]
    public void EveryDocumentedNpmScript_ExistsInPackageJson()
    {
        var documented = Regex
            .Matches(InstructionCorpus(), @"npm run ([a-z0-9:_-]+)", RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        documented.Should().NotBeEmpty("CLAUDE.md documents the frontend commands, so the scan must find some");

        using var doc = JsonDocument.Parse(Read("src", "frontend", "package.json"));
        var scripts = doc.RootElement.GetProperty("scripts")
            .EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = documented.Where(s => !scripts.Contains(s)).ToList();

        missing.Should().BeEmpty(
            "CLAUDE.md tells every agent these commands work, so a command that does not exist sends "
            + "them down a dead end (this is exactly how `npm run lint` stayed broken and unnoticed). "
            + "Either wire the script up in src/frontend/package.json, or stop documenting it. "
            + "Missing: {0}",
            string.Join(", ", missing));
    }

    /// <summary>
    /// Every `scripts/*.sh` referenced by CLAUDE.md must exist. These are the gate scripts
    /// (`run-backend-tests.sh`, `gen-openapi.sh`) that CI and `/implement-all` both depend on.
    /// </summary>
    [Fact]
    public void EveryDocumentedShellScript_ExistsOnDisk()
    {
        var referenced = Regex
            .Matches(InstructionCorpus(), @"scripts/([A-Za-z0-9._-]+\.sh)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var missing = referenced
            .Where(s => !File.Exists(Path.Combine(RepoRoot(), "scripts", s)))
            .ToList();

        missing.Should().BeEmpty(
            "a gate script named in CLAUDE.md but absent from scripts/ means an agent following the "
            + "documented verify sequence silently skips that gate. Missing: {0}",
            string.Join(", ", missing));
    }

    /// <summary>
    /// Every relative markdown link target in CLAUDE.md must resolve. CLAUDE.md links out to the
    /// skills, agents, hooks and ledgers that define how this repo is worked on; a dead link there
    /// points an agent at a protocol it then cannot read.
    /// </summary>
    [Fact]
    public void EveryRelativeMarkdownLink_Resolves()
    {
        var targets = Regex
            .Matches(ClaudeMd(), @"\]\(([^)\s#]+)(?:#[^)]*)?\)")
            .Select(m => m.Groups[1].Value)
            .Where(t => !t.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .Where(t => !t.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        targets.Should().NotBeEmpty("CLAUDE.md links to the skills and ledgers it describes");

        var broken = targets
            .Where(t =>
            {
                var p = Path.Combine(RepoRoot(), t.Replace('/', Path.DirectorySeparatorChar));
                return !File.Exists(p) && !Directory.Exists(p);
            })
            .ToList();

        broken.Should().BeEmpty(
            "CLAUDE.md points agents at these files as the source of truth for how to work in this "
            + "repo; a link that does not resolve is a protocol an agent cannot follow. Broken: {0}",
            string.Join(", ", broken));
    }

    /// <summary>
    /// The backend test project must be documented, and must never again be described as absent.
    /// This is the specific 2026-08-22 drift, pinned so it cannot come back.
    /// </summary>
    [Fact]
    public void BackendTestProject_IsDocumentedAsExisting()
    {
        Directory.Exists(Path.Combine(RepoRoot(), "src", "backend", "HRM.Tests"))
            .Should().BeTrue("this guard assumes HRM.Tests exists; if it was removed, retire the guard deliberately");

        var claudeMd = InstructionCorpus();

        claudeMd.Should().Contain("HRM.Tests",
            "the backend test project is the thing agents must extend rather than re-invent, so "
            + "CLAUDE.md has to name it. It once claimed the project did not exist at all.");

        claudeMd.Should().NotContain("no backend test project",
            "this exact sentence was false for the entire life of HRM.Tests (575 test files) and was "
            + "handed to every agent run as a premise before any work started");
    }

    /// <summary>
    /// `dotnet test` must never be presented as the way to run the backend suite. ISSUE-312: it can
    /// exit 0 on an ABORTED run, so a partial run is indistinguishable from a green suite — which is
    /// how a real regression already hid. Every mention in CLAUDE.md must carry the warning.
    /// </summary>
    [Fact]
    public void RawDotnetTest_IsNeverPresentedAsTheBackendTestCommand()
    {
        var offending = InstructionFiles()
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (line, no: $"{Path.GetFileName(f)}:{i + 1}")))
            .Where(x => x.line.Contains("dotnet test", StringComparison.Ordinal))
            // A line is fine when it names the wrapper or explicitly warns against the raw command.
            .Where(x => !x.line.Contains("run-backend-tests", StringComparison.Ordinal)
                     && !x.line.Contains("never", StringComparison.OrdinalIgnoreCase)
                     && !x.line.Contains("Never", StringComparison.Ordinal)
                     && !x.line.Contains("ISSUE-312", StringComparison.Ordinal))
            .Select(x => $"{x.no}: {x.line.Trim()}")
            .ToList();

        offending.Should().BeEmpty(
            "`dotnet test` exits 0 on an aborted run, so documenting it bare teaches every agent to "
            + "read a partial run as a full pass — the ISSUE-312 failure. Route through "
            + "scripts/run-backend-tests.sh, or annotate the line with the warning. Offending: {0}",
            string.Join(" | ", offending));
    }

    private static string ClaudeMd() => Read("CLAUDE.md");

    /// <summary>
    /// CLAUDE.md is no longer the whole instruction surface. As of 2026-08-23 the module-specific
    /// content lives in path-scoped rules under <c>.claude/rules/</c>, which Claude Code loads when a
    /// matching file enters context. These guards assert against the UNION, so moving a command from
    /// CLAUDE.md into a rule keeps it covered — and so that a command documented ONLY in a rule is
    /// still checked.
    /// </summary>
    private static string InstructionCorpus()
    {
        var parts = new List<string> { ClaudeMd() };
        var rulesDir = Path.Combine(RepoRoot(), ".claude", "rules");
        if (Directory.Exists(rulesDir))
        {
            parts.AddRange(Directory.EnumerateFiles(rulesDir, "*.md", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal)
                .Select(File.ReadAllText));
        }

        return string.Join("\n", parts);
    }

    private static IEnumerable<string> InstructionFiles()
    {
        yield return Path.Combine(RepoRoot(), "CLAUDE.md");
        var rulesDir = Path.Combine(RepoRoot(), ".claude", "rules");
        if (!Directory.Exists(rulesDir)) yield break;
        foreach (var f in Directory.EnumerateFiles(rulesDir, "*.md", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            yield return f;
        }
    }

    private static string Read(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
        File.Exists(path).Should().BeTrue($"the file should be at {path}");
        return File.ReadAllText(path);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the repo root (containing CLAUDE.md) should be an ancestor of the test assembly");
        return dir!.FullName;
    }
}
