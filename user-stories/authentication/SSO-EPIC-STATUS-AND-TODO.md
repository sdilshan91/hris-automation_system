# Entra SSO Epic (CR-AUTH-001) — Reconciled Status + TODO

> **Maintained checklist** for the Microsoft Entra ID SSO epic (US-AUTH-011..016) + the
> related platform story US-PLT-002. Reconciled against the **actual code on `main`** on
> 2026-06-26 — `user-stories/STATUS.md` was significantly stale (it predated PR #112).
> Companion to the per-story files in this folder. Update the checkboxes as work lands.

## TL;DR
PR #112 (`feat(m365)`, commit `7ea6ce6`) landed a **working end-to-end SSO POC** that the tracker
never recorded: challenge → Microsoft → callback → `id_token` validation → fail-closed tenant
isolation → user match/JIT → app JWT, plus the frontend button + callback route. The genuine
**remaining build work is only three items: US-AUTH-012, US-AUTH-016, US-PLT-002.**

## Reconciliation — tracker vs. actual code on `main`

| Story | Old tracker | **Actual (verified in code)** | Where |
|---|---|---|---|
| US-AUTH-011 OIDC foundation | `[~]` Inc-2 *blocked* | **Built (POC)** — full challenge/callback/code-exchange/`id_token` validation (JWKS sig, `aud`, custom per-`tid` issuer, `exp`, `nonce`) | `EntraSsoService.cs` |
| US-AUTH-013 tid/domain isolation | `[ ]` pending | **Built (logic)** — `CheckIsolation` + `ValidateMicrosoftIssuer`, fail-closed | `EntraSsoService.cs:234`, `:315` |
| US-AUTH-014 match/link/JIT | `[ ]` pending | **Built** — `AuthService.SsoSignInAsync` (oid → email bootstrap → JIT) | `AuthService.cs:1403` |
| US-AUTH-015 "Sign in with Microsoft" | `[~]` Inc-1 | **Built** — login button + `sso-callback` component + auth-service/guard wiring | `login.component.*`, `sso-callback/` |
| **US-AUTH-012 per-tenant DB config** | `[ ]` pending | **PENDING (real gap)** — allow-list/JIT still in `appsettings` `EntraSsoOptions.TenantAllowList`; `TenantAuthSettings` has MFA/session/lockout but **no SSO fields** | `EntraSsoOptions.cs`, `TenantAuthSettingsResponse.cs` |
| **US-AUTH-016 enforcement/break-glass** | `[ ]` pending | **PENDING (real gap)** — no `enforcement_mode`, break-glass, or admin-consent flow | — |
| **US-PLT-002 RLS Phase 4** | `[~]` deferred | **PENDING (env-gated)** — Phases 1–3 inert; Phase 4 needs Docker/Postgres | `Persistence/Rls/README.md` |
| US-PLT-003 enum casing | `[~]` residual | **DONE** (PR #111) | — |

## Live test results (2026-06-26, running stack on :5000, tenant `techoneglobal`)

Functional probes against the **running** API (report-only; no code changed):

| AC | Scenario | Result |
|---|---|---|
| AC-1 | `GET /auth/sso/challenge?tenant=acme` | ✅ **PASS** — 302 to `login.microsoftonline.com/organizations/.../authorize` with `client_id`, `response_type=code`, fixed `redirect_uri`, scope `openid profile email`, signed `state`, `nonce`, PKCE `code_challenge`+`S256` |
| AC-2 | callback with missing `code`+`state` | ✅ **PASS** — 302 `login?sso_error=sso_failed`, no `Set-Cookie` |
| AC-5 | callback with tampered `state` | ✅ **PASS** — 302 `login?sso_error=sso_failed`, no token; logged `state could not be validated` (WRN) |
| AC-7 | callback with `error=access_denied` | ✅ **PASS** — 302 `login?sso_error=access_denied`, no token; logged `access_denied: user_cancelled` (WRN); lockout untouched |
| FR-8 | outcomes logged with `RequestId` | ✅ **PASS (Serilog)** — see caveat below |

**Config confirmed:** SSO is live-configured in dev (real `ClientId`, secret in user-secrets). Allow-list
set for `techoneglobal`: `AllowedTenantIds=[f9654482-…]`, `AllowedDomains=[techoneglobal.org]`, owner
`sachithra@techoneglobal.org` (email-matched → TenantOwner; other `@techoneglobal.org` → JIT DefaultRole).

### Not yet verified (need a real browser login or unit tests)
- [ ] **AC-3 / AC-4 happy path** — full Microsoft round-trip → app JWT + refresh issued → redirect to
  originating subdomain. Requires an interactive sign-in as `sachithra@techoneglobal.org`. Can't be
  done via curl (needs Microsoft auth + consent).
- [ ] **AC-6 `id_token` negatives** — wrong `aud`, expired `exp`, bad signature, `nonce` mismatch
  (4 cases). Needs crafted tokens → only reachable via unit tests or a mock IdP.
- [ ] **FR-8 caveat** — outcomes are written to the **Serilog** file, but confirm whether they are
  also persisted as structured **`audit_log` rows** with the named events
  (`sso_login_succeeded`/`sso_state_invalid`/`sso_token_validation_failed`/`sso_idp_error`). The story
  asks for audit-log records, not just app logs.

### Test coverage gaps (worth closing regardless of new features)
- [ ] **No backend xUnit/integration tests for `EntraSsoService`** — the security-critical `id_token`
  validation, custom issuer validator, and `CheckIsolation` fail-closed logic have **zero automated
  coverage**. PR #112's "real-boundary test suites" were markdown TCs + FE e2e fixtures, not unit tests.
- [ ] **No `sso-callback.component.spec.ts`** — the FE callback component is untested.
- [ ] Backend `dotnet test` not run this pass (running app holds DLL locks; would need the app stopped).

## Remaining build work — TODO

### ☐ US-AUTH-012 — Per-tenant SSO configuration (DB-backed)
*Productionizes the already-built US-AUTH-013 isolation guard by moving config out of `appsettings`.*
- [ ] Add SSO fields to `TenantAuthSettings` (DTOs/command/query/validator/controller already exist for MFA/session/lockout): `sso_enabled`, `allowed_entra_tenant_ids`, `allowed_email_domains`, `jit_enabled`, `jit_default_role`, `enforcement_mode`.
- [ ] EF migration (via `dotnet ef`, never hand-written) for the new columns.
- [ ] Rewire `EntraSsoService.CheckIsolation` to read the per-tenant allow-list from the **DB** (replacing/superseding `EntraSsoOptions.TenantAllowList`), still fail-closed.
- [ ] Gate SSO config + flow on `PlanFeatureFlags.Sso` (flag plumbing already exists in `SubscriptionPlanService`).
- [ ] Cache per-tenant settings + invalidate on write.
- [ ] Validators: `tid` GUID format, domain format, role exists.

### ☐ US-AUTH-016 — Enforcement, break-glass & admin-consent
- [ ] `enforcement_mode = sso_only` — block local password login for the tenant except break-glass.
- [ ] **Mandatory** break-glass admin path (cannot enable `sso_only` without a designated break-glass admin — never let a tenant lock itself out).
- [ ] Admin-consent onboarding: generate the Entra admin-consent URL, capture the directory `tid` on return, add it to the allow-list.
- [ ] Audit enforcement changes + break-glass logins.
- [ ] FE (extends US-AUTH-015): hide password form in `sso_only`, surface the break-glass link.

### ☐ US-PLT-002 — PostgreSQL RLS Phase 4 (defense-in-depth)
- [ ] **Env-gated** — needs Docker/Postgres; prod-risky. Stage to non-prod first.
- [ ] RLS migration (`ENABLE ROW LEVEL SECURITY` + `CREATE POLICY` per tenant-scoped table).
- [ ] Route system/seeding/migration paths to the privileged (BYPASSRLS) connection.
- [ ] Testcontainers suite proving isolation survives raw SQL + `IgnoreQueryFilters()`; verify no pool bleed.
- [ ] Flip `Rls:Enabled` only after the suite is green (dev first, never straight to prod).

## Decisions still needed (human)
1. **Promote SSO to prod-spec?** 011/013/014/015 are a working **POC** (config-driven). Building 012
   converts the appsettings allow-list to per-tenant DB config — required before the epic ships per
   BR-5 (011 stays feature-flagged off until enforced isolation is DB-backed).
2. **STATUS.md state for 011/013/014/015** — keep `[~]` (POC, awaiting 012 prod form) vs flip `[x]`.
   Currently left `[~]` with a note pointing here; revisit when 012 lands.
