---
id: ADR-2026-07-29-tenant-secrets-are-platform-level
date: 2026-07-29
status: accepted
tags: [security, encryption, multi-tenancy, adr]
supersedes: none
related: [US-PLT-005, US-NTF-006, US-AUTH-012]
---

# ADR — Tenant-supplied secrets are platform-level; US-PLT-005 AC-2 is N/A

## Context

[[US-PLT-005]] AC-2 required that "a tenant stores an SMTP/IdP secret → it is envelope-encrypted at rest and
never returned to the client in full." Before implementing it, we checked what such a secret would actually be.

**There are none.** Verified 2026-07-29 against both the source and the live database:

- `information_schema.columns` over the whole `public` schema, filtered on
  `column_name ~* 'secret|credential|smtp'`, returns exactly **one** row: `users.mfa_secret`.
- **SMTP:** `SmtpEmailSender` reads `Smtp:Host|Port|Username|Password|FromAddress|FromName|UseStartTls` from
  `IConfiguration` only. There is no tenant email-settings entity and no `tenant_smtp_settings` table. The one
  per-tenant email field is `Tenant.PayrollFromEmail` — a sender *address*, not a credential.
- **IdP:** the Entra client secret lives in `EntraSsoOptions.ClientSecret`, bound from `Authentication:Entra`
  and sourced from user-secrets/environment. PR #444's per-tenant SSO columns (`sso_enabled`,
  `allowed_entra_tenant_ids`, `allowed_email_domains`, `jit_enabled`, `jit_default_role`, `enforcement_mode`,
  `break_glass_admin_user_ids`, `sso_onboarding_status`) are **all policy, no credential**.
- Every other credential-ish column is already a **one-way hash**, which must not be encrypted:
  `User.PasswordHash`, `User.PasswordResetTokenHash`, `RefreshToken.TokenHash`, `UserInvitation.TokenHash`,
  `ApplicantPortalToken.TokenHash`, `PasswordHistory.PasswordHash`.

The design is explicit, not accidental — `Tenant.cs:153`:

> `// Client secrets/certs are PLATFORM-level, NOT stored here.`

## Decision

**Tenant-supplied secrets are not stored per-tenant. US-PLT-005 AC-2 is closed as N/A by design.**

The platform holds one set of outbound credentials (one SMTP relay, one Entra app registration). Tenants
configure *policy* — which Entra tenant IDs and email domains are allowed, whether JIT is on, which sender
address to stamp on payroll email — never *credentials*.

## Consequences

**Positive.** No per-tenant secret store means no per-tenant key management, no envelope-encryption layer, no
per-tenant secret-rotation surface, and no class of "tenant A reads tenant B's SMTP password" vulnerability.
The blast radius of a DB compromise stays limited to `users.mfa_secret`, which is Data-Protection-wrapped.

**Negative / accepted.** A tenant cannot send mail from their own SMTP relay, and cannot bring their own Entra
app registration. Multi-tenant SSO works because the single app registration is multi-tenant and isolation is
enforced by the `tid`/domain allow-list per tenant (US-AUTH-012/013), not by separate app registrations.

**If this is ever revisited** — e.g. an enterprise customer demands their own relay — the work is to *build the
feature first*: a new tenant-scoped settings table (which per the standing rule ships its own dormant
`tenant_isolation` RLS policy in-migration), CRUD, admin UI, and a `SmtpEmailSender` that resolves per-tenant
with a global fallback. Encrypting its credential columns is then the easy part — the existing
`EncryptedFieldConverters` + `EncryptedFieldRegistry` pattern applies directly, since the table *would* have a
`tenant_id` and so avoids the `users`-table problem described in [[US-PLT-005]] §4. That is a net-new feature
story (US-NTF-006-adjacent), not encryption scope.

## Alternatives considered

1. **Build per-tenant SMTP settings now, so AC-2 has a surface.** Rejected: it inverts the reasoning — inventing
   a feature in order to satisfy an acceptance criterion written on the assumption the feature already existed.
   If per-tenant SMTP is worth building, it should be justified by a customer need and specced as its own story.
2. **Leave AC-2 open indefinitely.** Rejected: an AC that can never be satisfied by any amount of work on the
   story it belongs to is ledger noise. It made US-PLT-005 permanently un-closeable and misrepresented the
   platform's security posture as having an unencrypted-secrets gap.
