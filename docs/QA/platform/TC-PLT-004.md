---
id: TC-PLT-004
user_story: US-PLT-005
module: Platform
priority: critical
type: security
status: automated
created: 2026-07-24
automated: 2026-07-24
defect:
  - ISSUE-293
  - DF-enc-nationalid-backfill
---

# TC-PLT-004: Real-Postgres bulk re-encryption sweep + key-usage retirement gate — moves old-key ciphertext to the active key across tenants (incl. suspended/soft-deleted), heals plaintext residue, byte-stable on active rows

## 1. Test Objective
Verify the key-rotation maintenance service (`FieldEncryptionMaintenanceService`) end-to-end against a real
Postgres column set — what InMemory cannot prove because the sweep is raw SQL over the RAW stored bytes.
It proves: (1) values under a **retired** ring key (k1) are re-encrypted under the **active** key (k2) and
still decrypt through EF — across `pip.reason`, `recommendation.bonus_amount`, AND `employees.national_id`
(the ISSUE-293 column the old hand-maintained back-fill list missed); (2) rows already under the active key
are **byte-identical** afterwards (never rewritten); (3) an undecryptable value is skipped + counted, the
sweep continues, and a second run re-skips it (idempotent); (4) the per-keyId usage report — the key-
retirement verification gate — counts correctly BEFORE and AFTER; (5) the sweep is per-tenant via
`ITenantJobRunner` and deliberately includes **Suspended** and **soft-deleted** tenants (or retiring the key
destroys their data); (6) the registry-driven startup back-fill heals plaintext in ANY
`StartupBackfillFields` column (pip.reason AND `employees.national_id`, per DF-enc-nationalid-backfill) and
the report surfaces the residue under a `(plaintext)` pseudo-key BEFORE the heal and zero after.

## 2. Related Requirements
- User Story: US-PLT-005
- Acceptance Criteria: AC-3 (PII columns encrypted at rest — proven via the raw-column ciphertext) and AC-4
  (two-plus tenants; key usage/decryption remains tenant-safe with no cross-tenant secret exposure — proven
  by the per-tenant sweep spanning Active/Suspended/soft-deleted tenants).
- Finding: ISSUE-293 (national_id encrypted at rest), DF-enc-nationalid-backfill (national_id added to the
  registry back-fill), plus the `IgnoreQueryFilters` fix so a soft-deleted tenant is not silently dropped
  from the sweep + retirement gate (US-ADM-004 restore safety).

## 3. Preconditions
- A Postgres 17 container (Testcontainers) with the P3-4 migration applied. Executed by the orchestrator's
  Postgres run (the agent verify gate has no Docker).
- A shared key ring: active **k2** + retired **k1**; a separate k1-only writer to fabricate pre-rotation raw
  values. Deterministic (static) keys so the process-wide EF model cache stays consistent.
- Tenants seeded via the privileged path: an Active tenant, a Suspended tenant, and a soft-deleted tenant,
  each with an FK parent chain so Postgres accepts the child rows.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Active-tenant old-key values | pip.reason, recommendation.bonus_amount, employees.national_id under k1 | re-encrypted to k2 |
| Active-tenant active-key row | pip.reason under k2 | must stay byte-identical |
| Corrupt row | `enc:v1:kX:` + 3 bytes | skipped + counted, never overwritten |
| Suspended-tenant secret | pip.reason under k1 | must also move off the old key |
| Soft-deleted tenant | `IsDeleted = true`, one k1 pip.reason | must be seen by the sweep + gate |
| Plaintext residue | pip.reason + employees.national_id plaintext | healed by the startup back-fill |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Seed an Active + Suspended tenant with old-key (k1) pip/recommendation/national_id values, one already-active (k2) pip, and one corrupt (unknown-key) pip; take the BEFORE key-usage report. | Report: active key k2; 4 values under k1; 1 under the unknown key kX. | `Reencrypt_sweep_moves_old_key_values_leaves_active_rows_byte_identical_and_skips_corrupt` (report arm) |
| 2 | Run `ReencryptAsync()`. | Summary: `RowsReencrypted == 4` (3 Active + 1 Suspended k1 values), `RowsSkippedUndecryptable == 1`; AFTER report has NO k1 key (retire-safe) and still 1 under kX (the corrupt row surfaced, never overwritten). | same fact |
| 3 | Inspect the RAW columns + EF reads after the sweep. | pip.reason, recommendation.bonus_amount, employees.national_id (Active) and the Suspended pip.reason all now `enc:v1:k2:` and decrypt to their originals; the already-active pip.reason is byte-for-byte unchanged; the corrupt row's bytes are untouched. | same fact |
| 4 | Run `ReencryptAsync()` a SECOND time. | `RowsReencrypted == 0`, `RowsSkippedUndecryptable == 1` — idempotent / re-run safe. | same fact (second-run arm) |
| 5 | Write plaintext residue into pip.reason + employees.national_id, take the report, run `DbInitializer.EncryptSensitiveFieldsAtRestAsync`, re-report. | BEFORE: the report surfaces exactly the one plaintext national_id under the `(plaintext)` pseudo-key; AFTER: both columns are `enc:v1:k2:`, decrypt to the original, and zero plaintext national_id rows remain. | `Registry_backfill_encrypts_both_pip_and_national_id_plaintext_and_report_surfaces_it` |
| 6 | Seed a **soft-deleted** (`IsDeleted=true`) tenant with a k1 pip.reason; report + sweep. | The report SEES its k1 row (not dropped by the `!IsDeleted` global filter), the sweep re-encrypts it to k2, AFTER report has no k1 — so retiring the key cannot destroy a soft-deleted tenant's data (US-ADM-004 restore safety). | `Sweep_and_report_include_a_soft_deleted_tenant` |

## 6. Postconditions
- All old-key ciphertext across every tenant (Active, Suspended, soft-deleted) has moved to the active key
  and the retirement gate reads zero on the old key; active-key rows are untouched; plaintext residue is
  healed. The old key can now be retired without data loss.

## 7. Test Category Tags
- [x] Happy path (old-key → active-key sweep; plaintext back-fill heals)
- [x] Negative test (corrupt/unknown-key row skipped, never overwritten)
- [x] Boundary test (active-key row byte-identical; idempotent second run; soft-deleted tenant included)
- [x] Security test (retirement gate proves no residual old-key ciphertext; PII stays encrypted)
- [x] Multi-tenant isolation (per-tenant sweep via `ITenantJobRunner` spanning Active/Suspended/soft-deleted tenants)
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by:** `HRM.Tests/Integration/FieldEncryptionReencryptPostgresTests` (3 facts), carrying `[Trait("TC", "TC-PLT-004")]` (the class also carries `[Trait("TC", "TC-PLT-P34")]`). Real Postgres — executed on the orchestrator's Postgres run, not the Docker-less agent verify gate.
- The pure per-value `TryReencrypt` core + the registry drift guards this sweep relies on are TC-PLT-003 (`EncryptedFieldRegistryTests`).
- **Note:** the `employees.national_id` **back-fill parity** that a "TC-PLT-005" would nominally cover is asserted here by `Registry_backfill_encrypts_both_pip_and_national_id_plaintext_and_report_surfaces_it` (this TC) — there is no separate `TC-PLT-005` trait in code.
