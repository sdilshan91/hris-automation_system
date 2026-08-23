// ============================================================================
// C3 / GAP-025 — every employee field-audit write stays paired with a central audit row.
//
// `Employee` is IAuditExempt, so AuditCaptureInterceptor deliberately skips it and employee changes are
// recorded by hand into `employee_field_audit_logs`. That table is NOT readable from the US-NTF-005 audit
// viewer, and the DECISION (2026-08-23, recorded in docs/vault/decisions/2026-08-23-employee-field-audit-is-forensic.md)
// is that it should stay that way: its snapshots carry masked PII that must not surface in a viewer everyone
// with audit access can read.
//
// The consequence of that decision is this guard. If the forensic table is the only record of a change, the
// change is invisible to compliance — which is exactly what had happened at three call sites, while merely
// VIEWING a profile logged "Employee.ProfileViewed". Each write must therefore be accompanied by a central
// `AuditLogs.Add` in the same method, carrying the action name and resource, with the sensitive values left
// behind in the forensic table.
//
// A STATIC SOURCE SCAN, deliberately — same reasoning as PlanLimitLookupUsageGuardTests. It needs no
// database or container, so it cannot become the slow flaky test people learn to skip, and it catches the
// regression when someone writes the line rather than when an auditor cannot answer a question.
//
// The table has FOUR writers and ZERO production readers. That is the real hazard of a reader-less table:
// nothing notices when a write stops happening. The behavioural arms in EmployeeServiceTests,
// EmployeeStatusServiceTests and ManagerAssignmentAuditWriteTests pin that each writer still fires; this
// guard pins that none of them may ever go unpaired again.
// ============================================================================

using System.Text.RegularExpressions;
using FluentAssertions;

namespace HRM.Tests.Unit.Configuration;

public sealed class EmployeeFieldAuditPairingGuardTests
{
    /// <summary>
    /// Anchored on the ENTITY TYPE, not the receiver.
    ///
    /// <para>
    /// An earlier version keyed on <c>"_dbContext.EmployeeFieldAuditLogs.Add"</c>, which quietly made the
    /// whole guard depend on one field being named <c>_dbContext</c>: a service injecting it as <c>_db</c>
    /// would have been invisible to BOTH arms — no pairing demanded, and the positive guardian would not
    /// notice the new writer either.
    /// </para>
    ///
    /// <para>
    /// The receiver qualifier was there to dodge a substring trap: the obvious token <c>"AuditLogs.Add"</c>
    /// is a SUBSTRING of <c>"EmployeeFieldAuditLogs.Add"</c>, so every field-audit write satisfied the
    /// pairing check by containing itself and the guard could never fail — it passed on the pre-fix code and
    /// on two deliberate un-pairings before mutation testing exposed it. <c>new AuditLog</c> is NOT a
    /// substring of <c>new EmployeeFieldAuditLog</c>, so the type anchor is both trap-free and
    /// receiver-independent.
    /// </para>
    /// </summary>
    private const string FieldAuditWrite = "new EmployeeFieldAuditLog";

    private const string CentralAuditWrite = "new AuditLog";

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

    private static IEnumerable<string> ProductionSources()
        => new[] { "HRM.Api", "HRM.Application", "HRM.Domain", "HRM.Infrastructure" }
            .Select(p => Path.Combine(BackendRoot().FullName, p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));

    /// <summary>
    /// Splits a C# file into class-member regions by MEMBER SIGNATURE, not by brace depth.
    ///
    /// <para>
    /// The first version of this counted braces, and it was decoration: both mutations I ran against it —
    /// removing a real pairing — passed. These services are full of interpolated strings
    /// (<c>$"...{employee.Id}..."</c>), whose braces wreck any naive depth count, so the splitter yielded
    /// nothing and "no unpaired methods found" was vacuously true. A guard that cannot fail is worse than no
    /// guard, because it reads like coverage.
    /// </para>
    ///
    /// <para>
    /// Member signatures at class indentation are unambiguous and immune to string contents, so a region is
    /// simply "from one member signature to the next".
    /// </para>
    /// </summary>
    private static readonly Regex MemberSignature = new(
        @"^\s{4}(?:\[[^\]]*\]\s*)*(?:public|private|protected|internal)\s+[^;=]*\(",
        RegexOptions.Compiled);

    private static IEnumerable<(int StartLine, string Body)> MemberRegions(string source)
    {
        var lines = source.Split('\n');
        var start = -1;
        var buffer = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            if (MemberSignature.IsMatch(lines[i]))
            {
                if (start >= 0 && buffer.Count > 0)
                {
                    yield return (start, string.Join('\n', buffer));
                }

                start = i + 1;
                buffer.Clear();
            }

            if (start >= 0)
            {
                buffer.Add(lines[i]);
            }
        }

        if (start >= 0 && buffer.Count > 0)
        {
            yield return (start, string.Join('\n', buffer));
        }
    }

    /// <summary>
    /// Member NAME from a signature line, so the guard can follow one level of indirection.
    /// </summary>
    private static readonly Regex MemberName = new(
        @"\s(\w+)\s*\(",
        RegexOptions.Compiled);

    private static string? NameOf(string signatureLine)
    {
        var m = MemberName.Match(signatureLine);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// THE GUARD. Any member writing an <c>EmployeeFieldAuditLog</c> must also cause a central
    /// <c>AuditLog</c> write — either inline, or by calling a helper in the same file that does.
    ///
    /// <para>
    /// The indirection matters: <c>ReportingStructureService.AssignManagerAsync</c> is correctly paired via
    /// its private <c>AddManagerAudit</c> helper, and a guard that could not see that would have demanded a
    /// second, duplicate write — pushing the code toward the very copy-paste shape this whole programme is
    /// trying to remove.
    /// </para>
    ///
    /// <para>
    /// This asserts the absence of a pattern, which is normally a weak shape — so the arm below pairs it with
    /// a positive assertion that the writers genuinely still exist. Without that, deleting every field-audit
    /// write in the codebase would satisfy this one perfectly.
    /// </para>
    ///
    /// <para>
    /// <b>Known limit, stated rather than overclaimed.</b> This is per-MEMBER, not per-branch: a method that
    /// writes the central row on one path and not another still passes. Verified by mutation — removing one
    /// of <c>AssignManagerAsync</c>'s two <c>AddManagerAudit</c> calls did not redden it; removing both did.
    /// Catching per-branch gaps needs real flow analysis, which is not worth the fragility here: the class
    /// that actually occurred was three members writing the forensic row and NEVER the central one, and that
    /// is exactly what this catches.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_field_audit_write_is_paired_with_a_central_audit_write()
    {
        var unpaired = new List<string>();

        foreach (var file in ProductionSources())
        {
            var source = File.ReadAllText(file);
            if (!source.Contains(FieldAuditWrite, StringComparison.Ordinal))
            {
                continue;
            }

            var regions = MemberRegions(source).ToList();

            // ATTRIBUTION. An absence-assertion over an empty input set always passes and reads like
            // coverage — which is exactly how the brace-depth version of this splitter shipped: it broke on
            // interpolated strings, yielded zero regions, and reported "nothing unpaired" forever. The
            // positive guardian below does NOT catch that (it reads raw file text, never the splitter), so
            // the splitter has to prove it accounted for every write it is supposed to be checking.
            var attributed = regions.Sum(
                r => r.Body.Split(FieldAuditWrite).Length - 1);
            var actual = source.Split(FieldAuditWrite).Length - 1;
            attributed.Should().Be(actual,
                "every field-audit write in {0} must fall inside a member region; a splitter that loses "
                + "them makes the pairing assertion below vacuously true",
                Path.GetFileName(file));

            // Helpers in this file that perform the central write on a caller's behalf.
            //
            // Restricted to PRIVATE members that do not themselves write a field audit. Without both
            // conditions the set widens to "any member here that happens to write centrally", and a caller
            // is then let off simply for MENTIONING that member's name. Concretely: moving the
            // `Employee.ProfileViewed` write into `LoadProfileAsync` — an ordinary refactor — would let
            // `UpdateProfileAsync` pass forever on the strength of its closing
            // `return await LoadProfileAsync(...)`, blinding the guard on the one site C3 exists to protect.
            var auditHelpers = regions
                .Where(r => r.Body.Contains(CentralAuditWrite, StringComparison.Ordinal)
                    && !r.Body.Contains(FieldAuditWrite, StringComparison.Ordinal)
                    && r.Body.Split('\n')[0].Contains("private", StringComparison.Ordinal))
                .Select(r => NameOf(r.Body.Split('\n')[0]))
                .Where(n => n is not null)
                .Select(n => n!)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var (startLine, body) in regions)
            {
                if (!body.Contains(FieldAuditWrite, StringComparison.Ordinal))
                {
                    continue;
                }

                var writesCentrally = body.Contains(CentralAuditWrite, StringComparison.Ordinal);
                var delegatesToHelper = auditHelpers.Any(
                    h => body.Contains($"{h}(", StringComparison.Ordinal));

                if (!writesCentrally && !delegatesToHelper)
                {
                    unpaired.Add($"{Path.GetFileName(file)} (member starting line {startLine})");
                }
            }
        }

        unpaired.Should().BeEmpty(
            "employee_field_audit_logs is NOT readable from the audit viewer (a deliberate decision — its "
            + "snapshots carry masked PII), so a change recorded ONLY there is invisible to compliance. "
            + "Add an AuditLogs.Add beside the field-audit write, carrying the action and resource but not "
            + "the sensitive values. Unpaired: {0}",
            string.Join("; ", unpaired));
    }

    /// <summary>
    /// THE POSITIVE GUARDIAN. The arm above is satisfied by a codebase that writes no field audits at all, so
    /// this pins that the writers are really there. If a writer is legitimately removed, drop its entry here
    /// deliberately — that is a decision, not a silent deletion.
    /// </summary>
    [Fact]
    public void The_known_field_audit_writers_still_exist()
    {
        var writers = ProductionSources()
            .Where(f => File.ReadAllText(f).Contains(FieldAuditWrite, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        writers.Should().BeEquivalentTo(
            new[] { "EmployeeService.cs", "EmployeeStatusService.cs", "ReportingStructureService.cs" },
            "these are the services that record per-field employee forensics; a writer vanishing silently is "
            + "the failure mode a table with four writers and zero readers is most exposed to");
    }
}
