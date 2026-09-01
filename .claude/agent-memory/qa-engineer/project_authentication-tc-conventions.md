---
name: authentication-tc-conventions
description: Authentication module TC numbering — RUNNING counter decoupled from US IDs (unlike Recruitment/Payroll suffix scheme)
metadata:
  type: project
---

Authentication (`docs/QA/authentication/`) uses a **single running counter** `TC-AUTH-{NNN}` that is
**decoupled from US IDs** — e.g. `TC-AUTH-012.md` belongs to US-AUTH-004, not US-AUTH-012. This is the
OPPOSITE of the Recruitment/Payroll per-story-suffix scheme ([[project_recruitment-tc-conventions]],
[[project_payroll-tc-conventions]]). A separate running counter `TC-AUTH-ISO-{NNN}` holds the dedicated
multi-tenant isolation TCs.

**Why:** the auth suite predates the suffix convention; the TEST-MATRIX maps US→TC explicitly, so the
filename number carries no US semantics.

**How to apply:** before adding auth TCs, find the max `TC-AUTH-{NNN}` and max `TC-AUTH-ISO-{NNN}`, then
continue both counters. As of 2026-09-02: functional counter reached **161**, ISO counter reached **008**. TC-AUTH-161 = US-AUTH-013 (the FIRST TC ever bound to that story) — an `status: automated` doc for the 17 already-green arms of `HRM.Tests/Unit/SsoIsolationGuardTests` (pure `SsoIsolationGuard.Evaluate`), authored to close the BUG-298/GAP-017 traceability gap. That file carries NO `[Trait("TC",…)]`, so the binding is by test NAME; TC-AUTH-142/143 remain the blocked callback-level arms.
US-AUTH-012 SSO = TC-AUTH-115..125 + TC-AUTH-ISO-005. US-AUTH-016 (SSO enforcement/break-glass/admin-consent
onboarding) = TC-AUTH-126..136 + TC-AUTH-ISO-006. US-AUTH-016 endpoints beyond auth-settings GET/PUT:
break-glass reuses POST /api/v1/auth/login; new onboarding endpoints GET /api/v1/tenant/sso/admin-consent-url
+ the fixed-redirect consent return (forward-looking names — backend not built as of 2026-06-21). Audit
actions: sso_enforcement_changed, break_glass_login, sso_admin_consent_completed/_failed. RLS deferred →
assert 404-not-403 on cross-tenant id injection.
Update THREE files: per-TC docs, `authentication/TEST-MATRIX.md` (US→TC matrix + AC coverage + type/category
distributions + API endpoint coverage + summary counts), and root `docs/QA/TRACEABILITY-MATRIX.md`
(forward table + TOTAL + backward table + a per-US "Detailed Requirements Traceability" section + coverage
summary). SSO config lives on `TenantAuthSettings` via existing `/api/v1/tenant/auth-settings` GET/PUT;
gate is `PlanFeatureFlags.Sso`. Note US-AUTH-011 (OIDC) had NO TCs yet when 012 was written.
