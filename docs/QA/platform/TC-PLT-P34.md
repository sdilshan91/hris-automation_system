---
id: TC-PLT-P34
user_story: US-PLT-005
module: Platform
priority: critical
type: security
status: automated
created: 2026-07-24
automated: 2026-07-24
defect:
  - ISSUE-293
---

# TC-PLT-P34: Field-at-rest encryption (P3-4) — sensitive PII / compensation columns are AES-GCM ciphertext at rest and round-trip through EF

## 1. Test Objective
Verify the P3-4 field-at-rest encryption feature: designated sensitive columns — PIP notes
(`pip.reason` / `final_outcome_notes` / `escalation_notes`), per-employee compensation on
`recommendation` (current/bonus/increment amounts + percents), and `employees.national_id` (ISSUE-293 PII)
— are stored as `enc:v1:` **AES-GCM ciphertext at rest** (never plaintext) and transparently decrypt back
to the original CLR value on read through the EF value converters. Proven at three layers: the pure
converter boundary, EF end-to-end (InMemory), and against a real Postgres column where only a live
relational store can prove the RAW stored bytes are ciphertext and the `DbInitializer` back-fill is
idempotent.

> **Traceability note (flag):** the `TC-PLT-P34` trait's `"P34"` denotes the **field-at-rest encryption
> Phase 3-4** work (per the test file headers), i.e. the encryption-at-rest half of US-PLT-005 — NOT RLS.
> It is bound here to **US-PLT-005**, not US-PLT-002.

## 2. Related Requirements
- User Story: US-PLT-005 (Encryption-at-Rest for Sensitive PII)
- Acceptance Criteria: AC-3 (designated PII columns encrypted at rest per the spec's §6 pgcrypto guidance —
  here app-layer AES-GCM), and the migration-of-existing-plaintext clause (idempotent startup back-fill).
- Finding: ISSUE-293 (Employee.NationalId was plaintext PII → now born-encrypted at rest)
- **Not covered by this TC:** AC-1 (MFA/TOTP secret) and AC-2 (tenant SMTP/IdP envelope secret) — those
  columns are out of scope of the bound tests.

## 3. Preconditions
- The AES-GCM `IFieldEncryptor` ring (active key + optional retired keys) is configured.
- For the real-Postgres arms: a Postgres 17 container with the P3-4 encrypt-columns migration applied
  (executed by the orchestrator's Postgres run; the agent verify gate is Docker-less and runs only the
  converter + InMemory arms).
- A seeded FK parent chain (Department + JobTitle → Employee, + an AppraisalCycle) so Postgres accepts the
  child Pip/Recommendation rows.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Pip.Reason | "Confidential PIP reason." | required-string encrypted column |
| Recommendation.BonusAmount | 7500.25m | decimal→ciphertext-text column |
| Employee.NationalId | "SL-CIPHER-001" / "SL-931204567V" | ISSUE-293 PII column |
| Legacy plaintext row | written via NoOp-encryptor context | models a pre-P3-4 row for the back-fill |
| Raw column prefix (encrypted) | `enc:v1:` | at-rest ciphertext marker |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Convert a string / decimal / null through the encryption value converters directly. | `ConvertToProvider` yields `enc:v1:` ciphertext (never the plaintext/number); `ConvertFromProvider` round-trips to the original; null maps to null; a legacy plain numeric string is read verbatim. | `EncryptedValueConverterTests` (5 facts) |
| 2 | Write a Pip, Recommendation, and Employee (national_id) through the AES-GCM EF context, read back via a FRESH scoped context. | All sensitive fields decrypt back to the original plaintext/decimals; the model cache discriminates by encryptor type so a NoOp-encryptor context never collides with the real one. | `FieldEncryptionIntegrationTests` (4 facts, InMemory) |
| 3 | (Real Postgres) Persist Pip/Recommendation/Employee, then read the RAW column via SQL. | The raw stored values start with `enc:v1:` and do NOT contain the plaintext/number (genuine at-rest encryption — the WIRING proof InMemory cannot give); a fresh EF read decrypts them back. | `FieldEncryptionPostgresTests.Persisted_values_are_ciphertext_in_the_raw_column_and_decrypt_on_read` |
| 4 | (Real Postgres) Write legacy PLAINTEXT rows via a NoOp-encryptor context, run `DbInitializer.EncryptSensitiveFieldsAtRestAsync`, then run it again. | After the first run the raw column is `enc:v1:` ciphertext and still decrypts to the original; the second run is a no-op (already-encrypted rows are byte-for-byte unchanged — idempotent). | `FieldEncryptionPostgresTests.Backfill_encrypts_legacy_plaintext_and_is_idempotent` |

## 6. Postconditions
- The designated PII / compensation columns are ciphertext at rest; a database-only read yields
  `enc:v1:` blobs, not plaintext. The startup back-fill heals pre-existing plaintext and is safe to re-run.

## 7. Test Category Tags
- [x] Happy path (encrypt-on-write / decrypt-on-read round-trip)
- [x] Negative test (null-handling; legacy-plaintext read path)
- [x] Boundary test (idempotent second back-fill run; model-cache discriminator)
- [x] Security test (at-rest ciphertext proven against a real column; PII not stored plaintext)
- [ ] Multi-tenant isolation (covered by the per-tenant sweep in TC-PLT-004)
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by, all carrying `[Trait("TC", "TC-PLT-P34")]`:**
  - `HRM.Tests/Unit/EncryptedValueConverterTests` (5 facts — converter boundary)
  - `HRM.Tests/Integration/FieldEncryptionIntegrationTests` (4 facts — EF end-to-end, InMemory)
  - `HRM.Tests/Integration/FieldEncryptionPostgresTests` (real Postgres; the ciphertext + back-fill arms — the class also carries `TC-CHR-332` on the national_id round-trip fact)
  - `HRM.Tests/Integration/FieldEncryptionReencryptPostgresTests` (also `TC-PLT-P34`; its rotation/sweep arms are documented under TC-PLT-004)
- The real-Postgres arms run on the orchestrator's Postgres pass (Testcontainers); the converter + InMemory arms run in the agent verify gate.
