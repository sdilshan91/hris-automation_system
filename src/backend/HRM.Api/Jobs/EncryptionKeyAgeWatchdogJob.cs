using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Weekly Hangfire watchdog for the QUARTERLY field-encryption key-rotation cadence
/// (<c>HRM.Infrastructure/Security/README.md</c>). Nothing in the key ring records WHEN a key was installed,
/// so on each run it upserts a first-seen timestamp for the current <c>Encryption:ActiveKeyId</c> into the
/// system-scope <c>encryption_key_activation</c> table, then WARNs (structured <c>[EncryptionKeyAge]</c> log)
/// once the key's age reaches <c>Encryption:RotationCadenceDays</c> (default 90).
///
/// <para>Reads config + that one tiny platform-wide table ONLY — no tenant data, no tenant context/GUC/RLS
/// concerns (the table has no <c>tenant_id</c> and carries no policy). Registered <c>Cron.Weekly</c> in
/// Program.cs: a weekly CHECK of a quarterly cadence, so an overdue key warns ~13 times before it is 6 months
/// old. Trailing-optional <see cref="TimeProvider"/> = the repo's standard clock seam for tests.</para>
/// </summary>
public sealed class EncryptionKeyAgeWatchdogJob
{
    /// <summary>Config key for the rotation cadence threshold in days (default 90 — quarterly).</summary>
    public const string RotationCadenceDaysKey = "Encryption:RotationCadenceDays";
    public const int DefaultRotationCadenceDays = 90;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _time;

    public EncryptionKeyAgeWatchdogJob(IServiceScopeFactory scopeFactory, TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Runs one check. Returns the evaluated status (stored by Hangfire; asserted by tests) — null only when
    /// no active key id is configured (the encryptor would have fail-fasted the app first, so this is a
    /// test-host-only path).
    /// </summary>
    public async Task<EncryptionKeyAgeStatus?> RunAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var activeKeyId = configuration[$"{FieldEncryptionOptions.SectionName}:ActiveKeyId"];
        if (string.IsNullOrWhiteSpace(activeKeyId))
        {
            Log.Warning("[EncryptionKeyAge] No Encryption:ActiveKeyId configured — nothing to watch.");
            return null;
        }

        var thresholdDays = configuration.GetValue(RotationCadenceDaysKey, DefaultRotationCadenceDays);
        var nowUtc = _time.GetUtcNow().UtcDateTime;

        // First-seen upsert: written once per keyId, the first time this watchdog sees it active. Rows for
        // previously-active (retired) keys are retained as rotation history.
        var activation = await db.EncryptionKeyActivations
            .FirstOrDefaultAsync(k => k.KeyId == activeKeyId);
        if (activation is null)
        {
            activation = new Domain.Entities.EncryptionKeyActivation { KeyId = activeKeyId, FirstSeenUtc = nowUtc };
            db.EncryptionKeyActivations.Add(activation);
            await db.SaveChangesAsync();
            Log.Information(
                "[EncryptionKeyAge] Recorded first sighting of active field-encryption key {KeyId} at {FirstSeenUtc}.",
                activeKeyId, nowUtc);
        }

        var ageDays = (int)(nowUtc - activation.FirstSeenUtc).TotalDays;
        var rotationOverdue = ageDays >= thresholdDays;

        if (rotationOverdue)
        {
            Log.Warning(
                "[EncryptionKeyAge] Active field-encryption key {KeyId} is {AgeDays} day(s) old — at/over the "
                + "{ThresholdDays}-day rotation cadence. Rotate it per HRM.Infrastructure/Security/README.md "
                + "(generate key → add to Encryption:Keys → flip ActiveKeyId → restart → POST "
                + "/api/v1/system/encryption/reencrypt → verify 0 rows on the old key → retire it).",
                activeKeyId, ageDays, thresholdDays);
        }
        else
        {
            Log.Information(
                "[EncryptionKeyAge] Active field-encryption key {KeyId} is {AgeDays} day(s) old "
                + "(rotation cadence {ThresholdDays} days) — OK.",
                activeKeyId, ageDays, thresholdDays);
        }

        return new EncryptionKeyAgeStatus(activeKeyId, ageDays, thresholdDays, rotationOverdue);
    }
}

/// <summary>One watchdog evaluation (Hangfire-stored; asserted by tests).</summary>
public sealed record EncryptionKeyAgeStatus(string KeyId, int AgeDays, int ThresholdDays, bool RotationOverdue);
