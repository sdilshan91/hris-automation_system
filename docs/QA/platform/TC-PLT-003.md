---
id: TC-PLT-003
user_story: US-PLT-005
module: Platform
priority: high
type: functional
status: automated
created: 2026-07-24
automated: 2026-07-24
defect:
  - DF-19
  - ISSUE-293
---

# TC-PLT-003: Encrypted-field registry pins the exact at-rest column set + bidirectional model↔registry drift guards, and the pure per-value re-encrypt core

## 1. Test Objective
Verify the shared `EncryptedFieldRegistry` — the single source of truth that BOTH the `DbInitializer`
startup back-fill and the key-rotation re-encryption sweep enumerate — and the pure per-value
`FieldEncryptionMaintenanceService.TryReencrypt` core. The registry content is **pinned** (exact table/
column/back-fill-flag set) and **drift-guarded in both directions** against the live EF model, so that (a)
removing or renaming an encrypted column goes red here, and (b) — the data-destroying direction (the DF-19
lesson) — a column wired through `ApplyEncryption` but **missing** from the registry (which would be skipped
on key rotation and destroyed when the old key retires) also goes red. `TryReencrypt` is proven to move
old-key values to the active key, never rewrite active-key values, and skip plaintext/corrupt/unknown-key
values without throwing.

## 2. Related Requirements
- User Story: US-PLT-005 (Encryption-at-Rest) — the registry underpins AC-3 (which columns are encrypted)
  and AC-4 (tenant-safe key usage / rotation without data loss).
- Finding: DF-19 (registry drift → a rotation could destroy an un-registered encrypted column's data);
  ISSUE-293 / DF-enc-nationalid-backfill (`employees.national_id` added to the registry + startup back-fill).

## 3. Preconditions
- The AES-GCM encryptor and the EF model (with `ApplyEncryption` converters wired in `OnModelCreating`) are
  available. These are unit tests (EF InMemory through the real model + real encryptor); no container needed.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Registry size (all fields) | 9 columns across pip/recommendation/employees | pinned exactly |
| StartupBackfillFields count | 9 | pip(3) + recommendation(5) + employees.national_id(1) |
| Key ring | active k2 + retired k1 | mid-rotation shape |
| Corrupt value | `enc:v1:k1:` + 3 bytes | too short for nonce+tag → CryptographicException |
| Unknown-key value | `enc:v1:kX:…` | keyId absent from the ring |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Assert the exact `(Table, Column, IncludeInStartupBackfill)` set of `EncryptedFieldRegistry.Fields`. | Exactly the 9 pinned entries (pip.reason/final_outcome_notes/escalation_notes; recommendation.current_compensation/bonus_amount/bonus_percent/increment_amount/increment_percent; employees.national_id), all `IncludeInStartupBackfill = true`. | `Registry_pins_the_exact_encrypted_column_set` |
| 2 | Assert `StartupBackfillFields`. | 9 fields; includes `national_id` (DF-enc-nationalid-backfill); tables are exactly pip/recommendation/employees. | `StartupBackfillFields_covers_the_p34_set_plus_national_id` |
| 3 | For every registry entry, find the named entity + property in the EF model and inspect its value converter. | Each names a real property carrying the `IEncryptedValueConverter` whose provider CLR type is `string` — a registry row pointing at an un-encrypted/renamed property goes red. | `Every_registry_entry_is_bound_to_an_encrypted_model_property` |
| 4 | Enumerate every EF property whose converter is an `IEncryptedValueConverter` and compare to the registry. | The two sets match EXACTLY — an encrypted model property missing from the registry (the data-destroying drift on rotation) goes red (DF-19). | `Every_encrypted_model_property_is_listed_in_the_registry` |
| 5 | `TryReencrypt` an old-key (k1) value with active k2. | Outcome `Reencrypted`; output is `enc:v1:k2:` and decrypts back to the original plaintext. | `TryReencrypt_moves_an_old_key_value_to_the_active_key_and_preserves_the_plaintext` |
| 6 | `TryReencrypt` a value already under the active key. | Outcome `AlreadyActiveKey`; output is null — the sweep must never rewrite it. | `TryReencrypt_reports_an_active_key_value_without_producing_any_output_to_write` |
| 7 | `TryReencrypt` legacy plaintext / a corrupt value / an unknown-key value. | `NotEncrypted` / `Undecryptable` / `Undecryptable` respectively; never throws, never yields output to write (the original is never overwritten with garbage). | `TryReencrypt_reports_legacy_plaintext_as_NotEncrypted` · `…skips_a_corrupt_value…` · `…skips_a_value_whose_key_is_missing_from_the_ring` |

## 6. Postconditions
- The at-rest encrypted-column set is pinned and cannot drift from the EF model in either direction; the
  per-value re-encrypt core is proven safe (idempotent, never destructive) before it is driven over real
  columns (TC-PLT-004).

## 7. Test Category Tags
- [x] Happy path (registry pins; old-key → active-key re-encrypt)
- [x] Negative test (plaintext / corrupt / unknown-key never rewritten)
- [x] Boundary test (bidirectional drift guard; already-active-key no-op)
- [x] Security test (prevents a rotation from destroying an un-registered encrypted PII column — DF-19)
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by:** `HRM.Tests/Unit/EncryptedFieldRegistryTests` (8 facts), carrying `[Trait("TC", "TC-PLT-003")]`. Runs in the agent verify gate (EF InMemory + real AES-GCM encryptor; no container).
- The end-to-end sweep over real raw columns is TC-PLT-004 (`FieldEncryptionReencryptPostgresTests`, real Postgres).
