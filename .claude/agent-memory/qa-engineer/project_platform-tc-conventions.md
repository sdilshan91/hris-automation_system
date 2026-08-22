---
name: platform-tc-conventions
description: US-PLT platform TC doc conventions — trait-first naming, code-bound automated regression TCs, RLS/encryption deviations
metadata:
  type: project
---

FIRST platform (US-PLT) TC docs, authored 2026-07-24 to close DF-plt-tc-structure (platform test cases
existed only as `[Trait("TC",…)]` in code with no doc-tracked coverage → /test-all couldn't see them).

**Naming is TRAIT-DRIVEN, not a running counter.** Docs are named after the EXACT trait string already in
code: `TC-PLT-002-RLS`, `TC-PLT-P34`, `TC-PLT-003/004/006/007`. Do NOT invent a `-NNN-XX` scheme — the id
must match the `[Trait("TC", "<id>")]` so the runner binding survives. Live in `docs/QA/platform/`.

**These are automated regression TCs** (`status: automated`) binding already-green xUnit arms — NOT
report-only /test-all passes. In TEST-STATUS.md mark the story `[x]` as a *traceability-gap-closure*
(US-PAY-013 precedent) with an explicit "Not a report-only /test-all pass — xUnit arms run on the
orchestrator's Testcontainers/HTTP-harness Postgres run (agent gate is Docker-less)" note.

**Two accuracy landmines flagged (both real, in the code):**
- `TC-PLT-P34` binds to **field-at-rest encryption Phase 3-4** tests (US-PLT-005), NOT RLS. The task that
  spawned this mis-labeled it "RLS phase-3/4"; the file headers say "P3-4: field-at-rest encryption". Bind to
  US-PLT-005.
- **US-PLT-002 FR-3 deviation:** the story mandated `SET LOCAL` + ambient per-request transaction
  (`TenantTransactionBehavior`). That was RETIRED (threw under EnableRetryOnFailure + nested own-tx handlers,
  ISSUE-277) → replaced by `TenantGucConnectionInterceptor` (session-scope tx-less `set_config` per connection
  open, re-set per open so no pool leak). Assert the SHIPPED mechanism.
- **No `TC-PLT-005` trait exists.** national_id back-fill parity (nominal "TC-PLT-005") is asserted inside
  TC-PLT-004 (`FieldEncryptionReencryptPostgresTests.Registry_backfill_encrypts_both_pip_and_national_id…`).
  Don't fabricate a doc for a trait with no test.

**US-PLT-005 is a STUB epic:** implemented encryption covers AC-3 (PII/compensation: pip/recommendation/
employees.national_id) + AC-4 (tenant-safe rotation). AC-1 (MFA/TOTP secret) + AC-2 (tenant SMTP/IdP secret)
are NOT built — don't claim coverage.

RLS proven on real Postgres against the NOBYPASSRLS `hrm_app` role vs BYPASSRLS `hrm_owner` (privileged
migrate/seed/system path). Root TRACEABILITY-MATRIX.md is now **LF/ASCII** (was CRLF; re-verify with
`grep -c $'\r'` before choosing append method) — plain Edit works now.

**US-PLT-006 (error tracking / GlitchTip) deviation, 2026-07-24:** NET-NEW story, 0% built (no `Sentry.*`
package). No trait to bind → these are FORWARD-LOOKING `draft` specs (phase-2 FE slice = `blocked`), each
naming its INTENDED `[Trait("TC","TC-PLT-NNN")]`; never `pass`/`automated` until real run. Trait-named scheme
has NO running counter, so continued the numeric suffix PAST the highest used (007) — TC-PLT-006/007 are
already US-PLT-005's, so US-PLT-006 = TC-PLT-008..014 + TC-PLT-ISO-001 (first platform ISO). Created a
`docs/QA/platform/TEST-MATRIX.md` (none existed). CRUX TC = TC-PLT-009 (PII scrub, AC-2) with an explicit
negative arm (national-ID/email sentinel ABSENT from serialized event). Scrub targets: request body,
Authorization, cookies/session, query params, email, national_id; `SendDefaultPii=false`. Tenant tags
`tenant_id`/`tenant_subdomain` from scoped ITenantContext (TenantResolutionMiddleware). Sentry.AspNetCore
6.6.x supports .NET 10; ADR-2026-07-08 Decision 1 = self-hosted GlitchTip (Datadog rejected on PII egress).
