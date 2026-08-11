// ============================================================================
// GAP-007 — a hosted service that touches the database must declare its tenant scope.
//
// This guard exists because of a bug that got all the way to the running stack while every gate was green:
//
//   GAP-001 (#497) made "no ambient tenant" select the NOBYPASSRLS role. ApiCallCounterFlushService is a
//   BackgroundService — no request, no job, nothing to inherit a tenant from — and it writes usage rows for
//   EVERY tenant that saw traffic. Postgres began answering every 10-second tick with
//   `42501: new row violates row-level security policy for table "tenant_api_usage"`. The service's catch
//   re-buffered and logged at WARNING, so nothing crashed, nothing alerted, and tenant usage metering simply
//   stopped. 886 consecutive warnings on the running container. The backend suite was 5385/5385 green
//   throughout, because no test exercised a CROSS-TENANT WRITER under RLS.
//
// It was found by reading the log, which is not a repeatable control — hence this.
//
// Semgrep cannot cover this: whether a write spans tenants depends on the runtime ambient, not on syntax.
// So it is guarded the same way GAP-024 guarded the Hangfire jobs — a source-level coverage guard over the
// whole population, so the NEXT hosted service cannot quietly omit the declaration. Precedent for
// source-scanning as a coverage guard: FrontendPermissionLiteralTests, BackgroundJobTenantContextTests.
// ============================================================================

using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace HRM.Tests.Unit;

public sealed class HostedServiceTenantScopeTests
{
    /// <summary>
    /// Hosted services that touch the database but need no tenant declaration, each with its reason.
    /// </summary>
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal)
    {
        // Issues `.Take(1)` against a set of tables purely to compile the query plans and prime the pooled
        // connection. It never reads the rows, so RLS returning an empty set costs it nothing — the plan is
        // still compiled and the connection still primed. Declaring system context here would hand a warmup
        // routine the BYPASSRLS role for no benefit.
        ["DashboardWarmupHostedService"] = "query-plan warmup; discards results, so an empty set is fine",
    };

    /// <summary>Any of the three ways a caller may declare its scope.</summary>
    private static readonly Regex DeclaresTenantScope = new(
        @"CrossTenantScope|SetSystemContext\s*\(|RunForTenantAsync\s*\(|\.SetTenant\s*\(",
        RegexOptions.Compiled);

    /// <summary>Signals that the service actually reaches the database.</summary>
    private static readonly Regex TouchesDatabase = new(
        @"AppDbContext|DbContext|ExecuteSql|SaveChanges",
        RegexOptions.Compiled);

    [Fact]
    public void EveryDatabaseTouchingHostedService_DeclaresItsTenantScope()
    {
        var services = HostedServiceSources();
        services.Should().NotBeEmpty(
            "the scan found no hosted services at all — the path moved, which would make this test pass " +
            "while checking nothing");

        var undeclared = services
            .Where(s => TouchesDatabase.IsMatch(s.Text))
            .Where(s => !Exempt.ContainsKey(s.Name))
            .Where(s => !DeclaresTenantScope.IsMatch(s.Text))
            .Select(s => s.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        string.Join(", ", undeclared).Should().BeEmpty(
            "a hosted service runs with NO ambient tenant — no request to inherit one from and no job " +
            "argument carrying one. Since GAP-001 that means the NOBYPASSRLS role, under which a read " +
            "returns nothing and a cross-tenant write is refused with 42501. Declare the intent: " +
            "CrossTenantScope.Enter() for genuinely cross-tenant work (metering, sweeps), or " +
            "ITenantJobRunner.RunForTenantAsync per tenant. If the service genuinely does not care about " +
            "the rows it gets back, add it to Exempt WITH the reason — and note that the failure this " +
            "prevents is SILENT: the flush that motivated this guard logged a warning and dropped data " +
            "while every test stayed green.");
    }

    [Fact]
    public void TheExemptionList_HasNoStaleEntries()
    {
        // Same shrink-only discipline as the sibling guards: an exemption naming a file that no longer
        // exists silently pre-approves a future service that happens to reuse the name.
        var actual = HostedServiceSources().Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        var stale = Exempt.Keys
            .Where(name => !actual.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        string.Join(", ", stale).Should().BeEmpty(
            "these exemptions name hosted services that no longer exist — remove them.");
    }

    private static IReadOnlyList<(string Name, string Text)> HostedServiceSources()
    {
        var root = Path.Combine(RepoRoot(), "src", "backend");
        Directory.Exists(root).Should().BeTrue($"the backend sources should be at {root}");

        return Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}HRM.Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(p => (Name: Path.GetFileNameWithoutExtension(p), Text: File.ReadAllText(p)))
            // A hosted service is one that IMPLEMENTS the contract, not one that merely mentions it (the
            // composition root registers them by name and would otherwise match).
            .Where(s => Regex.IsMatch(s.Text, @":\s*(BackgroundService|IHostedService)\b"))
            .ToList();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "backend")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the repo root (containing src/backend) should be an ancestor of the test assembly");
        return dir!.FullName;
    }
}
