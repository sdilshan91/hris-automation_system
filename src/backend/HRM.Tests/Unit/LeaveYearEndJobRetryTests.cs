// ============================================================================
// ISSUE-041 (US-LV-008 NFR-4): retry guard for the leave year-end / carry-forward
// -expiry recurring jobs.
//
// The parallel fix wraps each job's PER-TENANT unit of work (inside RunAsync) in a
// Polly WaitAndRetryAsync policy (retryCount 3, exponential backoff) so a transient
// per-tenant failure is retried instead of being caught-and-skipped on the first
// throw. This test proves that retry behaviour end-to-end by driving the PUBLIC
// RunAsync against a real DI scope whose ILeaveCarryForwardService throws on its
// first two invocations and succeeds on the third: post-fix the service is invoked
// 3 times (1 initial + 2 retries), pre-fix it was invoked exactly once (no retry).
//
// This is a genuine behavioural retry test (not a mere attribute/field reflection
// guard): it confirms RunAsync actually EXECUTES through the retry policy. The exact
// max-3 / exponential-backoff timing and the Serilog/persistent-failure paths remain
// covered by TC-LV-160. The shared fault instance is registered as a singleton so its
// invocation counter survives the fresh DI scope each retry attempt creates.
// ============================================================================

using FluentAssertions;
using HRM.Api.Jobs;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveCarryForward.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Unit;

public sealed class LeaveYearEndJobRetryTests
{
    // The fault clears on the 3rd attempt, so a working retry policy (>= 2 retries) yields exactly
    // 3 invocations; pre-fix (no retry) yields 1. Distinguishes "retries on transient failure" cleanly.
    private const int ThrowsBeforeSuccess = 2;
    private const int ExpectedInvocations = ThrowsBeforeSuccess + 1;

    /// <summary>
    /// ISSUE-305 (CAL-8) made this job DATE-DEPENDENT: it only does per-tenant work inside that tenant's
    /// year-end window, so with the real clock it did nothing for 358 days a year and this test reported
    /// 0 invocations instead of 3. The retry contract it guards is unchanged and still asserted verbatim —
    /// the clock is pinned to 1 January (the seeded tenant is calendar) so RunAsync actually reaches the
    /// per-tenant body it is meant to be testing.
    /// </summary>
    [Fact]
    public async Task YearEndJob_RetriesTransientTenantFailure_ISSUE041()
    {
        var (provider, fault) = BuildProviderWithOneActiveTenant();

        var job = new ProcessLeaveYearEndJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeTimeProvider(new DateTimeOffset(2027, 1, 1, 2, 0, 0, TimeSpan.Zero)));
        await job.RunAsync();

        // ProcessYearEndAsync threw twice then succeeded — RunAsync must have retried through the
        // Polly policy (pre-fix it caught the first throw and moved on → exactly 1 call).
        fault.YearEndCalls.Should().Be(ExpectedInvocations,
            "the per-tenant year-end call must be retried on transient failure (ISSUE-041, US-LV-008 NFR-4)");
    }

    [Fact]
    public async Task CarryForwardExpiryJob_RetriesTransientTenantFailure_ISSUE041()
    {
        var (provider, fault) = BuildProviderWithOneActiveTenant();

        var job = new ProcessCarryForwardExpiryJob(provider.GetRequiredService<IServiceScopeFactory>());
        await job.RunAsync();

        fault.ExpiryCalls.Should().Be(ExpectedInvocations,
            "the per-tenant expiry call must be retried on transient failure (ISSUE-041, US-LV-008 NFR-4)");
    }

    // ── Test scaffolding ────────────────────────────────────────────────────

    private static (ServiceProvider Provider, FaultyCarryForwardService Fault) BuildProviderWithOneActiveTenant()
    {
        var dbName = Guid.NewGuid().ToString();
        var fault = new FaultyCarryForwardService(ThrowsBeforeSuccess);

        var services = new ServiceCollection();
        // Concrete TenantContext (the jobs set the tenant via the runner, which uses ITenantContext).
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        // RLS increment 2c: the jobs now wrap their per-tenant body in ITenantJobRunner. Register the real runner
        // + an empty IConfiguration (Rls:Enabled defaults to false → runs the work directly, no tx/GUC).
        services.AddScoped<ITenantJobRunner, TenantJobRunner>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // Singleton so the invocation counter is shared across the fresh scope each retry attempt creates.
        services.AddSingleton<ILeaveCarryForwardService>(fault);

        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Tenants.Add(new Tenant
            {
                Id = Guid.NewGuid(),
                Subdomain = "acme",
                Name = "Acme Corp",
                Status = TenantStatus.Active, // picked up by the job's active/trial tenant scan
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
            });
            db.SaveChanges();
        }

        return (provider, fault);
    }

    /// <summary>
    /// ILeaveCarryForwardService test double that throws a transient exception on its first
    /// <see cref="_throwsBeforeSuccess"/> invocations (per operation) and then succeeds, while counting
    /// calls. Used to observe whether the job retries.
    /// </summary>
    private sealed class FaultyCarryForwardService : ILeaveCarryForwardService
    {
        private readonly int _throwsBeforeSuccess;
        public int YearEndCalls { get; private set; }
        public int ExpiryCalls { get; private set; }

        public FaultyCarryForwardService(int throwsBeforeSuccess) => _throwsBeforeSuccess = throwsBeforeSuccess;

        public Task<Result<int>> ProcessYearEndAsync(int fromYear, CancellationToken cancellationToken = default)
        {
            YearEndCalls++;
            if (YearEndCalls <= _throwsBeforeSuccess)
                throw new InvalidOperationException("transient year-end failure");
            return Task.FromResult(Result<int>.Success(0));
        }

        public Task<Result<int>> ProcessExpiryAsync(DateOnly asOf, CancellationToken cancellationToken = default)
        {
            ExpiryCalls++;
            if (ExpiryCalls <= _throwsBeforeSuccess)
                throw new InvalidOperationException("transient expiry failure");
            return Task.FromResult(Result<int>.Success(0));
        }

        public Task<Result<IReadOnlyList<CarryForwardPreviewDto>>> PreviewYearEndAsync(
            int fromYear, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("not exercised by the retry test");
    }
}
