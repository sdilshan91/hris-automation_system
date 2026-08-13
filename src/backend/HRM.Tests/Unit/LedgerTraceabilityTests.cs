// ============================================================================
// The ledger-traceability guard.
//
// The gap analysis found the documentation and the code disagreeing in BOTH directions, and the running
// total kept moving: the register headlines "36 CONTRADICTED", a grep of the pass files returns 51, and
// classifying those 51 gives 23 summary lines, 22 doc-vs-code contradictions, and 6 rows where the LEDGER
// claims an open bug that is actually fixed. Correcting rows by hand is how the drift got here — every fix
// that lands without closing its row adds one, and I added two myself in a single session (a GAP row left
// reading "NOT DONE" after I shipped it, and a memory note calling a resolved bug "the only item accruing
// cost").
//
// So this is the S-2 pattern applied to the ledgers themselves: the thing that has consistently worked in
// this repo is a mechanical guard, not a sweep. Three checks, all measured against the real files before
// being written — two of them hold at ZERO today and are therefore asserted strictly.
//
// A note on scope, because getting it wrong nearly baked in seven false positives: finding IDs are only
// meaningful inside a story row's structured  segment. Scanning the whole file also catches
// narrative prose — "BUG-7 baseline FIXED" is shorthand for an old QA numbering, not a reference to a
// BUG-007 entry — so the scan is deliberately anchored to that segment.
// ============================================================================

using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace HRM.Tests.Unit;

public sealed class LedgerTraceabilityTests
{
    /// <summary>
    /// Stories marked `[x]` in `docs/BA/STATUS.md` that have no row in `docs/QA/TEST-STATUS.md` AS OF
    /// 2026-08-12 (60 of 123). Recorded so the hole is counted rather than hidden, and so it cannot GROW —
    /// a newly-completed story with no test-status row fails immediately.
    ///
    /// <para>This list must only ever SHRINK. "Done" with no test row is the shape that let
    /// `STATUS.md:61` claim US-CHR-013 shipped a frontend that did not exist (GAP-023).</para>
    /// </summary>
    private static readonly HashSet<string> StoriesWithoutTestStatusRow_2026_08_12 =
    [
        "US-ADM-011", "US-ADM-012", "US-ATT-002", "US-ATT-003",
        "US-ATT-004", "US-ATT-005", "US-ATT-007", "US-ATT-008",
        "US-ATT-009", "US-ATT-010", "US-AUTH-005", "US-LV-002",
        "US-LV-003", "US-LV-004", "US-LV-005", "US-LV-006",
        "US-LV-008", "US-LV-009", "US-LV-010", "US-LV-011",
        "US-LV-012", "US-NTF-002", "US-NTF-003", "US-NTF-004",
        "US-NTF-005", "US-NTF-006", "US-ONB-002", "US-ONB-003",
        "US-ONB-004", "US-ONB-005", "US-ONB-006", "US-PAY-002",
        "US-PAY-003", "US-PAY-004", "US-PAY-005", "US-PAY-006",
        "US-PAY-008", "US-PAY-009", "US-PAY-010", "US-PAY-011",
        "US-PAY-012", "US-PLT-003", "US-PLT-004", "US-PLT-006",
        "US-PRF-002", "US-PRF-003", "US-PRF-004", "US-PRF-005",
        "US-PRF-007", "US-PRF-008", "US-PRF-009", "US-PRF-010",
        "US-PRF-011", "US-RPT-002", "US-RPT-003", "US-RPT-004",
        "US-RPT-005", "US-TRN-001", "US-TRN-002", "US-TRN-003",
    ];

    [Fact]
    public void EveryFindingIdListedOnAStoryRow_HasAnEntryInTheFindingsLedger()
    {
        // Holds at zero today. A dangling id means a story is blamed on a finding nobody can read.
        var findings = FindingIds();
        findings.Should().NotBeEmpty("the findings ledger should parse — an empty set would pass vacuously");

        var dangling = new List<string>();
        foreach (var (story, segment) in StoryFindingSegments())
        {
            foreach (Match id in FindingIdPattern.Matches(segment))
            {
                if (!findings.Contains(id.Value))
                {
                    dangling.Add($"{story} -> {id.Value}");
                }
            }
        }

        string.Join(", ", dangling).Should().BeEmpty(
            "every finding id a story row blames must exist as a '### <ID>' entry in TEST-FINDINGS.md. A "
            + "dangling id is untraceable: nobody can tell whether the story is still affected, which is how "
            + "a ledger starts overstating open work.");
    }

    [Fact]
    public void NoStoryIsStillMarkedWithFindings_WhenAllOfThemAreResolved()
    {
        // THE DIRECT LEDGER-CONTRADICTION CHECK, and the one this guard exists for: TEST-STATUS.md says
        // "tested, has findings" while TEST-FINDINGS.md says every one of those findings is RESOLVED. That is
        // the two ledgers disagreeing in the PESSIMISTIC direction — the one the audit found costs most,
        // because it makes finished work look outstanding and distorts every estimate above it.
        var status = FindingStatuses();

        var contradictions = new List<string>();
        foreach (var (story, segment, marker) in StoryRows())
        {
            if (marker != '!') continue;

            var ids = FindingIdPattern.Matches(segment).Select(m => m.Value)
                .Where(status.ContainsKey).ToList();
            if (ids.Count > 0 && ids.TrueForAll(id => status[id] == "RESOLVED"))
            {
                contradictions.Add($"{story} ({string.Join(",", ids)})");
            }
        }

        string.Join(", ", contradictions).Should().BeEmpty(
            "these stories are marked [!] tested-with-findings, but every finding they name is RESOLVED. "
            + "Re-test and flip them to [x], or the ledger keeps reporting closed work as open.");
    }

    [Fact]
    public void TheNoTestStatusRowBaseline_OnlyEverShrinks()
    {
        var tested = TestStatusStories();
        var undone = DoneStories().Where(s => !tested.Contains(s)).ToList();

        var newlyMissing = undone.Where(s => !StoriesWithoutTestStatusRow_2026_08_12.Contains(s))
            .OrderBy(s => s, StringComparer.Ordinal).ToList();

        string.Join(", ", newlyMissing).Should().BeEmpty(
            "these stories are marked [x] done in STATUS.md but have no row in TEST-STATUS.md, and they are "
            + "not in the recorded baseline — so the traceability hole GREW. A story cannot be 'done' with no "
            + "record of whether anyone tested it; that is exactly how STATUS.md:61 came to claim a shipped "
            + "frontend that did not exist (GAP-023).");
    }

    [Fact]
    public void TheBaseline_HasNoStaleEntries()
    {
        // Keeps it honest in the other direction, same as FrontendPermissionLiteralTests: once a story gains a
        // test-status row, its baseline entry is stale and must go, or the list slowly becomes a place real
        // regressions can hide.
        var tested = TestStatusStories();
        var done = DoneStories().ToHashSet(StringComparer.Ordinal);

        var stale = StoriesWithoutTestStatusRow_2026_08_12
            .Where(s => tested.Contains(s) || !done.Contains(s))
            .OrderBy(s => s, StringComparer.Ordinal).ToList();

        string.Join(", ", stale).Should().BeEmpty(
            "these baseline entries no longer describe reality — the story now has a TEST-STATUS row, or is "
            + "no longer marked done. Remove them; the baseline must only shrink.");
    }

    // ── ledger readers ──────────────────────────────────────────────────────

    private static readonly Regex FindingIdPattern = new(@"(?:BUG|ISSUE|ENH)-\d+", RegexOptions.Compiled);

    /// <summary>A story row plus the text after it, and its checkbox marker.</summary>
    private static IEnumerable<(string Story, string Segment, char Marker)> StoryRows()
    {
        var text = Read("docs", "QA", "TEST-STATUS.md");
        foreach (Match m in Regex.Matches(text, @"^- \[([x~ !b])\] (US-[A-Z]+-\d+)([^\n]*)$", RegexOptions.Multiline))
        {
            yield return (m.Groups[2].Value, m.Groups[3].Value, m.Groups[1].Value[0]);
        }
    }

    /// <summary>
    /// Only the structured `findings:` segment of each row — NOT the whole line, and NOT the file's prose.
    /// The trailing `*(...)` commentary and the surrounding narrative both mention ids that are not
    /// structured references.
    /// </summary>
    private static IEnumerable<(string Story, string Segment)> StoryFindingSegments()
    {
        foreach (var (story, segment, _) in StoryRows())
        {
            var match = Regex.Match(segment, @"findings:\s*([^*(\n]*)");
            if (match.Success)
            {
                yield return (story, match.Groups[1].Value);
            }
        }
    }

    private static HashSet<string> FindingIds() =>
        Regex.Matches(Read("docs", "QA", "TEST-FINDINGS.md"), @"^### ((?:BUG|ISSUE|ENH)-\d+)", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>First declared status per finding — the block's own '**Status:**' line.</summary>
    private static Dictionary<string, string> FindingStatuses()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var block in Regex.Split(Read("docs", "QA", "TEST-FINDINGS.md"), @"\n### "))
        {
            var id = Regex.Match(block, @"^((?:BUG|ISSUE|ENH)-\d+)");
            if (!id.Success) continue;

            var status = Regex.Match(block, @"\*\*Status:\*\*\s*\**`?([A-Z]+)");
            result[id.Groups[1].Value] = status.Success ? status.Groups[1].Value : "UNKNOWN";
        }

        return result;
    }

    private static IEnumerable<string> DoneStories() =>
        Regex.Matches(Read("docs", "BA", "STATUS.md"), @"^- \[x\] (US-[A-Z]+-\d+)", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value);

    private static HashSet<string> TestStatusStories() =>
        StoryRows().Select(r => r.Story).ToHashSet(StringComparer.Ordinal);

    private static string Read(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
        File.Exists(path).Should().BeTrue($"the ledger should be at {path}");
        return File.ReadAllText(path);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "docs", "QA")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the repo root (containing docs/QA) should be an ancestor of the test assembly");
        return dir!.FullName;
    }
}
