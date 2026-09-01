// ============================================================================
// D1 / GAP-S1 — no NEW unchecked wire assertion may enter the Angular app.
//
// `http.get<IFoo>(...)` is an unchecked cast: TypeScript accepts whatever the server sends and calls it an
// IFoo. When the two disagree the field is silently `undefined`, which is how nine of thirteen modules
// shipped against shapes the backend could not emit — and how BUG-311, the teamRanking mapper, the
// self-assessment throw and the recommendation workspace's permanently-empty table all happened.
//
// The fix for an EXISTING one is to consume the generated contract type (`Schema<'…Dto'>`) with an explicit
// mapper. There are 167 left, and migrating them is the long tail of D1. This guard is the other half: it
// stops the count going UP while that work proceeds. Without it D1 never finishes — measured across two
// commits, 99 call sites were migrated while the hand-written interface count moved by three, because new
// ones kept arriving.
//
// WHY THE COUNT AND NOT THE INTERFACES. "662 hand-written interfaces" is the wrong denominator: most are
// view models that never cross the wire and therefore cannot drift. The number that matters is the count of
// UNCHECKED WIRE ASSERTIONS, and it moves as modules migrate — performance sits at 0 here because the
// D-perf slices genuinely finished, while the interface count barely twitched.
//
// This lives on the backend for the same reason as FrontendPermissionLiteralTests: the generated contract is
// produced from the .NET assembly, and a Karma test running in a browser cannot read the Angular sources off
// disk. It runs inside the existing gate rather than needing a new CI step.
// ============================================================================

using System.Text.RegularExpressions;
using FluentAssertions;

namespace HRM.Tests.Unit.Configuration;

public sealed class FrontendWireContractDriftGuardTests
{
    /// <summary>
    /// An HTTP call whose response type is a hand-written <c>I…</c> interface — the unchecked assertion.
    /// Generated types (<c>Schema&lt;'…'&gt;</c>) and wire aliases (<c>…Wire</c>) do not match, which is the
    /// point: those are the migrated shape.
    ///
    /// <para>
    /// <b>Receiver-independent and whitespace-tolerant, both learned the hard way.</b> The first version
    /// required a literal <c>http.</c>, which silently missed <b>67</b> real call sites — not because they
    /// used a different field name, but because they were LINE-WRAPPED
    /// (<c>this.http</c> newline <c>.get&lt;IFoo&gt;(…)</c>). A guard that depends on formatting is a guard
    /// a formatter can switch off. The same receiver-coupling mistake was found in
    /// <c>EmployeeFieldAuditPairingGuardTests</c> and fixed there for the same reason.
    /// </para>
    /// </summary>
    private static readonly Regex UncheckedWireAssertion = new(
        @"\.\s*(?:get|post|put|patch|delete)\s*<\s*I[A-Z][A-Za-z0-9]*",
        RegexOptions.Compiled);

    /// <summary>
    /// The unmigrated call sites, per file. 267 at D1's start; 218 after admin (49 -> 0); 167 after payroll (55 -> 4). Payroll's residual four are NOT unmigrated work — each targets an endpoint that does not exist (BUG-315, BUG-316) or returns no body to map (ISSUE-404), and each is documented at the call site.
    ///
    /// <para><b>This list may only ever SHRINK.</b> A new file appearing here means a fresh unchecked wire
    /// assertion was written; a count going up means one was added to a file that already had them. Either
    /// way the fix is to consume the generated type, not to edit this list upward — that is how a guard
    /// quietly becomes decoration.</para>
    ///
    /// <para>A file whose count reaches zero must be REMOVED from this list, which the staleness arm below
    /// enforces. Otherwise the baseline rots into a permanent exemption that no longer describes anything.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> KnownUnmigrated = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["core/auth/auth.service.ts"] = 23,
        ["features/benefits/services/benefit.service.ts"] = 12,
        ["features/core-hr/custom-fields/services/custom-field.service.ts"] = 6,
        ["features/core-hr/employees/services/bulk-import.service.ts"] = 1,
        ["features/core-hr/employees/services/document.service.ts"] = 1,
        ["features/core-hr/employees/services/employee.service.ts"] = 4,
        ["features/core-hr/job-titles/services/job-title.service.ts"] = 1,
        ["features/core-hr/org-tree/services/org-tree.service.ts"] = 1,
        ["features/dashboard/dashboard.service.ts"] = 1,
        ["features/leave-management/services/carry-forward-preview.service.ts"] = 1,
        ["features/leave-management/services/leave-entitlement.service.ts"] = 1,
        ["features/leave-management/services/leave-reports.service.ts"] = 2,
        ["features/leave-management/services/leave-request.service.ts"] = 1,
        ["features/leave-management/services/team-calendar.service.ts"] = 1,
        ["features/notifications/services/notification-preference.service.ts"] = 4,
        ["features/notifications/services/notification-template.service.ts"] = 6,
        ["features/notifications/services/notification.service.ts"] = 3,
        ["features/onboarding/services/exit-interview.service.ts"] = 4,
        ["features/onboarding/services/onboarding-asset.service.ts"] = 3,
        ["features/onboarding/services/onboarding-checklist.service.ts"] = 7,
        ["features/onboarding/services/onboarding-template.service.ts"] = 6,
        ["features/payroll/services/adjustment.service.ts"] = 2,
        ["features/payroll/services/payroll-run.service.ts"] = 1,
        ["features/payroll/services/payroll.service.ts"] = 1,
        ["features/performance/services/performance-goal.service.ts"] = 3,
        ["features/recruitment/services/careers.service.ts"] = 3,
        ["features/recruitment/services/dashboard.service.ts"] = 1,
        ["features/recruitment/services/interview.service.ts"] = 3,
        ["features/recruitment/services/pipeline.service.ts"] = 1,
        ["features/recruitment/services/scorecard.service.ts"] = 1,
        ["features/recruitment/services/vacancy.service.ts"] = 7,
        ["features/reports/services/reports.service.ts"] = 4,
        ["features/training/services/training.service.ts"] = 10,
    };

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "frontend")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the repo root (containing src/frontend) should be an ancestor of the test assembly");
        return dir!;
    }

    private static Dictionary<string, int> ScanAppSources()
    {
        var appRoot = Path.Combine(RepoRoot().FullName, "src", "frontend", "src", "app");
        Directory.Exists(appRoot).Should().BeTrue($"the Angular sources should be at {appRoot}");

        var generated = Path.Combine("core", "api", "generated");
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(appRoot, "*.ts", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(appRoot, file).Replace(Path.DirectorySeparatorChar, '/');

            // Specs may assert against hand-written shapes deliberately; the generated file is the contract.
            if (relative.EndsWith(".spec.ts", StringComparison.Ordinal)
                || relative.Contains(generated.Replace(Path.DirectorySeparatorChar, '/'), StringComparison.Ordinal))
            {
                continue;
            }

            var matches = UncheckedWireAssertion.Matches(StripComments(File.ReadAllText(file))).Count;
            if (matches > 0)
            {
                counts[relative] = matches;
            }
        }

        return counts;
    }

    /// <summary>
    /// Removes line and block comments before matching.
    ///
    /// <para>
    /// Found while doing the admin migration: every migrated file gains a doc-comment EXPLAINING the old
    /// <c>http.get&lt;IFoo&gt;</c> pattern it replaced, and the guard counted those as violations. So finishing a
    /// module made its count go UP unless the explanation was deleted — a guard that punishes documenting the
    /// very defect it exists to prevent. It would also have let someone trip the build by quoting the pattern
    /// in a comment.
    /// </para>
    ///
    /// <para>
    /// Deliberately naive: it does not understand strings containing <c>//</c>. That is safe here because the
    /// pattern being matched is a type argument, which cannot appear inside a URL or message literal in a way
    /// this would hide — and the alternative is a TypeScript parser for a text scan.
    /// </para>
    /// </summary>
    private static string StripComments(string source)
    {
        var withoutBlocks = Regex.Replace(source, @"/\*[\s\S]*?\*/", string.Empty);
        return Regex.Replace(withoutBlocks, @"//[^\n]*", string.Empty);
    }

    /// <summary>
    /// THE GUARD. No file outside the baseline may contain an unchecked wire assertion, and no baselined file
    /// may contain more than it did.
    /// </summary>
    [Fact]
    public void No_new_unchecked_wire_assertion_enters_the_app()
    {
        var actual = ScanAppSources();

        var newFiles = actual.Keys.Where(f => !KnownUnmigrated.ContainsKey(f)).OrderBy(f => f, StringComparer.Ordinal).ToList();
        newFiles.Should().BeEmpty(
            "`http.get<IFoo>` is an unchecked cast — TypeScript accepts whatever the server sends and calls it "
            + "an IFoo, so a mismatch is a silent `undefined` rather than an error. Consume the generated "
            + "contract type instead: `Schema<'TheDto'>` plus an explicit mapper. New offenders: {0}",
            string.Join(", ", newFiles));

        var grown = actual
            .Where(kv => KnownUnmigrated.TryGetValue(kv.Key, out var allowed) && kv.Value > allowed)
            .Select(kv => $"{kv.Key} ({KnownUnmigrated[kv.Key]} -> {kv.Value})")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        grown.Should().BeEmpty(
            "a baselined file gained unchecked wire assertions; the list may only shrink. Grown: {0}",
            string.Join(", ", grown));
    }

    /// <summary>
    /// STALENESS. A file that has been fully migrated must leave the baseline, so the list describes reality
    /// rather than becoming a permanent exemption nobody rereads.
    /// </summary>
    [Fact]
    public void The_baseline_lists_no_file_that_is_already_clean()
    {
        var actual = ScanAppSources();

        var stale = KnownUnmigrated.Keys
            .Where(f => !actual.ContainsKey(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        stale.Should().BeEmpty(
            "these files no longer contain an unchecked wire assertion — remove them from KnownUnmigrated so "
            + "the baseline keeps meaning something. Stale: {0}",
            string.Join(", ", stale));
    }

    /// <summary>
    /// POSITIVE GUARDIAN. The two arms above are assertions of ABSENCE, and both pass perfectly against an
    /// empty scan — a broken path, a changed layout, a regex that matches nothing. This pins that the scan
    /// genuinely read the app and that the migrated shape is genuinely present.
    /// </summary>
    [Fact]
    public void The_scan_actually_reads_the_angular_app()
    {
        var appRoot = Path.Combine(RepoRoot().FullName, "src", "frontend", "src", "app");
        var sources = Directory.EnumerateFiles(appRoot, "*.ts", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".spec.ts", StringComparison.Ordinal))
            .ToList();

        // 450 non-spec .ts files at the time of writing. The bound is deliberately well below that: its job
        // is to catch a BROKEN PATH (which yields ~0), not to track the app's size — a threshold that trips
        // whenever files are deleted is a guard people learn to edit rather than read.
        sources.Should().HaveCountGreaterThan(300, "a tiny count means the scan is looking in the wrong place");

        var migrated = sources.Count(f => File.ReadAllText(f).Contains("Schema<", StringComparison.Ordinal));
        migrated.Should().BeGreaterThan(20,
            "the migrated shape must be present, or this guard is protecting a migration that never started");
    }
}
