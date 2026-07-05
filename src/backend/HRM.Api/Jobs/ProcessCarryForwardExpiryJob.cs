using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Retry;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire recurring job that expires carried-forward leave days that remain unconsumed past
/// their expiry date (US-LV-008 FR-3, AC-3, BR-3, BR-4).
///
/// Runs monthly. For each active tenant it sets the tenant context and expires any Active
/// carry-forward tracking row whose expiry date has passed, creating an Expired ledger entry for
/// the FIFO-remaining carried days. Idempotent: a tracking row is moved to a terminal state once
/// processed, so it is never double-expired. Per-tenant work is wrapped in a Polly retry policy
/// (NFR-4: max 3 retries, exponential backoff) so a transient failure for one tenant is retried
/// before the loop logs it and moves on (NFR-2). Mirrors LeaveAccrualJob / HolidayRecurrenceJob.
/// </summary>
public sealed class ProcessCarryForwardExpiryJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    // NFR-4: retry transient per-tenant failures with exponential backoff (2^n s), max 3 attempts.
    // Mirrors the canonical Polly idiom in PayslipDistributionRunner / Program.cs ResilientClient.
    private static readonly AsyncRetryPolicy RetryPolicy = Policy
        .Handle<Exception>()
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
            onRetry: (ex, delay, attempt, _) => Log.Warning(ex,
                "ProcessCarryForwardExpiryJob: transient failure, retry {Attempt}/3 in {DelaySeconds}s",
                attempt, delay.TotalSeconds));

    public ProcessCarryForwardExpiryJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting ProcessCarryForwardExpiryJob");

        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);

        List<Guid> tenantIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantIds = await dbContext.Tenants
                .Where(t => !t.IsDeleted && (t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial))
                .Select(t => t.Id)
                .ToListAsync();
        }

        Log.Information("ProcessCarryForwardExpiryJob: Processing {TenantCount} tenants as of {AsOf}",
            tenantIds.Count, asOf);

        foreach (var tenantId in tenantIds)
        {
            try
            {
                // Retry the per-tenant unit of work (fresh scope each attempt) on transient failure.
                await RetryPolicy.ExecuteAsync(async () =>
                {
                    using var scope = _scopeFactory.CreateScope();

                    var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                    if (tenantContext is Infrastructure.Services.TenantContext mutableContext)
                    {
                        mutableContext.SetTenant(tenantId, $"tenant-{tenantId}", TenantStatus.Active);
                    }

                    var service = scope.ServiceProvider.GetRequiredService<ILeaveCarryForwardService>();
                    var result = await service.ProcessExpiryAsync(asOf);

                    Log.Information(
                        "ProcessCarryForwardExpiryJob: Tenant {TenantId} expired {Count} tracking rows",
                        tenantId, result.IsSuccess ? result.Value : 0);
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    "ProcessCarryForwardExpiryJob: Failed to process tenant {TenantId} after retries", tenantId);
                // Continue with other tenants; don't fail the whole job.
            }
        }

        Log.Information("Completed ProcessCarryForwardExpiryJob");
    }
}
