---
id: TC-ONB-ISO-006
user_story: US-ONB-002
module: Onboarding / Offboarding
priority: critical
type: security
status: pass
created: 2026-06-17
---

# TC-ONB-ISO-006: Missing/invalid tenant context + cross-tenant ID injection on assignment endpoints -> 404

## 1. Test Objective
Verify FR-7 / tenant isolation on writes and cross-resource access: assignment endpoints reject requests with no resolvable tenant context, and a request that injects another tenant's employee_id, template_id, or checklist_instance_id is treated as not-found. The response is 404 (existence not disclosed), NOT 403, so a caller cannot probe for the existence of another tenant's resources.

## 2. Related Requirements
- User Story: US-ONB-002
- Acceptance Criteria: AC-2
- Functional Requirements: FR-7
- Cross-cutting: mandatory multi-tenant isolation

> PLATFORM NOTE: The platform resolves tenant from subdomain (`TenantResolutionMiddleware`) and stamps writes via `TenantInterceptor`; cross-tenant IDs fall outside the EF query filter and resolve to nothing -> 404 (not 403), so existence is not disclosed. Postgres RLS is deferred.

## 3. Preconditions
- Tenant A (`acme`) and Tenant B (`globex`) exist with valid scoped JWTs.
- Tenant B owns: employee `empB`, active template `tplB`, and an assigned checklist `clB`.
- Tenant A owns its own active template and employee.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| no-tenant request | request without resolvable tenant context | expect rejection (400/401) |
| cross-tenant employee_id | empB used by an acme caller | expect 404 |
| cross-tenant template_id | tplB used by an acme caller | expect 404 |
| cross-tenant checklist_instance_id | clB read/modified by an acme caller | expect 404 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Issue an assignment request with no resolvable tenant context | Rejected (400/401); no checklist created; never falls back to a default tenant. |
| 2 | As an acme user, `POST /api/v1/onboarding/assignments` with Tenant B's employee_id (empB) | 404 Not Found — empB is outside acme's query filter; no checklist created. |
| 3 | As an acme user, assign using Tenant B's template_id (tplB) | 404 Not Found — tplB invisible to acme; no checklist created. |
| 4 | As an acme user, `GET`/`PATCH` Tenant B's checklist_instance_id (clB) | 404 Not Found (not 403) — existence of clB is not disclosed. |
| 5 | Confirm Tenant B state unchanged | empB / tplB / clB are untouched after every cross-tenant attempt. |

## 6. Postconditions
- Cross-tenant IDs are indistinguishable from non-existent ones (404); no write occurs without a valid tenant context.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
