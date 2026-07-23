using System.Security.Cryptography;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Security;

/// <summary>Outcome of re-encrypting ONE stored value (see <see cref="FieldEncryptionMaintenanceService.TryReencrypt"/>).</summary>
public enum FieldReencryptOutcome
{
    /// <summary>Not in the <c>enc:v1:</c> stored format (legacy plaintext) — the startup back-fill's job, not rotation's.</summary>
    NotEncrypted,

    /// <summary>Already under the active key — nothing to do (the scan predicate normally excludes these).</summary>
    AlreadyActiveKey,

    /// <summary>Decrypted with the ring and re-encrypted under the active key.</summary>
    Reencrypted,

    /// <summary>Corrupt/malformed, or its embedded keyId is missing from the ring — skipped, never overwritten.</summary>
    Undecryptable,
}

/// <summary>
/// The key-rotation maintenance sweep + report over the <see cref="EncryptedFieldRegistry"/> columns — the
/// previously-missing "step 4" of the rotation runbook (<c>Security/README.md</c>): the startup back-fill only
/// encrypts PLAINTEXT, so values written under a retired key stayed there forever and the old key could never
/// be safely removed from the ring.
///
/// <para><b>Tenant scoping / RLS (the DF-50/ISSUE-268 class):</b> mirrors the <c>AutoClockOutJob</c> cross-tenant
/// sweep pattern — enumerate tenants from the RLS-exempt <c>tenants</c> table, then run each tenant's scan inside
/// <c>ITenantJobRunner.RunForTenantAsync</c> (fresh DI scope per tenant) so the <c>app.current_tenant</c> GUC is
/// set under RLS-ON. Every scan/update additionally carries an explicit <c>tenant_id = @tid</c> predicate, so with
/// RLS OFF (the committed default) each row is still processed exactly once. UNLIKE the business jobs, the sweep
/// covers ALL non-deleted tenants regardless of status: a Suspended/Terminated tenant's rows still hold ciphertext
/// under the old key, and skipping them would let the verification report read 0 while retiring the key destroys
/// their data on reactivation.</para>
///
/// <para><b>Raw SQL, deliberately:</b> the EF value converters are transparent (they decrypt on read), so the
/// embedded keyId is only visible on the RAW stored value — and a converter round-trip write would be a no-op to
/// the change tracker anyway. Table/column names come from the constant registry (never external input — no
/// injection surface; built by concatenation so the EF1002 analyzer isn't tripped); every VALUE is parameterized.
/// Batched keyset pagination (<see cref="BatchSize"/> rows by <c>id</c>) keeps memory bounded and lets the cursor
/// advance PAST an undecryptable row instead of re-selecting it forever.</para>
///
/// <para>Registered as a singleton (holds only the scope factory, the singleton encryptor, config values and a
/// logger); resolves per-tenant scoped services (<c>AppDbContext</c>, <c>ITenantJobRunner</c>) from a fresh scope
/// per tenant. Relational-only: on the InMemory provider there is no raw stored form, so both operations return
/// empty results.</para>
/// </summary>
public sealed class FieldEncryptionMaintenanceService : IFieldEncryptionMaintenanceService
{
    /// <summary>Pseudo keyId under which unencrypted (legacy plaintext) residue is counted in the report.</summary>
    public const string PlaintextKeyId = "(plaintext)";

    private const int BatchSize = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFieldEncryptor _encryptor;
    private readonly ILogger<FieldEncryptionMaintenanceService> _logger;
    private readonly TimeProvider _time;
    private readonly string _activeKeyId;

    public FieldEncryptionMaintenanceService(
        IServiceScopeFactory scopeFactory,
        IFieldEncryptor encryptor,
        IConfiguration configuration,
        ILogger<FieldEncryptionMaintenanceService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _encryptor = encryptor;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System; // repo pattern: trailing-optional clock seam
        // Read once: the active key is fixed for the process lifetime (the rotation runbook flips ActiveKeyId
        // via config + RESTART, so a singleton snapshot can never be stale within a run).
        _activeKeyId = configuration
            .GetSection(FieldEncryptionOptions.SectionName).Get<FieldEncryptionOptions>()?.ActiveKeyId
            ?? string.Empty;
    }

    /// <summary>
    /// Re-encrypts ONE stored value under <paramref name="activeKeyId"/>. Pure per-value core of the sweep,
    /// exposed for unit tests. Never throws for a bad value — a corrupt/unknown-key value reports
    /// <see cref="FieldReencryptOutcome.Undecryptable"/> (and <paramref name="reencrypted"/> stays null) so the
    /// caller skips it rather than crashing the sweep or writing garbage over the original ciphertext.
    /// </summary>
    public static FieldReencryptOutcome TryReencrypt(
        string stored, string activeKeyId, IFieldEncryptor encryptor, out string? reencrypted)
    {
        reencrypted = null;

        if (!stored.StartsWith(AesGcmFieldEncryptor.Prefix, StringComparison.Ordinal))
            return FieldReencryptOutcome.NotEncrypted;

        // enc:v1:{keyId}:{payload} — same strict 4-segment split as AesGcmFieldEncryptor.Decrypt.
        var parts = stored.Split(':', 4);
        if (parts.Length == 4 && string.Equals(parts[2], activeKeyId, StringComparison.Ordinal))
            return FieldReencryptOutcome.AlreadyActiveKey;

        try
        {
            var plaintext = encryptor.Decrypt(stored);
            reencrypted = encryptor.Encrypt(plaintext);
            return FieldReencryptOutcome.Reencrypted;
        }
        catch (CryptographicException)
        {
            // Malformed payload, tampered ciphertext (AuthenticationTagMismatchException derives from
            // CryptographicException), or the embedded keyId is not in the ring.
            return FieldReencryptOutcome.Undecryptable;
        }
    }

    public async Task<EncryptionKeyUsageReportDto> GetKeyUsageReportAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await ListTenantsAsync(cancellationToken);
        var cells = new Dictionary<(string Table, string Column, string KeyId), int>();

        foreach (var tenant in tenants)
        {
            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!db.Database.IsRelational())
                break; // InMemory: no raw stored form to inspect.

            await runner.RunForTenantAsync(tenant.Id, tenant.Subdomain, async ct =>
            {
                await CollectTenantCountsAsync(db, tenant.Id, cells, ct);
            }, cancellationToken);
        }

        return BuildReport(tenants.Count, cells);
    }

    public async Task<FieldReencryptionSummaryDto> ReencryptAsync(CancellationToken cancellationToken = default)
    {
        var before = await GetKeyUsageReportAsync(cancellationToken);
        _logger.LogInformation(
            "[FieldReencrypt] Key usage BEFORE sweep (active {ActiveKeyId}): {@TotalsByKeyId}",
            _activeKeyId, before.TotalsByKeyId);

        var tenants = await ListTenantsAsync(cancellationToken);
        var reencrypted = 0;
        var skipped = 0;

        foreach (var tenant in tenants)
        {
            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!db.Database.IsRelational())
                break;

            await runner.RunForTenantAsync(tenant.Id, tenant.Subdomain, async ct =>
            {
                var (t, s) = await ReencryptTenantAsync(db, tenant.Id, ct);
                reencrypted += t;
                skipped += s;
            }, cancellationToken);
        }

        var after = await GetKeyUsageReportAsync(cancellationToken);
        _logger.LogInformation(
            "[FieldReencrypt] Key usage AFTER sweep (active {ActiveKeyId}): {@TotalsByKeyId} — "
            + "re-encrypted {Reencrypted} row-value(s), skipped {Skipped} undecryptable across {TenantCount} tenant(s). "
            + "Retire an old key ONLY when its AFTER count is 0 (Security/README.md).",
            _activeKeyId, after.TotalsByKeyId, reencrypted, skipped, tenants.Count);

        return new FieldReencryptionSummaryDto(
            _activeKeyId, tenants.Count, reencrypted, skipped, before, after);
    }

    /// <summary>
    /// ALL tenants — ANY status, INCLUDING soft-deleted. See the class remarks for why Suspended/Terminated
    /// tenants are included; soft-deleted tenants are too because a not-yet-purged tenant can be RESTORED
    /// (US-ADM-004): if its rows kept old-key ciphertext invisible to the verification report, retiring the
    /// key would destroy that data on restore. Sweeping them is safe (idempotent re-encryption).
    /// </summary>
    private async Task<List<TenantRef>> ListTenantsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // IgnoreQueryFilters: Tenant carries a global `!IsDeleted` filter (AppDbContext) that would silently
        // drop soft-deleted tenants from BOTH the sweep and the retirement-gate report — exactly the data-loss
        // path documented above. Deliberate, auditor-flagged (MED); pinned by the soft-deleted PG test arm.
        return await db.Tenants
            .IgnoreQueryFilters()
            .Select(t => new TenantRef(t.Id, t.Subdomain))
            .ToListAsync(ct);
    }

    private static async Task CollectTenantCountsAsync(
        AppDbContext db, Guid tenantId, Dictionary<(string, string, string), int> cells, CancellationToken ct)
    {
        foreach (var field in EncryptedFieldRegistry.Fields)
        {
            // Encrypted values grouped by their EMBEDDED keyId (segment 3 of enc:v1:{keyId}:{payload}).
            // Table/column are registry constants (concatenated, not interpolated — see class remarks).
            // Aliases are snake_case: the EFCore.NamingConventions plugin maps an UNMAPPED SqlQueryRaw<T>
            // projection type's properties to snake_case column names (KeyId ⇒ key_id) — PascalCase aliases
            // fail with "required column 'key_id' was not present" on real Postgres.
            var byKeySql =
                "SELECT split_part(" + field.Column + ", ':', 3) AS \"key_id\", count(*)::int AS \"count\" FROM "
                + field.Table + " WHERE tenant_id = {0} AND " + field.Column
                + " LIKE 'enc:v1:%' GROUP BY 1";
            var byKey = await db.Database.SqlQueryRaw<KeyIdCountRow>(byKeySql, tenantId).ToListAsync(ct);
            foreach (var row in byKey)
                Accumulate(cells, (field.Table, field.Column, row.KeyId), row.Count);

            // Plaintext residue — surfaced (never silently ignored) under the (plaintext) pseudo key.
            var plaintextSql =
                "SELECT count(*)::int AS \"Value\" FROM " + field.Table
                + " WHERE tenant_id = {0} AND " + field.Column + " IS NOT NULL AND " + field.Column
                + " NOT LIKE 'enc:v1:%'";
            var plaintext = await db.Database.SqlQueryRaw<int>(plaintextSql, tenantId).SingleAsync(ct);
            if (plaintext > 0)
                Accumulate(cells, (field.Table, field.Column, PlaintextKeyId), plaintext);
        }
    }

    private async Task<(int Reencrypted, int Skipped)> ReencryptTenantAsync(
        AppDbContext db, Guid tenantId, CancellationToken ct)
    {
        var reencrypted = 0;
        var skipped = 0;
        // Parameterized LIKE pattern for "under the ACTIVE key" (keyIds are operator-chosen config constants;
        // none contain LIKE wildcards).
        var activePrefixPattern = AesGcmFieldEncryptor.Prefix + _activeKeyId + ":%";

        foreach (var field in EncryptedFieldRegistry.Fields)
        {
            // Keyset pagination by id: already-re-encrypted rows drop out of the predicate, and the id cursor
            // advances PAST skipped (undecryptable) rows, so the loop always terminates.
            var lastId = Guid.Empty;
            while (true)
            {
                // snake_case aliases — same NamingConventions rule as the count query above (Id ⇒ id, Value ⇒ value).
                var pageSql =
                    "SELECT id, " + field.Column + " AS \"value\" FROM " + field.Table
                    + " WHERE tenant_id = {0} AND " + field.Column + " LIKE 'enc:v1:%' AND " + field.Column
                    + " NOT LIKE {1} AND id > {2} ORDER BY id LIMIT " + BatchSize;
                var page = await db.Database
                    .SqlQueryRaw<IdValueRow>(pageSql, tenantId, activePrefixPattern, lastId)
                    .ToListAsync(ct);
                if (page.Count == 0)
                    break;

                foreach (var row in page)
                {
                    lastId = row.Id;
                    switch (TryReencrypt(row.Value, _activeKeyId, _encryptor, out var updated))
                    {
                        case FieldReencryptOutcome.Reencrypted:
                            var updateSql =
                                "UPDATE " + field.Table + " SET " + field.Column
                                + " = {0} WHERE id = {1} AND tenant_id = {2}";
                            await db.Database.ExecuteSqlRawAsync(
                                updateSql, new object[] { updated!, row.Id, tenantId }, ct);
                            reencrypted++;
                            break;

                        case FieldReencryptOutcome.Undecryptable:
                            skipped++;
                            _logger.LogWarning(
                                "[FieldReencrypt] Undecryptable value in {Table}.{Column} id {RowId} "
                                + "(tenant {TenantId}) — skipped, original left untouched. Corrupt payload, or "
                                + "its key is missing from Encryption:Keys.",
                                field.Table, field.Column, row.Id, tenantId);
                            break;

                        // NotEncrypted / AlreadyActiveKey cannot match the scan predicate; nothing to do.
                    }
                }

                if (page.Count < BatchSize)
                    break;
            }
        }

        return (reencrypted, skipped);
    }

    private EncryptionKeyUsageReportDto BuildReport(
        int tenantsScanned, Dictionary<(string Table, string Column, string KeyId), int> cells)
    {
        var counts = cells
            .OrderBy(c => c.Key.Table).ThenBy(c => c.Key.Column).ThenBy(c => c.Key.KeyId)
            .Select(c => new EncryptionKeyUsageCountDto(c.Key.Table, c.Key.Column, c.Key.KeyId, c.Value))
            .ToList();
        var totals = counts
            .GroupBy(c => c.KeyId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Count), StringComparer.Ordinal);
        return new EncryptionKeyUsageReportDto(
            _activeKeyId, _time.GetUtcNow().UtcDateTime, tenantsScanned, counts, totals);
    }

    private static void Accumulate(
        Dictionary<(string, string, string), int> cells, (string, string, string) key, int count)
        => cells[key] = cells.TryGetValue(key, out var existing) ? existing + count : count;

    private sealed record TenantRef(Guid Id, string Subdomain);

    /// <summary>SqlQueryRaw projection: keyId ⇒ row count for one column.</summary>
    private sealed class KeyIdCountRow
    {
        public string KeyId { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    /// <summary>SqlQueryRaw projection: one row's id + raw stored column value.</summary>
    private sealed class IdValueRow
    {
        public Guid Id { get; set; }
        public string Value { get; set; } = string.Empty;
    }
}
