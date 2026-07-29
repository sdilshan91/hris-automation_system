---
id: US-PLT-005
module: Platform / Cross-Cutting
priority: Must Have
persona: System / Security
status: done
created: 2026-07-06
updated: 2026-07-29
sprint: backlog
acceptance_criteria_count: 4
---

# US-PLT-005: Encryption-at-Rest for Sensitive PII & MFA Secrets

> **⚠ REWRITTEN 2026-07-29 — the original stub's premises were factually wrong.**
> This story was authored 2026-07-06 as a skeleton and never re-checked against the code. Both headline
> claims had been overtaken by work that shipped in the meantime, so the story was directing effort at
> problems that no longer existed. The delivered work is **Scope A** in §5.
>
> | Original claim | Reality (verified 2026-07-29) |
> |---|---|
> | "the **TOTP MFA secret is stored plaintext** (`AuthService.cs:973-974`, open HIGH bug) — a DB read defeats all MFA" | **FALSE.** `users.mfa_secret` is wrapped by `MfaSecretProtector` (ASP.NET Data Protection, purpose `HRM.MfaSecret.v1`) with a **Postgres-persisted key ring** (`AddDataProtection().PersistKeysToDbContext<AppDbContext>().SetApplicationName("HRM")`, `DependencyInjection.cs:126-134`) — the ISSUE-247 fix, PR #224. The cited line no longer holds the secret; the only write site is `AuthService.cs:1279` → `Protect(secret)`. Column widened to `varchar(512)` by migration `20260708055825_WidenMfaSecretForEncryption`. |
> | "tenant-supplied SMTP/IdP credentials … no column-level tenant-secret encryption" (AC-2) | **MOOT — nothing to encrypt.** `users.mfa_secret` is the *only* secret/credential-bearing column in the entire schema (verified against the live DB via `information_schema.columns`). No per-tenant SMTP credential column exists (`SmtpEmailSender` reads global `IConfiguration` only); no per-tenant IdP client secret exists — PR #444's per-tenant SSO columns are all *policy*, and `Tenant.cs:153` states the design outright: *"Client secrets/certs are PLATFORM-level, NOT stored here."* |
> | "no column-level PII encryption" (AC-3) | **Substantially delivered** by PR #273 (app-side AES-256-GCM via EF value converters — a deliberate decision **against** pgcrypto) and PR #377 (`national_id`). |
>
> **Decision:** AC-2 is closed as **N/A by design**, recorded in
> [ADR 2026-07-29 — tenant secrets are platform-level](../../vault/decisions/ADR-2026-07-29-tenant-secrets-are-platform-level.md).
> Reopening it would mean *building* a per-tenant SMTP-credentials feature first — US-NTF-006 scope, not
> encryption scope.

## 1. Description
**As the** platform / security owner,
**I want** sensitive columns (the TOTP MFA secret and designated PII) encrypted at rest under a managed key,
**So that** a database-only compromise does not expose MFA secrets or regulated PII in plaintext.

## 2. Preconditions
- A key source is available: `Encryption:Keys:{keyId}` (base64 32-byte AES) for the field-encryption ring, and the Data Protection key ring persisted in `data_protection_keys`.
- The affected columns exist.

## 3. Acceptance Criteria — status
| # | Given | When | Then | Status |
|---|-------|------|------|--------|
| AC-1 | A user enrolls in MFA | The TOTP secret is stored | It is encrypted at rest (not plaintext); decryption occurs only in-process for verification. **Existing plaintext rows are migrated.** | ✅ **DONE** — protection since PR #224; the *"existing plaintext rows are migrated"* clause was the **only genuinely unbuilt part**, delivered by Scope A below. |
| AC-2 | A tenant stores an SMTP/IdP secret | It is persisted | It is envelope-encrypted at rest and never returned to the client in full. | ⛔ **N/A by design** — no such column exists or will (see ADR). |
| AC-3 | Designated PII columns are written | They are stored | They are encrypted at rest. | ✅ **DONE** — 9 columns via app-side AES-256-GCM (PR #273: 3 `pip` + 5 `recommendation`; PR #377: `employees.national_id`). The "designated" set is `EncryptedFieldRegistry`; adding a column follows the documented pattern in `HRM.Infrastructure/Security/README.md`. |
| AC-4 | Two tenants store secrets | Encryption/decryption runs | Key usage/decryption remains tenant-safe; no cross-tenant secret exposure. | ✅ **DONE** — the re-encryption sweep is per-tenant and RLS-safe (`FieldEncryptionMaintenanceService`, PR #438); cross-tenant authz pinned by TC-PLT-007. |

## 4. Why the MFA secret is NOT on the AES-GCM key-ring
A reasonable reading of "unify the crypto" would move `mfa_secret` onto the same `IFieldEncryptor` ring as the
other 9 columns. **It deliberately is not**, for two hard reasons found during implementation:

1. **`users` has no `tenant_id`.** `FieldEncryptionMaintenanceService` builds its sweep SQL with a hard-coded
   `WHERE tenant_id = {0}`. A registry entry for `users.mfa_secret` throws `42703 column "tenant_id" does not exist`
   on every tenant iteration. Supporting it means teaching `EncryptedField` + the maintenance service about
   system-scope (untenanted) tables.
2. **The reverse drift guard forces registry membership.** `EncryptedFieldRegistryTests` asserts that the set of
   model properties carrying an `IEncryptedValueConverter` **exactly equals** the registry set — so attaching a
   converter to `User.MfaSecret` without a registry row fails CI, and adding the row triggers (1). No escape hatch.

Data Protection is also the better fit: it already provides key rotation over a persisted, shared ring, and the
secret is read on the login hot path where a per-read AES-GCM decrypt buys nothing extra.

## 5. Scope A — the delivered work (2026-07-29)
`Unprotect` tolerates legacy plaintext **by design** (a row written before encryption must still authenticate, or
enrolled users get locked out). The gap: nothing ever *upgraded* such a row and nothing *reported* one existed —
so a pre-encryption secret stayed plaintext indefinitely, invisibly.

| Change | File |
|---|---|
| `IFieldProtector.IsProtected(string)` — distinguishes a protector-produced value from legacy plaintext | `HRM.Application/Common/Interfaces/IFieldProtector.cs` |
| `MfaSecretProtector` routes both `Unprotect` and `IsProtected` through one private `TryUnprotect`, so they can never disagree about what "legacy" means | `HRM.Infrastructure/Security/MfaSecretProtector.cs` |
| `BackfillLegacyMfaSecretsAsync` — idempotent startup pass; re-`Protect`s only unprotected rows. No tenant predicate (`users` is global) | `HRM.Infrastructure/Persistence/DbInitializer.cs` |
| `MfaSecretsLegacyPlaintext` on the encryption key-usage report — system-scope count, kept clearly separate from the registry-driven section | `HRM.Infrastructure/Security/FieldEncryptionMaintenanceService.cs` |

**No schema change and no EF migration** — the column is already `varchar(512)`.

**Verification.** 14/14 green, and mutation-verified rather than trusted: removing the idempotency guard
(⇒ unconditional re-`Protect` ⇒ double-wrap) kills 2 arms including the real-Postgres one; `IsProtected`
hard-coded `true` kills 6; hard-coded `false` kills 4. The load-bearing assertion is that `Unprotect` of a healed
value returns the **original plaintext** — healing is lossless, so a healed user can still authenticate.

## 6. Dependencies
- **US-PLT-002** (RLS — the other half of the spec's two-layer isolation; tracked separately, not duplicated here).
- US-AUTH-005 (MFA). US-NTF-006 / SSO are listed in the original stub as sources of tenant secrets; per the ADR they are not.

## 7. Test Hints
- DB-level read of `mfa_secret` yields a Data Protection payload, not the base32 TOTP secret.
- MFA still verifies end-to-end after a back-fill run (the lossless-healing assertion).
- The back-fill is safe to run on every startup (idempotent, no double-wrap).
