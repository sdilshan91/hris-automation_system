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
            if (ids.Count > 0 && ids.TrueForAll(id => status[id] == "RESOLVED")
                && !StoriesContradictingResolvedFindings_2026_09_01.Contains(story))
            {
                contradictions.Add($"{story} ({string.Join(",", ids)})");
            }
        }

        string.Join(", ", contradictions).Should().BeEmpty(
            "these stories are marked [!] tested-with-findings, but every finding they name is RESOLVED. "
            + "Re-test and flip them to [x], or the ledger keeps reporting closed work as open.");
    }

    /// <summary>
    /// Stories marked [!] tested-with-findings in TEST-STATUS.md whose every named finding is RESOLVED —
    /// the pessimistic contradiction, which makes finished work look outstanding.
    ///
    /// Recorded 2026-09-01, when normalising the findings ledger made its statuses machine-readable for
    /// the first time. These nine were ALWAYS contradictory; they were invisible because their status
    /// lines did not parse, so the guard above silently skipped them. Clearing one means re-running its
    /// TCs via /verify-fix and flipping the row to [x] — NOT editing the row to make a test go green.
    /// The list may only shrink; TheContradictionBaseline_HasNoStaleEntries enforces that.
    /// </summary>
    private static readonly HashSet<string> StoriesContradictingResolvedFindings_2026_09_01 =
        new(StringComparer.Ordinal)
        {
            "US-ADM-002", "US-ADM-003", "US-ADM-004", "US-ADM-005",
            "US-AUTH-001", "US-AUTH-003", "US-AUTH-004", "US-AUTH-012", "US-AUTH-016",
        };

    [Fact]
    public void TheContradictionBaseline_HasNoStaleEntries()
    {
        var status = FindingStatuses();
        var stillContradicting = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (story, segment, marker) in StoryRows())
        {
            if (marker != '!') continue;
            var ids = FindingIdPattern.Matches(segment).Select(m => m.Value).Where(status.ContainsKey).ToList();
            if (ids.Count > 0 && ids.TrueForAll(id => status[id] == "RESOLVED")) stillContradicting.Add(story);
        }

        var stale = StoriesContradictingResolvedFindings_2026_09_01
            .Where(s => !stillContradicting.Contains(s))
            .OrderBy(s => s, StringComparer.Ordinal).ToList();

        string.Join(", ", stale).Should().BeEmpty(
            "these baseline entries no longer contradict — the story was re-tested, or a finding re-opened. "
            + "Remove them; the baseline must only shrink, or it becomes a place real regressions hide.");
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

    /// <summary>
    /// The split's two structural invariants. Both were chosen because violating either silently
    /// corrupts the ledger rather than failing loudly:
    ///
    ///  1. **No ID may appear in both files.** A finding's history must live in one place — a
    ///     [[TEST-FINDINGS#BUG-292]] anchor has to resolve unambiguously, and a live extension hiding
    ///     behind an archived parent is exactly the "wrong in both directions" failure the ledger rule
    ///     already warns about. Systemic findings (BUG-003) carry sub-entries, so the whole ID family
    ///     moves together or not at all.
    ///  2. **No live finding may sit in the archive.** OPEN/DEFERRED work in an append-only archive is
    ///     work nobody will look at again. The reverse (a resolved entry lingering in the working file)
    ///     is harmless and deliberately NOT asserted — the split errs toward visibility.
    /// </summary>
    [Fact]
    public void TheSplitLedger_KeepsEachIdInOneFile_AndNoLiveFindingInTheArchive()
    {
        var archivePath = Path.Combine(RepoRoot(), "docs", "QA", "TEST-FINDINGS-RESOLVED.md");
        if (!File.Exists(archivePath)) return;   // split not in place; nothing to guard

        static HashSet<string> Ids(string text) =>
            Regex.Matches(text, @"^### ((?:BUG|ISSUE|ENH|GAP)-\d+)", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        var working = Read("docs", "QA", "TEST-FINDINGS.md");
        var archive = File.ReadAllText(archivePath);

        var spanning = Ids(working).Intersect(Ids(archive)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        spanning.Should().BeEmpty(
            "a finding's entries must all live in one file — an anchor pointing at an id in both is "
            + "ambiguous, and a live extension behind an archived parent is invisible work. Move the "
            + "whole id family. Spanning: {0}", string.Join(", ", spanning));

        var live = new[] { "OPEN", "DEFERRED" };
        var stranded = Regex.Split(archive, @"\n### ")
            .Select(b => (id: Regex.Match(b, @"^((?:BUG|ISSUE|ENH|GAP)-\d+)"),
                          st: Regex.Match(b, @"\*\*Type / Severity / Status:\*\*[^\n]*?·[^\n]*?·\s*\**`?([A-Z]+)")))
            .Where(x => x.id.Success && x.st.Success && live.Contains(x.st.Groups[1].Value))
            .Select(x => $"{x.id.Groups[1].Value}={x.st.Groups[1].Value}")
            .ToList();

        stranded.Should().BeEmpty(
            "TEST-FINDINGS-RESOLVED.md is an append-only archive of terminal findings; a live one filed "
            + "there is work the team will never see again. Move it back to TEST-FINDINGS.md. Stranded: {0}",
            string.Join(", ", stranded));
    }

    /// <summary>
    /// <summary>
    /// The Summary table at the head of TEST-FINDINGS.md must match the ledgers it summarises.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It did not. On 2026-09-04 the hand-written table read <b>169 total while 218 findings existed</b> — stale
    /// by 49, in the index a reader sees FIRST, with nothing checking it. Every other guard in this file protects
    /// the ledger's contents; none protected its front page, so the one number a human is most likely to quote
    /// was the one number nobody verified.
    /// </para>
    /// <para>
    /// This is the <c>ISSUE-437</c> shape — "nothing verifies a documented claim" — applied to the ledger itself,
    /// and it is why the counts are now ASSERTED rather than maintained. A stale summary is not cosmetic: it
    /// under-reports the backlog, and this repo has already measured 29% stale-pessimistic drift once.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSummaryTable_MatchesTheActualCounts()
    {
        var live = Read("docs", "QA", "TEST-FINDINGS.md");
        var entry = new Regex(@"^### (BUG|ISSUE|ENH|DECISION)-\d+", RegexOptions.Multiline);

        var liveCounts = entry.Matches(live).GroupBy(m => m.Groups[1].Value)
                              .ToDictionary(g => g.Key, g => g.Count());

        var archivePath = Path.Combine(RepoRoot(), "docs", "QA", "TEST-FINDINGS-RESOLVED.md");
        var archiveCounts = File.Exists(archivePath)
            ? entry.Matches(File.ReadAllText(archivePath)).GroupBy(m => m.Groups[1].Value)
                   .ToDictionary(g => g.Key, g => g.Count())
            : new Dictionary<string, int>();

        // Parse the table's own rows: | TYPE | live | archived | total |
        var row = new Regex(@"^\|\s*\**(BUG|ISSUE|ENH|DECISION)\**\s*\|\s*\**(\d+)\**\s*\|\s*\**(\d+)\**\s*\|",
                            RegexOptions.Multiline);
        var declared = row.Matches(live)
            .ToDictionary(m => m.Groups[1].Value,
                          m => (Live: int.Parse(m.Groups[2].Value), Archived: int.Parse(m.Groups[3].Value)));

        declared.Should().NotBeEmpty(
            "TEST-FINDINGS.md must carry a Summary table with one row per finding type — without it there is no "
            + "front-page count for this guard to hold honest");

        foreach (var kind in new[] { "BUG", "ISSUE", "ENH", "DECISION" })
        {
            var actualLive = liveCounts.GetValueOrDefault(kind, 0);
            var actualArchived = archiveCounts.GetValueOrDefault(kind, 0);
            declared.Should().ContainKey(kind);
            declared[kind].Live.Should().Be(actualLive,
                "the Summary row for {0} must match the {1} live '### {0}-NNN' entries actually in "
                + "TEST-FINDINGS.md. Do not hand-edit the table to go green — recount it.", kind, actualLive);
            declared[kind].Archived.Should().Be(actualArchived,
                "the Summary row for {0} must match the {1} archived entries in TEST-FINDINGS-RESOLVED.md",
                kind, actualArchived);
        }
    }

    /// The findings ledger is SPLIT (2026-09-01): live findings in TEST-FINDINGS.md, terminal ones in
    /// TEST-FINDINGS-RESOLVED.md. Every guard here reads the UNION, because a story row may legitimately
    /// blame a finding that has since been archived — resolving it against the working file alone would
    /// report a false "unknown finding id" the moment a fix closes out.
    /// </summary>
    private static string LedgerCorpus()
    {
        var parts = new List<string> { Read("docs", "QA", "TEST-FINDINGS.md") };
        var archive = Path.Combine(RepoRoot(), "docs", "QA", "TEST-FINDINGS-RESOLVED.md");
        if (File.Exists(archive)) parts.Add(File.ReadAllText(archive));
        return string.Join("\n", parts);
    }

    /// <summary>OPEN/DEFERRED (2) &gt; any terminal status (1) &gt; UNKNOWN (0).</summary>
    private static int Rank(string status) => status switch
    {
        "OPEN" or "DEFERRED" or "WIP" => 2,
        "UNKNOWN" => 0,
        _ => 1,
    };

    private static HashSet<string> FindingIds() =>
        Regex.Matches(LedgerCorpus(), @"^### ((?:BUG|ISSUE|ENH)-\d+)", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Status per finding id. A systemic id (BUG-003) has MANY entries — a parent plus
    /// `(EXTENDED to X)` / `NOTE` sub-entries — and they do not all share a status. **Any live entry
    /// makes the whole finding live**: reporting BUG-003 as RESOLVED because its historical
    /// pre-fix entry says so would tell every reader a cross-tenant leak is closed while live
    /// extensions of it are still open. Naive dictionary assignment did exactly that once the ledger
    /// split put the archived parent after the live extensions in the corpus.
    /// </summary>
    private static Dictionary<string, string> FindingStatuses()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var block in Regex.Split(LedgerCorpus(), @"\n### "))
        {
            var id = Regex.Match(block, @"^((?:BUG|ISSUE|ENH)-\d+)");
            if (!id.Success) continue;

            var status = Regex.Match(block, @"\*\*Type / Severity / Status:\*\*[^\n]*?·[^\n]*?·\s*\**`?([A-Z]+)")
                is { Success: true } combined ? combined
                : Regex.Match(block, @"\*\*Status:\*\*\s*\**`?([A-Z]+)");
            var value = status.Success ? status.Groups[1].Value : "UNKNOWN";
            var key = id.Groups[1].Value;
            // live beats terminal beats unknown, regardless of the order entries appear in
            if (!result.TryGetValue(key, out var seen) || Rank(value) > Rank(seen))
            {
                result[key] = value;
            }
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
