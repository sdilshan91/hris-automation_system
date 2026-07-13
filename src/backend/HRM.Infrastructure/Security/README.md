# Field encryption at rest — key ring & rotation SOP

App-side **AES-256-GCM** field encryption (P3-4) for the sensitive PIP free-text notes and the
per-employee Recommendation compensation figures. This runbook is the operational counterpart to the
code in this folder — read it before generating, rotating, or retiring an encryption key.

- **Encryptor:** [`AesGcmFieldEncryptor.cs`](AesGcmFieldEncryptor.cs) (registered singleton).
- **EF wiring:** [`../Persistence/Converters/EncryptedFieldConverters.cs`](../Persistence/Converters/EncryptedFieldConverters.cs)
  + `PipConfiguration` / `RecommendationConfiguration`.
- **One-time plaintext back-fill:** `DbInitializer.EncryptSensitiveFieldsAtRestAsync` (idempotent, on startup).
- **ADR / rationale:** app-side AES-GCM was chosen over pgcrypto so the InMemory test provider and EF value
  converters compose cleanly (see the P3-4 entry in `docs/QA/plans/COMPLETION-PLAN.md`).

## What is encrypted

Stored as encrypted `text` columns (format below). RecommendationBudget **pool** amounts are deliberately
NOT encrypted (aggregate arithmetic, not individual PII).

| Table | Columns |
|-------|---------|
| `pip` | `reason`, `final_outcome_notes`, `escalation_notes` |
| `recommendation` | `current_compensation`, `bonus_amount`, `bonus_percent`, `increment_amount`, `increment_percent` |

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

Rotate on a fixed cadence (recommend **annually**) and immediately on suspected compromise. Because each value
carries its own `keyId`, rotation is a *two-phase* operation: flip the active key, then re-encrypt the backlog.

1. **Generate** a new key, e.g. `hrm-field-key-2` (see above).
2. **Add it to the ring alongside the current key** — set `Encryption__Keys__hrm-field-key-2` **and keep**
   `Encryption__Keys__hrm-field-key-1`. Deploy/restart. (No behaviour change yet; both keys can now decrypt.)
3. **Flip the active key:** set `Encryption__ActiveKeyId=hrm-field-key-2`. Restart. From now on **new** writes
   use key-2; existing values still decrypt under key-1 (their embedded `keyId`).
4. **Re-encrypt the backlog under the new key.** ⚠ **This is NOT automatic.** The startup back-fill
   (`EncryptSensitiveFieldsAtRestAsync`) only encrypts *plaintext* (`NOT LIKE 'enc:v1:%'`); it does **not**
   re-encrypt values already encrypted under an older key. To move values off key-1 you must **rewrite** each
   affected row so the value is re-encrypted with the active key — e.g. load and re-save the entities through
   EF (the value converter re-encrypts on save), or run a dedicated maintenance pass over the 8 columns.
   > **Known gap / follow-up:** there is no built-in bulk re-encryption job today. If your rotation cadence
   > makes step 4 burdensome, build a one-shot admin/maintenance command that re-saves the `pip` +
   > `recommendation` rows in batches (file it as a follow-up story before the first real rotation).
5. **Verify no value still references the old key**, then **retire** it (remove `Encryption__Keys__hrm-field-key-1`
   from the environment and restart). See the verification query below. Removing a key that any stored value
   still references makes those rows undecryptable (`Decrypt` throws "No key '{id}' in the ring").

### Verification query (are any values still under the old key?)

Run against the app database; every count must be **0** before retiring `hrm-field-key-1`:

```sql
SELECT 'pip.reason'              AS col, count(*) FROM pip            WHERE reason               LIKE 'enc:v1:hrm-field-key-1:%'
UNION ALL SELECT 'pip.final_outcome_notes',  count(*) FROM pip            WHERE final_outcome_notes  LIKE 'enc:v1:hrm-field-key-1:%'
UNION ALL SELECT 'pip.escalation_notes',     count(*) FROM pip            WHERE escalation_notes     LIKE 'enc:v1:hrm-field-key-1:%'
UNION ALL SELECT 'rec.current_compensation', count(*) FROM recommendation WHERE current_compensation LIKE 'enc:v1:hrm-field-key-1:%'
UNION ALL SELECT 'rec.bonus_amount',         count(*) FROM recommendation WHERE bonus_amount         LIKE 'enc:v1:hrm-field-key-1:%'
UNION ALL SELECT 'rec.bonus_percent',        count(*) FROM recommendation WHERE bonus_percent        LIKE 'enc:v1:hrm-field-key-1:%'
UNION ALL SELECT 'rec.increment_amount',     count(*) FROM recommendation WHERE increment_amount     LIKE 'enc:v1:hrm-field-key-1:%'
UNION ALL SELECT 'rec.increment_percent',    count(*) FROM recommendation WHERE increment_percent    LIKE 'enc:v1:hrm-field-key-1:%';
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
| Routine rotation | Add new key → flip `ActiveKeyId` → re-encrypt backlog → verify 0 → retire old key. |
| Emergency rotation | Same, compressed; prioritise step 4 re-encryption, then retire the compromised key. |
| Retire a key | Confirm the verification query returns all 0, then remove `Encryption__Keys__{oldId}`. |
