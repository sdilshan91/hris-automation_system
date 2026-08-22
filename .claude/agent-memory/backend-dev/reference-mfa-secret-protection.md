---
name: reference-mfa-secret-protection
description: US-PLT-005 mfa_secret is DataProtection (not AES-GCM registry); why it must stay out of EncryptedFieldRegistry; legacy back-fill + IsProtected seam
metadata:
  type: reference
---

`users.mfa_secret` is the ONLY credential column in the schema and is protected by ASP.NET **DataProtection**
(`MfaSecretProtector`, purpose `HRM.MfaSecret.v1`, Postgres-persisted key ring — ISSUE-247), NOT the AES-GCM
`EncryptedFieldRegistry`.

**LANDMINE — never add `users.mfa_secret` to `EncryptedFieldRegistry`:** the registry sweep hard-codes
`WHERE tenant_id = {0}` (FieldEncryptionMaintenanceService) but `users` is GLOBAL (no `tenant_id`, `User` is
not `BaseEntity`) → a registry entry throws `42703 column "tenant_id" does not exist`. Also
`EncryptedFieldRegistryTests` forces registry membership the moment an `IEncryptedValueConverter` is attached,
so don't attach one either.

**Legacy-plaintext handling (US-PLT-005 Scope A):**
- `IFieldProtector.IsProtected(v)` = the detection counterpart to `Unprotect`'s legacy tolerance; both share one
  private `TryUnprotect` in `MfaSecretProtector` so they can't disagree (legacy = `CryptographicException`/`FormatException`).
- `DbInitializer.BackfillLegacyMfaSecretsAsync` — idempotent startup pass, EF change-tracking (opaque payload,
  no SQL prefix filter), NO tenant predicate, not gated to relational (mfa_secret has no value converter so even
  InMemory holds raw plaintext). Runs next to `EncryptSensitiveFieldsAtRestAsync`.
- Report visibility: `EncryptionKeyUsageReportDto.MfaSecretsLegacyPlaintext` — system-scope count, kept separate
  from the AES-GCM registry section. See [[reference-fresh-scope-rls-writes]] for the RLS-exempt global-table pattern.
