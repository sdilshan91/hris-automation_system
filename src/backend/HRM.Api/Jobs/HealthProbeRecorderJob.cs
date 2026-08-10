using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// US-ADM-002 FR-7 / TC-ADM-002-17: records one readiness probe per run into the system-scope
/// <c>health_probe</c> table, and prunes rows past the retention window. This history is the ONLY basis for the
/// dashboard's SLA uptime percentage — before it existed the field was honestly null, because computing uptime
/// with nothing to compute it from would have meant fabricating a figure (which the TC explicitly forbids).
///
/// <para>Runs the SAME "ready" tagged checks the <c>/health/ready</c> endpoint exposes, IN-PROCESS via
/// <see cref="HealthCheckService"/> rather than by self-issuing HTTP. A self-HTTP probe would additionally
/// measure the loopback network and the reverse proxy, and would fail for reasons unrelated to platform
/// readiness — an availability metric should measure the thing it claims to measure.</para>
///
/// <para><b>Degraded counts as NOT healthy.</b> A degraded platform is not meeting its availability promise;
/// counting it as up would be exactly the flattering measurement TC-ADM-002-17 warns against.</para>
///
/// <para>Reads/writes one tiny platform-wide table with no <c>tenant_id</c> — no tenant context, GUC or RLS
/// concerns. Trailing-optional <see cref="TimeProvider"/> is the repo's standard clock seam for tests.</para>
/// </summary>
public sealed class HealthProbeRecorderJob
{
    /// <summary>Config key for how many days of probe history to retain (default 90).</summary>
    public const string RetentionDaysKey = "Monitoring:HealthProbeRetentionDays";
    public const int DefaultRetentionDays = 90;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _time;

    public HealthProbeRecorderJob(IServiceScopeFactory scopeFactory, TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();

        // GAP-024: declare the cross-tenant intent explicitly rather than relying on "no context" happening to
        // behave like system context. `health_probe` is platform-wide with no tenant_id, so nothing leaks today —
        // but an unresolved context is ambiguous by construction: it cannot be told apart from a job that FORGOT
        // to scope itself, and GAP-001 inverts the unresolved default from privileged to restricted, at which
        // point silence here becomes a runtime failure instead of a lucky pass.
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetSystemContext();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var health = scope.ServiceProvider.GetRequiredService<HealthCheckService>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var startedAt = _time.GetUtcNow();
        HealthStatus status;
        try
        {
            var report = await health.CheckHealthAsync(
                r => r.Tags.Contains("ready"), cancellationToken);
            status = report.Status;
        }
        catch (Exception ex)
        {
            // A probe that THROWS is a probe that failed. Swallowing it and recording nothing would silently
            // inflate uptime by dropping exactly the samples that represent an outage.
            Log.Warning(ex, "[HealthProbe] readiness check threw; recording as Unhealthy");
            status = HealthStatus.Unhealthy;
        }

        var finishedAt = _time.GetUtcNow();

        db.HealthProbes.Add(new HealthProbe
        {
            Id = Guid.CreateVersion7(),
            ObservedAtUtc = startedAt.UtcDateTime,
            IsHealthy = status == HealthStatus.Healthy,
            Status = status.ToString(),
            DurationMs = (int)Math.Clamp((finishedAt - startedAt).TotalMilliseconds, 0, int.MaxValue),
        });

        var retentionDays = config.GetValue<int?>(RetentionDaysKey) ?? DefaultRetentionDays;
        if (retentionDays > 0)
        {
            var cutoff = finishedAt.UtcDateTime.AddDays(-retentionDays);
            await db.HealthProbes
                .Where(p => p.ObservedAtUtc < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        Log.Debug("[HealthProbe] recorded {Status} in {DurationMs}ms", status, (finishedAt - startedAt).TotalMilliseconds);
    }
}
