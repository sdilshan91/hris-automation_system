// ============================================================================
// BUG-095: report-export storage must not collide two same-type exports that complete in the same
// wall-clock second onto one file (the caller's fileName is only second-granular). The per-export
// reportId GUID makes the on-disk path unique.
// ============================================================================

using FluentAssertions;
using HRM.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRM.Tests.Unit;

public sealed class LocalReportExportStorageTests
{
    private readonly LocalReportExportStorage _storage = new(NullLogger<LocalReportExportStorage>.Instance);

    // second-granularity name with no per-export uniqueness — the exact collision the exporter produces.
    private const string SameSecondFileName = "hr-report-headcount-20260630-090632.csv";

    [Fact]
    [Trait("Bug", "BUG-095")]
    public async Task SaveAsync_SameFileName_DistinctExports_UseDistinctPaths()
    {
        var tenant = Guid.NewGuid();

        var p1 = await _storage.SaveAsync(tenant, Guid.NewGuid(), SameSecondFileName, "text/csv", [1, 2, 3]);
        var p2 = await _storage.SaveAsync(tenant, Guid.NewGuid(), SameSecondFileName, "text/csv", [4, 5, 6]);

        try
        {
            p1.Should().NotBe(p2, "the per-export reportId must make the on-disk path unique (BUG-095)");
            (await File.ReadAllBytesAsync(p1)).Should().Equal(1, 2, 3);
            (await File.ReadAllBytesAsync(p2)).Should().Equal(4, 5, 6); // NOT clobbered by the same-name second write.
        }
        finally
        {
            File.Delete(p1);
            File.Delete(p2);
        }
    }

    [Fact]
    [Trait("Bug", "BUG-095")]
    public async Task SaveAsync_ManyConcurrentSameFileName_AllSucceed()
    {
        var tenant = Guid.NewGuid();

        // The exact BUG-095 race: many same-type exports finishing in one second. With reportId in the path
        // they land on distinct files; pre-fix they raced File.WriteAllBytesAsync on ONE path → IOException.
        var paths = await Task.WhenAll(
            Enumerable.Range(0, 32)
                .Select(_ => _storage.SaveAsync(tenant, Guid.NewGuid(), SameSecondFileName, "text/csv", [7])));

        try
        {
            paths.Should().HaveCount(32).And.OnlyHaveUniqueItems();
        }
        finally
        {
            foreach (var p in paths.Distinct())
                File.Delete(p);
        }
    }
}
