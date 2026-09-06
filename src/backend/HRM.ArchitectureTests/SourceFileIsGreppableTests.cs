using FluentAssertions;
using Xunit;

namespace HRM.ArchitectureTests;

/// <summary>
/// BUG-448: a <c>.cs</c> file containing a literal NUL byte is SILENTLY SKIPPED by grep, ripgrep and
/// semgrep. Not warned about — skipped. The file simply does not appear in results, and the search
/// exits 0 as though it had looked.
///
/// <para>This was not hypothetical. <c>AuditAnonymizationService.cs</c> held two literal NULs from
/// <c>"\x00REDACTED\x00"</c>, and <c>grep -c 'IgnoreQueryFilters'</c> on it returned <b>0 with exit 1</b>
/// while the file really contained <b>3</b> — one of them a cross-tenant WRITE over <c>audit_logs</c>,
/// precisely the code <c>.semgrep/tenant-isolation.yml</c> exists to watch. A source file invisible to
/// every text-based audit tool is a hole in every scan that has ever run over this repository, including
/// the ones used to produce this project's own findings.</para>
///
/// <para><b>Why NUL specifically, and not <c>file(1)</c>'s "is it text" verdict.</b> The obvious guard —
/// <c>find … | xargs file | grep -v text</c> — is wrong in both directions here. It reports
/// <c>EncryptingFileStorageTests.cs</c> (which carries <c>0x03</c>/<c>0x04</c> in test data) and two EF
/// migrations that merely have a UTF-8 BOM; grep and ripgrep read all three perfectly well. Measured, not
/// assumed: <c>grep -c Assert</c> on that file returns 2, matching a byte-level count. Blindness tracks
/// the NUL byte, so that is what this rule tests. A guard that cries wolf on three false positives is a
/// guard someone switches off.</para>
///
/// <para>The fix in source is free: <c>"\0"</c> has the identical runtime value to <c>"\x00"</c> and
/// keeps the file valid text.</para>
/// </summary>
public sealed class SourceFileIsGreppableTests
{
    [Fact]
    public void No_backend_source_file_contains_a_literal_NUL_byte()
    {
        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(BackendSource.BackendRoot, "*.cs", SearchOption.AllDirectories))
        {
            var normalised = path.Replace('\\', '/');
            if (normalised.Contains("/obj/") || normalised.Contains("/bin/"))
                continue;

            // Read bytes, never text: a reader that decodes to string is exactly as blind as the tools
            // this rule protects, and would pass while the file stayed invisible.
            var bytes = File.ReadAllBytes(path);
            var nulCount = bytes.Count(b => b == 0);
            if (nulCount > 0)
                offenders.Add($"{Path.GetRelativePath(BackendSource.BackendRoot, path)} ({nulCount} NUL byte(s))");
        }

        offenders.Should().BeEmpty(
            because: "a .cs file containing a literal NUL byte is silently skipped by grep, ripgrep and "
                   + "semgrep — it vanishes from every text-based audit with no warning and no non-zero "
                   + "exit. Replace the \\x00 escape with \\0, which has the identical runtime value and "
                   + "leaves the file readable. Offenders: {0}",
            string.Join(", ", offenders));
    }
}
