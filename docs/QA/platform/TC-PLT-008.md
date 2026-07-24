---
id: TC-PLT-008
user_story: US-PLT-006
module: Platform
priority: high
type: functional
status: draft
created: 2026-07-24
---

# TC-PLT-008: Unhandled exception is captured in GlitchTip with stack trace, release/version, and tenant tags

## 1. Test Objective
Verify AC-1: when a `GlitchTip:Dsn` is configured and an unhandled/thrown exception occurs during a request,
the exception is captured in GlitchTip (Sentry-API-compatible) with its **full stack trace** and the
application **release/version**, and is **tagged** with `tenant_id` and `tenant_subdomain` (sourced from the
scoped `ITenantContext` via `TenantResolutionMiddleware`) so issues are filterable per tenant. This is the
core "an error reaches the tracker, correctly attributed" happy path.

## 2. Related Requirements
- User Story: US-PLT-006
- Acceptance Criteria: AC-1
- Functional Requirements: FR-3 (`UseSentry`), FR-5 (tenant tags from `ITenantContext`), FR-6 (release/version)
- Business Rule: BR-5 (every captured issue carries `tenant_id` + `tenant_subdomain`)

## 3. Preconditions
- The API is running with a **valid** `GlitchTip:Dsn` supplied via user-secrets/env (a self-hosted GlitchTip
  or a Sentry-protocol test transport/spy the test can inspect).
- Serilog `TenantId`/`TenantSubdomain`/`RequestId` enrichment is active; a tenant subdomain (`acme`) resolves
  to a known `TenantId`.
- A deliberate throwing endpoint (or an existing endpoint forced to throw) is reachable behind the tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant subdomain | acme | resolves to a seeded TenantId |
| Release/version | assembly informational version | attached to every event |
| Exception | `InvalidOperationException("boom-US-PLT-006")` | deliberate, unique marker string |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Issue a request to the throwing endpoint behind the `acme` subdomain. | Request fails (500/handled error); the exception propagates to the Sentry ASP.NET Core integration. |
| 2 | Inspect the captured event on the transport/spy (or the GlitchTip issue). | Exactly one event captured for `boom-US-PLT-006` with `Level == Error`. |
| 3 | Inspect the event's exception payload. | Contains the exception **type**, message, and a non-empty **stack trace** with frames. |
| 4 | Inspect the event `Release`. | Equals the application release/version (FR-6). |
| 5 | Inspect the event tags. | `tenant_id` == the resolved TenantId for `acme` and `tenant_subdomain` == `acme` (FR-5, BR-5). |

## 6. Postconditions
- One tenant-attributed, release-stamped, stack-traced issue exists in GlitchTip; the Serilog file log for the
  same `RequestId` still records the exception (see TC-PLT-011).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation (single-tenant here; cross-tenant attribution is TC-PLT-ISO-001)
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Intended binding:** an xUnit arm carrying `[Trait("TC", "TC-PLT-008")]` that drives the throwing endpoint
  through the real ASP.NET Core Sentry integration against an in-process capture transport and asserts the
  event's stack trace, `Release`, and `tenant_id`/`tenant_subdomain` tags.
- **Status:** `draft` — the Sentry/GlitchTip SDK layer is unwired (0% built per the feasibility study); this
  spec is forward-looking and flips to `automated` when the binding arm lands. Do not mark `pass` without a run.
