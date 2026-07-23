namespace HRM.Domain.Entities;

/// <summary>
/// SYSTEM-scope record of when a field-encryption key was FIRST SEEN as <c>Encryption:ActiveKeyId</c> — the
/// key ring itself lives in config/secret store, which records nothing about *when* a key was activated, so
/// without this the quarterly key-age watchdog (<c>EncryptionKeyAgeWatchdogJob</c>) would have no basis for
/// "this key is N days old, rotate it".
///
/// <para>Deliberately NOT a <see cref="BaseEntity"/>: like <c>tenants</c> / the Data Protection key ring it is
/// a platform-wide table with NO <c>TenantId</c> — the encryption key is shared by all tenants — so the
/// tenant interceptor/query-filter/RLS-policy rules for tenant tables do not apply. Append-only upsert: one
/// row per keyId, written once by the watchdog on first sight; rows for retired keys are retained as a
/// rotation history.</para>
/// </summary>
public sealed class EncryptionKeyActivation
{
    /// <summary>The <c>Encryption:Keys</c> keyId (e.g. <c>hrm-field-key-1</c>). Primary key.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>UTC instant this keyId was first observed as the active key (watchdog upsert).</summary>
    public DateTime FirstSeenUtc { get; set; }
}
