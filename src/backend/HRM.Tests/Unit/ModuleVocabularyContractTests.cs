// ============================================================================
// ISSUE-353 — the FE↔BE canonical module-vocabulary contract, closed from the backend side.
//
// The canonical module list exists in THREE places:
//   1. BE  `PlanModules.All`                                    (the source of truth)
//   2. FE  `core/tenant/module.guard.ts` CANONICAL_MODULE_KEYS   (the route guard)
//   3. FE  `features/admin/plans/models/plan.models.ts` CANONICAL_MODULES (the plan editor)
//
// (2) and (3) are already pinned to each other by `module-key-drift.spec.ts`. That spec cannot reach (1)
// — a Karma spec has no access to C# — so the backend copy was the one link with nothing asserting it.
// This test closes it from the side that CAN read both: a .NET test can read the TypeScript file.
//
// WHY THIS MATTERS MORE THAN A TIDINESS ARGUMENT. `isModuleEntitled` FAILS OPEN on any token it does not
// recognize. So if the backend grants a module key the frontend guard has never heard of, the tenant
// holding that key trips the unknown-token branch and the FRONTEND SILENTLY STOPS ENFORCING ENTITLEMENT
// ENTIRELY, while the backend keeps enforcing it. UI and API then disagree about what is enabled, with no
// error anywhere — the symptom presents as "it works". That is exactly the shape of ISSUE-335, where two
// module vocabularies coexisted for months precisely because nothing read the column.
//
// Parsing a source file from a test is unusual, and deliberate: the alternative (a generated shared JSON
// artifact) needs build wiring in two toolchains, and the failure mode of a stale generated file is the
// very drift being guarded against. Reading the real file cannot go stale.
// ============================================================================

using System.Text.RegularExpressions;
using FluentAssertions;
using HRM.Domain.Authorization;

namespace HRM.Tests.Unit;

public sealed class ModuleVocabularyContractTests
{
    // Binds @TC-ADM-353-01.
    [Fact]
    public void Frontend_route_guard_module_keys_exactly_match_PlanModules_All_ISSUE353()
    {
        var guardPath = ResolveRepoFile("src/frontend/src/app/core/tenant/module.guard.ts");
        var feKeys = ExtractCanonicalKeys(File.ReadAllText(guardPath), "CANONICAL_MODULE_KEYS");

        feKeys.Should().NotBeEmpty("if the parse yields nothing the test would vacuously pass and guard nothing");

        // Membership-exact in BOTH directions. A one-directional sweep would pass while one list quietly grew,
        // which is the specific failure this exists to catch.
        feKeys.Should().BeEquivalentTo(PlanModules.All,
            "the frontend route guard and PlanModules.All are the same vocabulary in two languages — if they "
            + "diverge, a key the guard does not recognize makes isModuleEntitled FAIL OPEN and the frontend "
            + "silently stops enforcing entitlement while the backend keeps enforcing it");
    }

    // Binds @TC-ADM-353-02. The plan editor is what an admin actually grants modules from, so a key it can
    // grant but the backend does not recognize is grantable-but-meaningless.
    [Fact]
    public void Frontend_plan_editor_module_keys_exactly_match_PlanModules_All_ISSUE353()
    {
        var modelsPath = ResolveRepoFile("src/frontend/src/app/features/admin/plans/models/plan.models.ts");
        var text = File.ReadAllText(modelsPath);

        // CANONICAL_MODULES is an array of objects: { key: 'CoreHR', label: '…' }. Pull the key values.
        var block = ExtractBlock(text, "CANONICAL_MODULES");
        var feKeys = Regex.Matches(block, @"key:\s*'([^']+)'")
            .Select(m => m.Groups[1].Value)
            .ToList();

        feKeys.Should().NotBeEmpty("a vacuous parse must fail loudly rather than silently guard nothing");
        feKeys.Should().BeEquivalentTo(PlanModules.All,
            "an admin can only grant what the plan editor lists; a key here that the backend does not "
            + "recognize is grantable but meaningless, and one missing here is ungrantable");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks up from the test assembly location to the repo root (identified by the committed HRM.sln beside
    /// src/), then resolves a repo-relative path. Fails with a clear message rather than a bare
    /// FileNotFoundException, since a moved file is itself a contract change worth reading about.
    /// </summary>
    private static string ResolveRepoFile(string repoRelativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "frontend")))
            dir = dir.Parent;

        dir.Should().NotBeNull("the repo root (containing src/frontend) must be locatable from the test output dir");

        var full = Path.Combine(dir!.FullName, repoRelativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(full).Should().BeTrue(
            $"{repoRelativePath} must exist — if it moved, this contract test must be repointed rather than "
            + "left silently passing against a file that is no longer the canonical one");
        return full;
    }

    /// <summary>Extracts the string literals from a named `export const X = [ ... ];` array.</summary>
    private static List<string> ExtractCanonicalKeys(string source, string constName)
    {
        var block = ExtractBlock(source, constName);
        return Regex.Matches(block, @"'([^']+)'").Select(m => m.Groups[1].Value).ToList();
    }

    private static string ExtractBlock(string source, string constName)
    {
        var start = source.IndexOf(constName, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"{constName} must be present — if it was renamed, this contract "
            + "test must follow it rather than pass on an empty parse");

        var open = source.IndexOf('[', start);
        var close = source.IndexOf("];", open, StringComparison.Ordinal);
        open.Should().BeGreaterThan(-1);
        close.Should().BeGreaterThan(open);
        return source[open..close];
    }
}
