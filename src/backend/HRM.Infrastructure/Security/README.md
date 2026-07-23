# Field encryption at rest — key ring & rotation SOP

App-side **AES-256-GCM** field encryption (P3-4) for the sensitive PIP free-text notes and the
per-employee Recommendation compensation figures. This runbook is the operational counterpart to the
code in this folder — read it before generating, rotating, or retiring an encryption key.

- **Encryptor:** [`AesGcmFieldEncryptor.cs`](AesGcmFieldEncryptor.cs) (registered singleton).
- **EF wiring:** [`../Persistence/Converters/EncryptedFieldConverters.cs`](../Persistence/Converters/EncryptedFieldConverters.cs)
  + `PipConfiguration` / `RecommendationConfiguration` / `EmployeeConfiguration` (ISSUE-293).
- **Column registry (single source of truth):** [`EncryptedFieldRegistry.cs`](EncryptedFieldRegistry.cs) —
  BOTH the startup back-fill AND the rotation re-encryption sweep enumerate it, so the two paths cannot
  drift. ⚠ **Every newly-encrypted column MUST be added there** or rotation will never re-encrypt it and
  retiring the old key destroys its data.
- **One-time plaintext back-fill:** `DbInitializer.EncryptSensitiveFieldsAtRestAsync` (idempotent, on startup).
- **Bulk re-encryption (rotation step 4):** [`FieldEncryptionMaintenanceService.cs`](FieldEncryptionMaintenanceService.cs)
  via `POST /api/v1/system/encryption/reencrypt` + the `GET /api/v1/system/encryption/report` verification gate.
- **Key-age watchdog:** `EncryptionKeyAgeWatchdogJob` (HRM.Api/Jobs, weekly) — warns when the active key
  exceeds the quarterly cadence (`Encryption:RotationCadenceDays`, default 90).
- **ADR / rationale:** app-side AES-GCM was chosen over pgcrypto so the InMemory test provider and EF value
  converters compose cleanly (see the P3-4 entry in `docs/QA/plans/COMPLETION-PLAN.md`).

## What is encrypted

Stored as encrypted `text` columns (format below). RecommendationBudget **pool** amounts are deliberately
NOT encrypted (aggregate arithmetic, not individual PII). The authoritative list is
[`EncryptedFieldRegistry.cs`](EncryptedFieldRegistry.cs); as of today:

| Table | Columns | In startup back-fill? |
|-------|---------|-----------------------|
| `pip` | `reason`, `final_outcome_notes`, `escalation_notes` | yes (had plaintext history) |
| `recommendation` | `current_compensation`, `bonus_amount`, `bonus_percent`, `increment_amount`, `increment_percent` | yes (had plaintext history) |
| `employees` | `national_id` (ISSUE-293) | no — born encrypted (no plaintext window); still in the rotation sweep + report |

**Stored format:** `enc:v1:{keyId}:{base64(nonce ‖ ciphertext ‖ tag)}`. The `keyId` is stored **with** the
value, so a value written under a retired key still decrypts for as long as that key stays in the ring — this
is what makes overlap-based (zero-downtime) rotation possible.

## Configuration

| Config key | Meaning |
|------------|---------|
| `Encryption:ActiveKeyId` | The `keyId` used to encrypt **new** writes. Must exist in `Encryption:Keys`. |
| `Encryption:Keys:{keyId}` | The key ring: `keyId` ⇒ base64-encoded **32-byte** (256-bit) AES key. Retired keys stay here to decrypt old values. |

- **Dev/test:** a committed dev-only key is supplied via `appsettings.Development.json` (and the test config).
- **Prod/staging (⚠ deploy-gate):** the real key MUST be supplied via env/secret store
  (`Encryption__Keys__{keyId}` — double-underscore) **before app start**. With no usable active key the
  constructor **fail-fasts** — the app never silently stores plaintext.
- Tamper/wrong-key on read throws (`AuthenticationTagMismatchException`), never falls back to plaintext.

### Generating a new 32-byte key

```bash
openssl rand -base64 32
```
```powershell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Max 256 }))   # dev only
# prefer a CSPRNG in prod, e.g. via openssl or your secret store's key-gen
```

## Rotation SOP (overlap-based, zero-downtime)

Rotate on a fixed **quarterly** cadence and immediately on suspected compromise. The weekly
`EncryptionKeyAgeWatchdogJob` logs a `[EncryptionKeyAge]` **WARNING** once the active key's age reaches
`Encryption:RotationCadenceDays` (default **90**; override via config/env `Encryption__RotationCadenceDays`).
It tracks key age in the system-scope `encryption_key_activation` table (first-seen per keyId — the config
ring itself records nothing about *when* a key was installed). Because each value carries its own `keyId`,
rotation is a *two-phase* operation: flip the active key, then re-encrypt the backlog.

1. **Generate** a new key, e.g. `hrm-field-key-2` (see above).
2. **Add it to the ring alongside the current key** — set `Encryption__Keys__hrm-field-key-2` **and keep**
   `Encryption__Keys__hrm-field-key-1`. Deploy/restart. (No behaviour change yet; both keys can now decrypt.)
3. **Flip the active key:** set `Encryption__ActiveKeyId=hrm-field-key-2`. Restart. From now on **new** writes
   use key-2; existing values still decrypt under key-1 (their embedded `keyId`).
4. **Re-encrypt the backlog:** `POST /api/v1/system/encryption/reencrypt` (SystemAdmin JWT — gated
   `Tenant.Lifecycle`; in dev you can also enqueue `ReencryptFieldKeyJob` from the Hangfire dashboard). The
   sweep enumerates **every** registry column across **all** tenants — including Suspended/Terminated and
   soft-deleted-but-not-yet-purged ones (a restorable tenant's data must also move off the old key) —
   re-encrypting each value stored under a non-active ring key.
   Batched, idempotent, re-run safe; an undecryptable (corrupt) value is skipped + logged, never overwritten.
   Runs per tenant through `ITenantJobRunner`, so it stays correct under RLS-ON (the GUC is set — a bare
   scan would silently see 0 rows). The startup back-fill does NOT do this (it only encrypts *plaintext*).
5. **Verify no value still references the old key:** `GET /api/v1/system/encryption/report` (same permission)
   returns per-keyId row counts across every encrypted column; the sweep also logs BEFORE/AFTER totals
   (`[FieldReencrypt]`). The old key's count must be **0** (plaintext residue shows under the `(plaintext)`
   pseudo key; an undecryptable row stays visible under its embedded keyId — investigate before retiring).
   Then **retire** it (remove `Encryption__Keys__hrm-field-key-1` from the environment and restart). Removing
   a key that any stored value still references makes those rows undecryptable (`Decrypt` throws
   "No key '{id}' in the ring").

### Verification (are any values still under the old key?)

Preferred: `GET /api/v1/system/encryption/report` — it is registry-driven, so it can never miss a column the
way a hand-maintained query can. Raw-SQL equivalent per column (repeat for every
[`EncryptedFieldRegistry`](EncryptedFieldRegistry.cs) column; every count must be **0** before retiring
`hrm-field-key-1`):

```sql
SELECT split_part(reason, ':', 3) AS key_id, count(*)
FROM pip WHERE reason LIKE 'enc:v1:%' GROUP BY 1;  -- and so on for each registry column
```

## Retention & retirement rules

- **Keep** a retired key in the ring until step 5 confirms **zero** values reference it. Data loss otherwise.
- **Never delete** the last/active key. `ActiveKeyId` must always point at a present key or the app fail-fasts.
- **Compromise:** treat the encrypted data as only as safe as the key. On suspected key exposure, rotate
  immediately (steps 1-5, compressed) and handle the exposed data per your incident-response policy.
- **Backups:** a database backup is only decryptable with the keys that were active when it was written — keep
  retired keys archived in the secret store for as long as you retain backups that may contain values under them.

## Quick reference

| Action | Steps |
|--------|-------|
| First-time prod setup | Set `Encryption__ActiveKeyId` + `Encryption__Keys__{id}` (base64 32-byte) before start. |
| Routine rotation (quarterly) | Add new key → flip `ActiveKeyId` → restart → `POST /api/v1/system/encryption/reencrypt` → `GET .../report` shows 0 on old key → retire old key (keep archived while backups reference it). |
| Emergency rotation | Same, compressed; prioritise step 4 re-encryption, then retire the compromised key. |
| Retire a key | Confirm `GET /api/v1/system/encryption/report` shows 0 for it, then remove `Encryption__Keys__{oldId}`. |
| Watch the cadence | `EncryptionKeyAgeWatchdogJob` (weekly) warns `[EncryptionKeyAge]` at ≥ `Encryption:RotationCadenceDays` (default 90). |
| Encrypt a NEW column | Add the converter (ApplyEncryption) **and** the [`EncryptedFieldRegistry`](EncryptedFieldRegistry.cs) entry — the registry drives back-fill, sweep and report. |
