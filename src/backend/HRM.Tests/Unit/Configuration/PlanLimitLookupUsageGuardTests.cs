// ============================================================================
// BUG-307 / ISSUE-388 — nothing may reintroduce the ambiguous plan-limit lookup.
//
// The bug was never one bad line; it was TEN copies of one line, and the fix is a shared helper. A shared
// helper only stays shared if something notices when the eleventh copy appears — otherwise this converges
// straight back to the S-1 shape it was created to escape. `BulkEmployeeImportService`'s own comment records
// that these paths already drifted once: "three paths, three different answers about one limit."
//
// This is a STATIC SOURCE SCAN, deliberately. It needs no database, no container and no host, so it cannot
// become the slow flaky test people learn to skip — and it catches the regression at the only moment it is
// cheap to catch, which is when someone writes the query rather than when a tenant loses a cap.
//
// It asserts the ABSENCE of a pattern. That is normally a weak shape (see the HSTS lesson in
// SecurityHeadersApiTests), so it is paired with a positive assertion that the shared helper is genuinely in
// use — otherwise deleting every call site would satisfy it.
// ============================================================================

using System.Text.RegularExpressions;
using FluentAssertions;

namespace HRM.Tests.Unit.Configuration;

public sealed class PlanLimitLookupUsageGuardTests
{
    /// <summary>
    /// The exact shape that caused BUG-307: select a single nullable limit column straight off the plan row,
    /// so "no plan" and "plan with a NULL limit" collapse into the same <c>null</c>.
    /// </summary>
    private static readonly Regex AmbiguousLookup = new(
        @"\.Select\(\s*p\s*=>\s*\(long\?\)p\.\w+\s*\)",
        RegexOptions.Compiled);

    private static DirectoryInfo BackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "HRM.Infrastructure")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the guard must be able to locate src/backend from the test binary");
        return dir!;
    }

    /// <summary>
    /// KNOWN, TRACKED offenders — ISSUE-388. These three resolve a plan limit in a method that returns a bare
    /// <c>int</c>/<c>long</c>/<c>void</c>, so "fail closed" cannot mean "return an error"; it has to mean
    /// "return the most restrictive defensible value", and THAT is a per-limit product decision rather than a
    /// mechanical edit. They are listed rather than ignored so the guard can still block the ELEVENTH copy
    /// today instead of waiting for those decisions.
    ///
    /// <para><b>This list must only ever shrink.</b> Adding to it is how a guard quietly becomes decoration —
    /// if a new offender appears, migrate it instead.</para>
    /// </summary>
    private static readonly HashSet<string> KnownUnmigrated = new(StringComparer.Ordinal)
    {
        // EMPTY — all ten call sites are migrated. The list did what it was for: it kept the guard live while
        // three of them waited on a product decision, then shrank to nothing. The staleness arm below is what
        // forced it to shrink rather than quietly becoming a permanent exemption.
    };

    private static IEnumerable<string> ProductionSources()
        => new[] { "HRM.Api", "HRM.Application", "HRM.Domain", "HRM.Infrastructure" }
            .Select(p => Path.Combine(BackendRoot().FullName, p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            // Generated EF migrations are snapshots of past schema, not live query code.
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));

    /// <summary>
    /// THE ARM THAT MATTERS. No production file may resolve a plan limit with the ambiguous projection.
    /// A new call site that copies the old idiom silently reintroduces the fail-open for that limit — no
    /// error, no log, just no cap, exactly as before.
    /// </summary>
    [Fact]
    public void NoProductionFile_ResolvesAPlanLimit_WithTheAmbiguousProjection_BUG307()
    {
        var offenders = ProductionSources()
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                // Only flag it where it is actually a PLAN lookup — the projection shape alone is innocent.
                return text.Contains("SubscriptionPlans") && AmbiguousLookup.IsMatch(text);
            })
            .Select(f => Path.GetFileName(f))
            .Where(f => !KnownUnmigrated.Contains(f))
            .OrderBy(f => f)
            .ToList();

        offenders.Should().BeEmpty(
            "resolving a plan limit by selecting the column alone collapses \"no plan row\" and \"plan row "
            + "with a NULL limit\" into one indistinguishable null, which every call site reads as "
            + "\"unlimited\" — that is BUG-307. Use PlanLimitLookup.ResolveAsync, which preserves the "
            + "distinction, and act on IsConfigurationError. Offending file(s): "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// The positive half. Without this, deleting every plan-limit check in the product would satisfy the arm
    /// above — an absence assertion is green both when the rule holds and when the subject is gone.
    /// </summary>
    [Fact]
    public void TheSharedLookup_IsActuallyUsed_BUG307()
    {
        var users = ProductionSources()
            .Where(f => File.ReadAllText(f).Contains("PlanLimitLookup.ResolveAsync"))
            .Select(Path.GetFileName)
            .ToList();

        users.Should().NotBeEmpty(
            "the absence check above is only meaningful while the shared lookup is genuinely in use — if "
            + "every call site were deleted, both arms would pass while the product silently stopped "
            + "enforcing plan limits at all");
    }

    /// <summary>
    /// The allowlist must not outlive the debt. Every entry has to still BE an offender — a stale entry would
    /// silently permit that file to reintroduce the pattern later, which is the failure mode allowlists have.
    /// </summary>
    [Fact]
    public void TheKnownUnmigratedList_ContainsNoStaleEntries_ISSUE388()
    {
        var stillOffending = ProductionSources()
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return text.Contains("SubscriptionPlans") && AmbiguousLookup.IsMatch(text);
            })
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.Ordinal);

        var stale = KnownUnmigrated.Where(f => !stillOffending.Contains(f)).OrderBy(f => f).ToList();

        stale.Should().BeEmpty(
            "a file that no longer uses the ambiguous projection must be REMOVED from the allowlist, or it "
            + "keeps a standing exemption it no longer needs and could silently reintroduce the pattern. "
            + $"Stale entr(ies): {string.Join(", ", stale)}");
    }
}
