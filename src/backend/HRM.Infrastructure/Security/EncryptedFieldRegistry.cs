using HRM.Domain.Entities;
using HRM.Domain.Performance;

namespace HRM.Infrastructure.Security;

/// <summary>
/// ONE column, encrypted at rest via <c>IFieldEncryptor</c> (P3-4 / ISSUE-293).
/// </summary>
/// <param name="Table">Physical (snake_case) table name.</param>
/// <param name="Column">Physical (snake_case) column name.</param>
/// <param name="Entity">CLR entity name (nameof) — lets tests bind the registry to the EF model.</param>
/// <param name="Property">CLR property name (nameof) — lets tests assert the converter is actually wired.</param>
/// <param name="IncludeInStartupBackfill">
/// Whether <c>DbInitializer.EncryptSensitiveFieldsAtRestAsync</c> back-fills legacy PLAINTEXT values in this
/// column on startup. <b>true</b> for the original P3-4 pip/recommendation columns (they had a plaintext
/// history that the back-fill migrated). <b>false</b> for columns that were BORN encrypted (added with the
/// converter already applied, so no plaintext window ever existed — e.g. <c>employees.national_id</c>,
/// ISSUE-293/migration 20260718201107): excluding them keeps the startup back-fill byte-identical to its
/// pre-registry behaviour. Every column, regardless of this flag, IS scanned by the key-rotation
/// re-encryption sweep and counted in the key-usage report (any plaintext residue surfaces there as the
/// <c>(plaintext)</c> pseudo key rather than being silently ignored).
/// </param>
public sealed record EncryptedField(
    string Table, string Column, string Entity, string Property, bool IncludeInStartupBackfill);

/// <summary>
/// The single authoritative list of every field-at-rest ENCRYPTED column (AES-256-GCM via
/// <see cref="AesGcmFieldEncryptor"/>). Both consumers route through this registry so they can never drift
/// (the DF-19 lesson — when two paths each hand-maintain the same set, a column added to one silently misses
/// the other):
/// <list type="bullet">
///   <item><c>DbInitializer.EncryptSensitiveFieldsAtRestAsync</c> — the startup plaintext→ciphertext back-fill
///     (entries with <see cref="EncryptedField.IncludeInStartupBackfill"/> = true).</item>
///   <item><c>FieldEncryptionMaintenanceService</c> — the key-rotation re-encryption sweep + per-keyId usage
///     report (ALL entries).</item>
/// </list>
/// ⚠ When you encrypt a NEW column (a new <c>ApplyEncryption</c> property in an entity configuration invoked
/// from <c>AppDbContext.OnModelCreating</c>), you MUST add it here — otherwise key rotation will never
/// re-encrypt it and retiring the old key destroys that column's data. The registry content is pinned by
/// <c>EncryptedFieldRegistryTests</c>; the sweep/back-fill behaviour by <c>FieldEncryptionPostgresTests</c> /
/// <c>FieldEncryptionReencryptPostgresTests</c>. Runbook: <c>Security/README.md</c>.
/// </summary>
public static class EncryptedFieldRegistry
{
    /// <summary>Every encrypted column (the re-encryption sweep + key-usage report set).</summary>
    public static IReadOnlyList<EncryptedField> Fields { get; } =
    [
        // P3-4: sensitive PIP free-text notes (had a plaintext history → startup back-fill applies).
        new("pip", "reason", nameof(Pip), nameof(Pip.Reason), IncludeInStartupBackfill: true),
        new("pip", "final_outcome_notes", nameof(Pip), nameof(Pip.FinalOutcomeNotes), IncludeInStartupBackfill: true),
        new("pip", "escalation_notes", nameof(Pip), nameof(Pip.EscalationNotes), IncludeInStartupBackfill: true),

        // P3-4: per-employee Recommendation compensation figures (numeric→text migration + plaintext history).
        // Deliberately EXCLUDES the RecommendationBudget pool amounts (aggregate arithmetic, not individual PII).
        new("recommendation", "current_compensation", nameof(Recommendation), nameof(Recommendation.CurrentCompensation), IncludeInStartupBackfill: true),
        new("recommendation", "bonus_amount", nameof(Recommendation), nameof(Recommendation.BonusAmount), IncludeInStartupBackfill: true),
        new("recommendation", "bonus_percent", nameof(Recommendation), nameof(Recommendation.BonusPercent), IncludeInStartupBackfill: true),
        new("recommendation", "increment_amount", nameof(Recommendation), nameof(Recommendation.IncrementAmount), IncludeInStartupBackfill: true),
        new("recommendation", "increment_percent", nameof(Recommendation), nameof(Recommendation.IncrementPercent), IncludeInStartupBackfill: true),

        // ISSUE-293: Employee.NationalId PII — BORN encrypted (column added as `text` with the converter in the
        // same release, migration 20260718201107_Employee_NationalId), so no plaintext history exists in practice.
        // Still INCLUDED in the startup back-fill (DF-enc-nationalid-backfill, user-approved): the back-fill is a
        // no-op today (every row already `enc:v1:%`), but on a PII column it is safe, idempotent defence-in-depth —
        // any residue that ever bypassed the converter is HEALED at boot instead of only surfaced in the report.
        // excluded from the startup back-fill (keeps DbInitializer byte-identical to its pre-registry column
        // set). It IS in the rotation sweep — without it, retiring an old key would break every national_id.
        new("employees", "national_id", nameof(Employee), nameof(Employee.NationalId), IncludeInStartupBackfill: true),
    ];

    /// <summary>The subset the startup plaintext back-fill processes (the original, pinned P3-4 column set).</summary>
    public static IEnumerable<EncryptedField> StartupBackfillFields =>
        Fields.Where(f => f.IncludeInStartupBackfill);
}
