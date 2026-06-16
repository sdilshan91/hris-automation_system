---
id: TC-PRF-ISO-015
user_story: US-PRF-004
module: Performance Management
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PRF-ISO-015: Cross-tenant write block — server-derived tenant_id + foreign department/employee/rating-scale rejected (NFR-2)

## 1. Test Objective
Verify NFR-2: cycle writes (create/edit/clone/transition/cancel) always stamp tenant_id from the server-resolved tenant context, never from the request body; and a write that references a foreign department, employee, or rating scale (belonging to another tenant) is rejected rather than silently associated.

## 2. Related Requirements
- User Story: US-PRF-004
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-3, FR-6
- Data Requirements: S7

## 3. Preconditions
- Tenant "acme" and "globex" both active.
- acme has Engineering department, employee Asha, and rating scale 1-5.
- globex has its own department, employee Ben, and rating scale.
- HR Officer Maya authenticated in acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Auth context | acme | Maya |
| Injected tenant_id | globex_id | in request body — must be ignored |
| Foreign department | globex department id | must be rejected for scope |
| Foreign employee | Ben (globex) | must be rejected as participant |
| Foreign rating scale | globex scale id | must be rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Maya (acme), `POST .../cycles` with `tenant_id=globex_id` injected in the body | The injected tenant_id is IGNORED; the persisted cycle is tenant_id=acme (server-derived via TenantInterceptor). |
| 2 | Create/edit a cycle scoping it to a globex department id | Rejected (400/404) — the department is not resolvable under acme; no cycle scoped to a foreign department is created. |
| 3 | Attempt to add globex employee Ben as a participant of an acme cycle | Rejected — Ben is not an acme employee; not added (foreign employee_id blocked). |
| 4 | Select a globex rating-scale id when creating an acme cycle | Rejected — the rating scale is not resolvable under acme; 400. |
| 5 | Attempt to transition/cancel/clone a globex cycle while authenticated in acme | 404/403 — the globex cycle is invisible/unwritable from acme; no state change. |

## 6. Postconditions
- All cycle writes carry the server-derived acme tenant_id; no cross-tenant foreign-key association is possible.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
