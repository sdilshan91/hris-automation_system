namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Key-rotation maintenance over the field-at-rest encrypted columns (the P3-4 follow-up: the runbook's
/// "re-encrypt the backlog" step, previously a documented-but-unbuilt gap). Two operations, both driven by the
/// shared <c>EncryptedFieldRegistry</c> (Infrastructure) so they can never drift from the startup back-fill:
/// <list type="bullet">
///   <item><see cref="GetKeyUsageReportAsync"/> — READ-ONLY per-keyId row counts across every encrypted column
///     (the key-retirement verification gate: "0 rows on the old key → safe to retire").</item>
///   <item><see cref="ReencryptAsync"/> — the bulk re-encryption sweep: every value stored under a NON-active
///     ring key is decrypted and re-written under <c>Encryption:ActiveKeyId</c>.</item>
/// </list>
/// <para><b>RLS-critical:</b> both operations enumerate tenants (the RLS-exempt <c>tenants</c> table) and run
/// each tenant's scan inside <c>ITenantJobRunner.RunForTenantAsync</c>, so under RLS-ON the
/// <c>app.current_tenant</c> GUC is set — a bare scan on a fresh <c>hrm_app</c> connection would fail-closed
/// to 0 rows and silently no-op (the DF-50/ISSUE-268 class).</para>
/// </summary>
public interface IFieldEncryptionMaintenanceService
{
    /// <summary>
    /// Per-keyId row counts across all encrypted columns and ALL tenants (any status, including soft-deleted —
    /// a restorable tenant's rows must stay visible to the retirement gate). Plaintext residue is reported
    /// under the <c>(plaintext)</c> pseudo key. Read-only.
    /// </summary>
    Task<EncryptionKeyUsageReportDto> GetKeyUsageReportAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-encrypts every value stored under a non-active ring key with the active key. Batched, idempotent /
    /// re-run safe (already-active-key rows never match the scan predicate). An undecryptable value (corrupt,
    /// or its key is missing from the ring) is skipped + counted — the sweep never crashes on it and never
    /// writes garbage. Logs the per-keyId usage BEFORE and AFTER.
    /// </summary>
    Task<FieldReencryptionSummaryDto> ReencryptAsync(CancellationToken cancellationToken = default);
}

/// <summary>Row count for one (table, column, keyId) cell of the key-usage report.</summary>
public sealed record EncryptionKeyUsageCountDto(string Table, string Column, string KeyId, int Count);

/// <summary>
/// The key-retirement verification report: how many stored values sit under each ring key (plus the
/// <c>(plaintext)</c> pseudo key for unencrypted residue), per column and totalled.
/// </summary>
/// <param name="MfaSecretsLegacyPlaintext">
/// SYSTEM-SCOPE count, kept DELIBERATELY SEPARATE from the AES-GCM registry section (<see cref="Counts"/> /
/// <see cref="TotalsByKeyId"/>): the number of <c>users.mfa_secret</c> rows that are non-null and NOT yet
/// DataProtection-protected — i.e. legacy plaintext awaiting the US-PLT-005 (Scope A) startup back-fill.
/// <para><b>Why this is not another registry column:</b> <c>mfa_secret</c> is protected by ASP.NET Data
/// Protection, NOT the AES-GCM <c>EncryptedFieldRegistry</c>, and it must STAY that way — <c>users</c> has no
/// <c>tenant_id</c>, but the registry sweep hard-codes <c>WHERE tenant_id = …</c>, so a registry entry would
/// throw <c>42703 column "tenant_id" does not exist</c> (and attaching an <c>IEncryptedValueConverter</c> would
/// trip the reverse drift guard forcing registry membership). It is therefore counted here, tenant-agnostic and
/// visibly apart from the ring-key totals, so nobody mistakes it for an AES-GCM field.</para>
/// </param>
public sealed record EncryptionKeyUsageReportDto(
    string ActiveKeyId,
    DateTime GeneratedAtUtc,
    int TenantsScanned,
    IReadOnlyList<EncryptionKeyUsageCountDto> Counts,
    IReadOnlyDictionary<string, int> TotalsByKeyId,
    int MfaSecretsLegacyPlaintext);

/// <summary>Outcome of one <see cref="IFieldEncryptionMaintenanceService.ReencryptAsync"/> run.</summary>
public sealed record FieldReencryptionSummaryDto(
    string ActiveKeyId,
    int TenantsProcessed,
    int RowsReencrypted,
    int RowsSkippedUndecryptable,
    EncryptionKeyUsageReportDto Before,
    EncryptionKeyUsageReportDto After);

/// <summary>
/// Response of <c>POST /api/v1/system/encryption/reencrypt</c>. <c>Mode</c> is <c>enqueued</c> (Hangfire
/// available — poll the GET report / Hangfire dashboard for completion) or <c>inline</c> (no background job
/// client — the run completed synchronously and <c>Summary</c> is populated).
/// </summary>
public sealed record FieldReencryptionTriggerDto(
    string Mode, string? JobId, FieldReencryptionSummaryDto? Summary);
