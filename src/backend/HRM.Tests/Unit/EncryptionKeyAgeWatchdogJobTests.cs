// ============================================================================
// Key-rotation tooling — the quarterly key-age watchdog (EncryptionKeyAgeWatchdogJob).
//
// Nothing in the key ring records WHEN a key was activated, so the watchdog upserts a first-seen timestamp
// per Encryption:ActiveKeyId into the system-scope encryption_key_activation table and warns once the key's
// age reaches Encryption:RotationCadenceDays (default 90). Driven end-to-end over a real DI graph on an
// InMemory-through-real-EF store with the repo's hand-rolled FakeTimeProvider clock seam, proving:
//   1. first run records first-seen at "now" and reports age 0 / not overdue,
//   2. a later run does NOT advance first-seen (the upsert is first-sight-only) and ages correctly,
//   3. the 90-day boundary: 89 days → not overdue; 90 days → overdue,
//   4. Encryption:RotationCadenceDays overrides the threshold,
//   5. a key FLIP gets its own first-seen row (age restarts) while the retired key's row is retained,
//   6. no configured ActiveKeyId → null status (test-host-only path; the encryptor fail-fasts the real app).
// ============================================================================

using FluentAssertions;
using HRM.Api.Jobs;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Unit;

[Trait("TC", "TC-PLT-006")] // encryption key-age watchdog: first-seen upsert + quarterly-cadence WARN
public sealed class EncryptionKeyAgeWatchdogJobTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task First_run_records_first_seen_now_and_is_not_overdue()
    {
        var provider = BuildProvider(Guid.NewGuid().ToString());

        var status = await RunAt(provider, T0);

        status.Should().NotBeNull();
        status!.KeyId.Should().Be("hrm-field-key-1");
        status.AgeDays.Should().Be(0);
        status.ThresholdDays.Should().Be(90);
        status.RotationOverdue.Should().BeFalse();

        using var scope = provider.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .EncryptionKeyActivations.SingleAsync();
        row.KeyId.Should().Be("hrm-field-key-1");
        row.FirstSeenUtc.Should().Be(T0.UtcDateTime);
    }

    [Fact]
    public async Task Later_run_does_not_advance_first_seen_and_ages_from_the_original_sighting()
    {
        var provider = BuildProvider(Guid.NewGuid().ToString());
        await RunAt(provider, T0);

        var status = await RunAt(provider, T0.AddDays(10));

        status!.AgeDays.Should().Be(10);
        status.RotationOverdue.Should().BeFalse();

        using var scope = provider.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .EncryptionKeyActivations.SingleAsync();
        row.FirstSeenUtc.Should().Be(T0.UtcDateTime, "first-seen is written once, at first sighting only");
    }

    [Fact]
    public async Task Day_89_is_not_overdue_but_day_90_is()
    {
        var provider = BuildProvider(Guid.NewGuid().ToString());
        await RunAt(provider, T0);

        (await RunAt(provider, T0.AddDays(89)))!.RotationOverdue.Should().BeFalse();

        var at90 = await RunAt(provider, T0.AddDays(90));
        at90!.AgeDays.Should().Be(90);
        at90.RotationOverdue.Should().BeTrue("90 days IS the quarterly cadence boundary");
    }

    [Fact]
    public async Task RotationCadenceDays_config_overrides_the_90_day_default()
    {
        var provider = BuildProvider(Guid.NewGuid().ToString(), new Dictionary<string, string?>
        {
            ["Encryption:ActiveKeyId"] = "hrm-field-key-1",
            ["Encryption:RotationCadenceDays"] = "30",
        });
        await RunAt(provider, T0);

        (await RunAt(provider, T0.AddDays(29)))!.RotationOverdue.Should().BeFalse();

        var at30 = await RunAt(provider, T0.AddDays(30));
        at30!.ThresholdDays.Should().Be(30);
        at30.RotationOverdue.Should().BeTrue();
    }

    [Fact]
    public async Task A_key_flip_restarts_the_age_clock_and_retains_the_retired_keys_row()
    {
        var dbName = Guid.NewGuid().ToString();
        var provider1 = BuildProvider(dbName);
        await RunAt(provider1, T0);

        // Ops rotation: ActiveKeyId flipped to key-2 (config change + restart → new provider, same DB).
        var provider2 = BuildProvider(dbName, new Dictionary<string, string?>
        {
            ["Encryption:ActiveKeyId"] = "hrm-field-key-2",
        });
        var status = await RunAt(provider2, T0.AddDays(100));

        status!.KeyId.Should().Be("hrm-field-key-2");
        status.AgeDays.Should().Be(0, "the NEW key's age starts at ITS first sighting, not the old key's");
        status.RotationOverdue.Should().BeFalse();

        using var scope = provider2.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .EncryptionKeyActivations.OrderBy(k => k.FirstSeenUtc).ToListAsync();
        rows.Select(r => r.KeyId).Should().Equal("hrm-field-key-1", "hrm-field-key-2");
    }

    [Fact]
    public async Task No_configured_active_key_returns_null_status()
    {
        var provider = BuildProvider(Guid.NewGuid().ToString(), new Dictionary<string, string?>());

        (await RunAt(provider, T0)).Should().BeNull();
    }

    // ── harness ─────────────────────────────────────────────────────────────

    private static Task<EncryptionKeyAgeStatus?> RunAt(ServiceProvider provider, DateTimeOffset now)
        => new EncryptionKeyAgeWatchdogJob(
            provider.GetRequiredService<IServiceScopeFactory>(), new FakeTimeProvider(now)).RunAsync();

    private static ServiceProvider BuildProvider(string dbName, Dictionary<string, string?>? config = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(config ?? new Dictionary<string, string?>
            {
                ["Encryption:ActiveKeyId"] = "hrm-field-key-1",
            })
            .Build());
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<ITenantContext, TenantContext>();
        return services.BuildServiceProvider();
    }
}
