---
id: TC-AUTH-113
user_story: US-AUTH-005
module: Authentication
priority: high
type: integration
status: pass
created: 2026-07-09
verified: 2026-07-09 (PR #224 merged; 3/3 green on merged test/local-subdomains, Docker up)
---

# TC-AUTH-113: Data Protection key ring persists to Postgres and survives a redeploy (cross-instance MFA-secret decryption)

## 1. Test Objective
Verify that the ASP.NET Core Data Protection key ring — which encrypts the TOTP MFA secret at rest (purpose `HRM.MfaSecret.v1`, US-AUTH-005 NFR-2) — is **persisted to Postgres** (`data_protection_keys` table via EF `PersistKeysToDbContext<AppDbContext>()`) and shared across process instances under a fixed application name (`SetApplicationName("HRM")`). This closes **ISSUE-247** (HIGH): the previous default **ephemeral, per-instance** key ring rotated on every redeploy / differed per instance, so a secret encrypted on one instance became **undecryptable** on the next — silently breaking MFA login after a deploy.

## 2. Related Requirements
- User Story: US-AUTH-005
- Non-Functional Requirements: NFR-2 (encrypt the TOTP MFA secret at rest)
- Defect: ISSUE-247 (HIGH, Authentication) — ephemeral Data Protection ring; MFA secrets undecryptable after redeploy / on a second instance

## Automated Test Binding
- Runner: xUnit + Testcontainers (real PostgreSQL) — `@TC-AUTH-113`
- File: `src/backend/HRM.Tests/Integration/DataProtectionKeyPersistencePostgresTests.cs`
- Tests:
  - `Protect_PersistsKeyRing_ToPostgres_ISSUE247` — after a real `MfaSecretProtector.Protect(...)`, `data_protection_keys` has ≥ 1 row (0 with the old ephemeral ring).
  - `SecondInstance_DecryptsFirstInstancesCiphertext_ISSUE247` — a fully independent second `ServiceProvider` (simulated redeploy / second instance, same DB + `SetApplicationName("HRM")`) `Unprotect`s the first instance's ciphertext back to the exact secret. **This is the load-bearing cross-instance assertion that fails pre-fix.**
  - `DifferentApplicationName_CannotDecrypt_ISSUE247` — negative control: a provider with a different application name on the same key ring throws `CryptographicException`, proving `SetApplicationName("HRM")` is the load-bearing discriminator.
- Note: MUST run on real Postgres. The property is the read/write round-trip of the `data_protection_keys` table; an EF InMemory provider would let a fabricated "key persisted" assertion pass without exercising it (the BUG-068 "InMemory-masks-Postgres" class). Each fresh `ServiceProvider` is a genuinely separate instance (its own in-memory key-ring cache), so a green run proves the ring is loaded from Postgres, not shared in-process.

## 3. Preconditions
- A running PostgreSQL instance (Testcontainers `postgres:17-alpine`) with all EF migrations applied — including the migration that creates `data_protection_keys` (columns `id`, `friendly_name`, `xml`).
- The DI container is wired as in production: `AddDataProtection().PersistKeysToDbContext<AppDbContext>().SetApplicationName("HRM")` with `AppDbContext` resolvable.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Secret (plaintext) | `JBSWY3DPEHPK3PXP` | Base32 TOTP secret shape |
| Purpose | `HRM.MfaSecret.v1` | `MfaSecretProtector.Purpose` |
| Application name (match) | `HRM` | Cross-instance discriminator |
| Application name (mismatch) | `OTHER` | Negative-control provider |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Migrate a fresh Postgres container; confirm `data_protection_keys` is empty. | Table exists with 0 rows. |
| 2 | Build DI container (instance #1) wired like production; resolve `MfaSecretProtector`; `Protect("JBSWY3DPEHPK3PXP")`. | Ciphertext ≠ plaintext. |
| 3 | Read `data_protection_keys` via a fresh `AppDbContext`. | Row count ≥ 1 — the key ring was persisted to Postgres. |
| 4 | Build a fully independent DI container (instance #2), same DB + `SetApplicationName("HRM")`; `Unprotect` instance #1's ciphertext. | Recovered value equals the original secret exactly. |
| 5 | Build a provider with `SetApplicationName("OTHER")` on the same DB; attempt to `Unprotect` instance #1's ciphertext with the raw `IDataProtector`. | Throws `CryptographicException` — mismatched app name cannot decrypt. |

## 6. Postconditions
- The persisted key ring in `data_protection_keys` is unchanged and reusable by any instance sharing the DB + application name.
- No plaintext secret is written to any store during the test.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
