---
id: US-PLT-005
module: Platform / Cross-Cutting
priority: Must Have
persona: System / Security
status: draft
created: 2026-07-06
sprint: backlog
acceptance_criteria_count: 4
---

# US-PLT-005: Encryption-at-Rest for Sensitive PII & MFA Secrets (pgcrypto/KEK)  [EPIC STUB]

> **STUB** — goal + AC skeleton + dependencies only; full detail to be authored before build.
> **Reconciliation story (COMPLETION-PLAN Themes A/D).** Ref: `docs/hrm_technical_document_v4.0.md` §6
> (§2394 `pgcrypto` for sensitive PII; §1705 `mfa_secret` "encrypted at column level"; §2412 tenant-uploaded
> secrets envelope-encrypted). Today the **TOTP MFA secret is stored plaintext** (`AuthService.cs:973-974`,
> open HIGH bug) — a DB read defeats all MFA — and there is no column-level PII / tenant-secret encryption.
>
> **Note (judgment):** The spec's *second isolation layer* — Postgres **RLS enablement** — is already tracked
> as the in-progress **US-PLT-002** (Phase-4 switch-on). To avoid duplication, this story covers the
> **encryption-at-rest** half of Themes A/D and cross-references US-PLT-002 for RLS rather than re-creating it.

## 1. Description
**As the** platform / security owner,
**I want** sensitive columns (TOTP MFA secret, tenant-supplied SMTP/IdP credentials, and designated PII)
encrypted at rest with a KEK (pgcrypto or app-layer AES with envelope encryption),
**So that** a database-only compromise does not expose MFA secrets, tenant secrets, or regulated PII in plaintext.

## 2. Preconditions
- A key-management source for the KEK (vault/secret store/user-secrets) is available.
- The affected columns exist (mfa_secret, tenant SMTP creds per US-NTF-006, IdP cert per SSO).

## 3. Acceptance Criteria (SKELETON — expand before build)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A user enrolls in MFA | The TOTP secret is stored | It is encrypted at rest (not plaintext); decryption occurs only in-process for verification. Existing plaintext rows are migrated. |
| AC-2 | A tenant stores an SMTP/IdP secret | It is persisted | It is envelope-encrypted at rest and never returned to the client in full. |
| AC-3 | Designated PII columns are written | They are stored | They are encrypted at rest per the spec's `pgcrypto` guidance (§6). |
| AC-4 | Two tenants store secrets | Encryption/decryption runs | Key usage/decryption remains tenant-safe; no cross-tenant secret exposure. |

## 4–10. Requirements (TO AUTHOR)
- FR/BR/NFR/data/UI to be written: KEK provisioning + rotation, chosen crypto approach (pgcrypto vs app-layer AES), migration of existing plaintext MFA secrets, column selection for PII, log redaction, performance impact.

## 9. Dependencies
- **US-PLT-002** (RLS — the other half of the spec's two-layer isolation; tracked separately).
- US-AUTH-005 (MFA — the plaintext-secret fix), US-NTF-006 (tenant SMTP secrets), SSO stories (IdP cert).

## 11. Test Hints
- Verify DB-level read of mfa_secret yields ciphertext; verify MFA still verifies end-to-end; verify migration of pre-existing plaintext rows; verify tenant secrets never returned in full to the client.
