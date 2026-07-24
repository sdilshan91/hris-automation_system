---
id: TC-PLT-013
user_story: US-PLT-006
module: Platform
priority: low
type: e2e
status: blocked
created: 2026-07-24
---

# TC-PLT-013 (PHASE-2, OPTIONAL): Angular @sentry/angular slice captures a client-side error with a backend-mirrored scrub and a subdomain-derived tenant tag

## 1. Test Objective
Verify AC-6 — the **optional / phase-2** frontend slice. When the Angular SPA is configured with
`@sentry/angular` and a frontend DSN, a client-side error is captured in GlitchTip with a `beforeSend` scrub
**mirroring the backend** (request/PII stripping) and a `tenant_id` / `tenant_subdomain` tag derived from the
`tenant/` subdomain signal. This TC is **deferred (phase-2)**: it is not required to close the backend value,
and its automation is gated on `@sentry/angular` major-version compatibility with Angular 20 (Confidence:
Medium per the feasibility study).

## 2. Related Requirements
- User Story: US-PLT-006
- Acceptance Criteria: AC-6 *(optional / phase-2)*
- Functional Requirement: FR-9 (`@sentry/angular`, DSN from `environment.ts`, mirrored scrub, tenant tag from subdomain signal)

## 3. Preconditions
- **DEFERRED — phase-2.** Requires: `@sentry/angular` added to `src/frontend` with a version verified against
  Angular 20; a frontend DSN in `environment.ts`; a `beforeSend` scrub mirroring the backend; and the
  `tenant/` subdomain signal available for tag derivation. None of this is built (FE SDK is 0% wired).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant subdomain | acme | drives the client-side tenant tag |
| Client error | thrown from a component/handler | e.g. `throw new Error('fe-boom')` |
| PII sentinel | email / national ID entered in a form | must be scrubbed client-side too |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With `@sentry/angular` configured under the `acme` subdomain, trigger a client-side error. | The error is captured and sent to GlitchTip via the browser SDK. |
| 2 | Inspect the captured client event tags. | `tenant_id` / `tenant_subdomain` derived from the `tenant/` subdomain signal (== `acme`). |
| 3 | Inspect the client event payload after `beforeSend`. | Request/PII data is stripped, mirroring the backend scrub — no email/national-ID sentinel present. |
| 4 | Confirm no third-party cloud egress. | Only the self-hosted GlitchTip DSN host receives the client event. |

## 6. Postconditions
- Client-side errors are tenant-attributed and PII-scrubbed in GlitchTip — once the phase-2 FE slice ships.

## 7. Test Category Tags
- [x] Happy path (intended: client error captured + tagged)
- [ ] Negative test
- [ ] Boundary test
- [x] Security test (intended: client-side PII scrub mirrors backend)
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test (intended: browser SDK behaviour across Chrome/Edge/Firefox/Safari)

## Automation & Traceability
- **Intended binding:** a Playwright arm tagged `@TC-PLT-013` that throws a client-side error behind the `acme`
  subdomain and asserts the captured browser event's tenant tag + scrub, once `@sentry/angular` is wired.
- **Status:** `blocked` — **phase-2 / optional**; FE SDK unwired and version-compatibility unverified. Kept
  tagged with its intended categories (security / cross-browser) per the coverage contract; it is not a gap,
  it is a deliberately deferred slice.
