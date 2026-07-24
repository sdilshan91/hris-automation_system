---
id: TC-PLT-ISO-001
user_story: US-PLT-006
module: Platform
priority: high
type: security
status: draft
created: 2026-07-24
---

# TC-PLT-ISO-001: Two tenants' captured errors are each tagged with their OWN tenant_id/tenant_subdomain and are never cross-attributed

## 1. Test Objective
Verify the multi-tenant isolation guarantee for error tracking (AC-1 tenant tags + BR-5, under concurrent
tenants). Exceptions thrown behind **tenant A** must be captured with tenant A's `tenant_id`/`tenant_subdomain`
tags, and exceptions behind **tenant B** with tenant B's — with **no cross-attribution** (A's error never
carries B's tags and vice-versa), even when requests interleave. The tags derive from the per-request scoped
`ITenantContext` populated by `TenantResolutionMiddleware`, so a scope-bleed would surface here as a
mis-tagged event.

## 2. Related Requirements
- User Story: US-PLT-006
- Acceptance Criteria: AC-1 (tenant tags), plus multi-tenant isolation (Critical Rule #1)
- Functional Requirement: FR-5 (tenant tags from the scoped `ITenantContext`)
- Business Rule: BR-5 (every issue tenant-attributable)

## 3. Preconditions
- API running with a valid `GlitchTip:Dsn` and a capture transport/spy that records each event's tags.
- Two seeded tenants: `acme` (TenantId A) and `globex` (TenantId B), each with a resolvable subdomain.
- A throwing endpoint reachable behind each subdomain, producing a uniquely-identifiable exception per tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | subdomain `acme` → TenantId A | marker `boom-acme` |
| Tenant B | subdomain `globex` → TenantId B | marker `boom-globex` |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Throw `boom-acme` behind the `acme` subdomain. | Captured event tags: `tenant_id == A`, `tenant_subdomain == acme`. |
| 2 | Throw `boom-globex` behind the `globex` subdomain. | Captured event tags: `tenant_id == B`, `tenant_subdomain == globex`. |
| 3 | Interleave requests from both tenants (concurrent/alternating). | Each captured event carries ONLY its own tenant's tags; no event mixes A's id with B's subdomain or vice-versa. |
| 4 | Assert the `boom-acme` event never carries TenantId B / `globex`, and the `boom-globex` event never carries TenantId A / `acme`. | No cross-attribution — tenant tagging is strictly per-request-scoped (FR-5, BR-5). |
| 5 | Confirm neither event's payload leaks the OTHER tenant's identity anywhere (tags/extra/context). | Tenant isolation holds end-to-end in captured telemetry. |

## 6. Postconditions
- In GlitchTip, filtering by `tenant_id == A` returns only tenant A's issues and never tenant B's — per-tenant
  triage is correct and leakage-free.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test (cross-tenant attribution leakage)
- [x] Multi-tenant isolation (two tenants; no cross-attribution of captured errors)
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Intended binding:** an xUnit two-tenant integration arm `[Trait("TC", "TC-PLT-ISO-001")]` that throws
  behind two subdomains (interleaved) against a capture transport and asserts each event's tenant tags match
  its originating tenant and never the other's.
- **Status:** `draft` — SDK layer unwired; forward-looking. Flips to `automated` when the arm lands. This is
  the mandatory multi-tenant isolation TC for the module.
