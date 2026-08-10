// ============================================================================
// GAP-016 — every permission string the Angular app uses must exist in PermissionCatalog.
//
// The FE keeps its own `permission-catalog.ts` — a hand-written mirror of PermissionCatalog.cs, with UI
// labels and grouping the backend has no reason to carry. Two hand-maintained lists of one contract with
// nothing comparing them: the same shape as finding S-2 (the RLS layer had a coverage guard and zero holes;
// the EF filter layer had none and six), and the same shape as S-1 (the FE↔BE DTO drift).
//
// It had drifted. `Admin.Roles.Manage` — guarding the entire roles admin section — existed in no catalog, so
// its route guard could never pass. `ExitInterview.Conduct` likewise, leaving that check permanently false
// and the feature dependent on a role-name fallback. Worse, PERMISSION_CATALOG is what the role editor
// renders as ASSIGNABLE CHECKBOXES, so a tenant admin could grant permissions the backend has never heard
// of: the role saves, the UI shows them ticked, and the user gets nothing.
//
// This test lives on the backend because PermissionCatalog.cs is the source of truth and only a .NET test can
// read it directly. It parses the Angular sources as text — deliberately, since the alternative (generating
// the FE catalog) would discard the UI metadata that is the reason the FE list exists at all.
// ============================================================================

using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using HRM.Domain.Authorization;

namespace HRM.Tests.Unit;

public sealed class FrontendPermissionLiteralTests
{
    /// <summary>
    /// Keys present in the FE catalog with no backend counterpart AS OF 2026-08-08 (17 keys), recorded so the drift is
    /// counted rather than hidden, and so it cannot GROW — a new mismatch fails this test immediately.
    ///
    /// <para>They are NOT fixed here on purpose. Each one needs an authorization decision (is
    /// <c>Admin.View</c> meant to be a real permission, a UI-only grouping, or a rename of something that
    /// exists?), and guessing a mapping for a permission string is exactly the kind of change that silently
    /// grants or removes access. Filed as ISSUE-363; this list must only ever shrink.</para>
    /// </summary>
    private static readonly HashSet<string> KnownDriftIssue363 =
    [
        "Admin.Audit.View", "Admin.Tenant.Configure", "Admin.Users.Manage", "Admin.View",
        "Attendance.Configure", "Attendance.View",
        "Employee.Edit.All", "Employee.Edit.Self", "Employee.View.Self",
        "Leave.Configure", "Leave.View",
        "Performance.Configure", "Performance.Review", "Performance.View",
        "Recruitment.Interview", "Recruitment.Offer",
        "Reports.Create",
    ];

    [Fact]
    public void EveryPermissionLiteralUsedByTheAngularApp_ExistsInPermissionCatalog()
    {
        var catalog = PermissionCatalog.AllPermissions.ToHashSet(StringComparer.Ordinal);
        catalog.Should().NotBeEmpty("the catalog is the source of truth this test compares against");

        var used = CollectFrontendPermissionLiterals();
        used.Should().NotBeEmpty(
            "the scan found no permission literals at all — the Angular sources moved or the patterns below " +
            "stopped matching, which would make this test pass while checking nothing");

        var unknown = used
            .Where(p => !catalog.Contains(p))
            .Where(p => !KnownDriftIssue363.Contains(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        string.Join(", ", unknown).Should().BeEmpty(
            "every permission string the frontend guards on, renders a directive for, or offers as an " +
            "assignable checkbox must exist in PermissionCatalog. A literal that does not exist can never be " +
            "granted, so the guard is permanently closed (a feature nobody can reach) or the checkbox is " +
            "permanently inert (a role that grants nothing). Add the permission to PermissionCatalog and its " +
            "role bundles, or correct the frontend string.");
    }

    [Fact]
    public void TheKnownDriftList_DoesNotContainEntriesThatHaveSinceBeenFixed()
    {
        // Keeps the baseline honest in the other direction: once a key is added to PermissionCatalog (or the
        // FE stops using it), its entry here is stale and must go, or the list slowly becomes a place where
        // real regressions can hide.
        var catalog = PermissionCatalog.AllPermissions.ToHashSet(StringComparer.Ordinal);
        var used = CollectFrontendPermissionLiterals();

        var obsolete = KnownDriftIssue363
            .Where(p => catalog.Contains(p) || !used.Contains(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        string.Join(", ", obsolete).Should().BeEmpty(
            "these entries are no longer drifting — either the permission now exists in PermissionCatalog or " +
            "the frontend no longer uses it. Remove them from KnownDriftIssue363; the list must only shrink.");
    }

    /// <summary>
    /// Every permission string the Angular app depends on: route guards, the structural directive, the
    /// service helpers, and the assignable-permission catalog the role editor renders.
    /// </summary>
    private static HashSet<string> CollectFrontendPermissionLiterals()
    {
        var appRoot = Path.Combine(RepoRoot(), "src", "frontend", "src", "app");
        Directory.Exists(appRoot).Should().BeTrue($"the Angular sources should be at {appRoot}");

        // A permission is Pascal-cased dot-separated segments ("Roles.Manage", "Employee.View.Own"). The
        // patterns below bound the search to the places a permission can actually be used, so ordinary
        // dotted strings (routes, i18n keys, css classes) are not swept up.
        var patterns = new[]
        {
            new Regex(@"permissionGuard\(\s*\[([^\]]*)\]", RegexOptions.Singleline),
            new Regex(@"has(?:Any|All)?Permission\(\s*(\[[^\]]*\]|'[^']*')", RegexOptions.Singleline),
            new Regex(@"appHasPermission\s*=\s*""([^""]*)""", RegexOptions.Singleline),
            new Regex(@"key:\s*'([A-Z][A-Za-z]*(?:\.[A-Za-z]+)+)'", RegexOptions.Singleline),
        };
        var literal = new Regex(@"'([A-Z][A-Za-z]*(?:\.[A-Za-z]+)+)'");

        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(appRoot, "*.ts", SearchOption.AllDirectories))
        {
            // Specs may deliberately reference made-up permissions to prove a guard DENIES.
            if (file.EndsWith(".spec.ts", StringComparison.Ordinal)) continue;

            var text = File.ReadAllText(file);
            foreach (var pattern in patterns)
            {
                foreach (Match m in pattern.Matches(text))
                {
                    var body = m.Groups[1].Value;
                    // The catalog-key pattern captures the bare value; the others capture an argument list.
                    if (!body.Contains('\'') && !body.Contains('"'))
                    {
                        if (literal.IsMatch($"'{body}'")) found.Add(body);
                        continue;
                    }

                    foreach (Match lit in literal.Matches(body)) found.Add(lit.Groups[1].Value);
                    foreach (Match lit in literal.Matches($"'{body}'")) found.Add(lit.Groups[1].Value);
                }
            }
        }

        return found;
    }

    /// <summary>Walks up from the test assembly until the repo root (the directory holding <c>src/</c>).</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "frontend")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the repo root (containing src/frontend) should be an ancestor of the test assembly");
        return dir!.FullName;
    }
}
