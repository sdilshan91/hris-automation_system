---
id: TC-PRF-ISO-019
user_story: US-PRF-005
module: Performance Management
priority: critical
type: security
status: fail
created: 2026-06-16
---

# TC-PRF-ISO-019: Cross-tenant write block — server-derived tenant_id (no body injection) + foreign reviewer/reviewee/cycle rejected (NFR-2)

## 1. Test Objective
Verify NFR-2 on writes: when creating 360 assignments or submitting feedback, the server derives `tenant_id` from the resolved tenant context — never from a client-supplied body field — and rejects any assignment/feedback that references a reviewer_id, reviewee_id, or cycle_id belonging to another tenant. A spoofed `tenant_id` in the payload is ignored; foreign foreign-keys are rejected.

## 2. Related Requirements
- User Story: US-PRF-005
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-1, FR-2, FR-4
- Data Requirements: S7 (feedback_360 tenant_id server-stamped)

## 3. Preconditions
- Tenants "acme" and "globex" each have employees, cycles, and 360 reviews.
- HR Officer authenticated in acme; globex employee/cycle IDs known to the tester.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Caller tenant | acme | server-derived |
| Spoofed body tenant_id | globex_tenant_id | must be ignored |
| Foreign refs | globex reviewer_id / reviewee_id / cycle_id | must be rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme HR, `POST .../performance/360/{acmeRevieweeId}/assignments` with a body that includes `tenant_id = globex_tenant_id` | The spoofed tenant_id is ignored; the assignment is stamped with acme's tenant_id (TenantInterceptor), not globex's. |
| 2 | Attempt to assign a globex employee as a reviewer for an acme reviewee (foreign reviewer_id) | Rejected — the reviewer_id resolves to a different tenant (or 404 under acme's query filter); no cross-tenant assignment row created. |
| 3 | Attempt to create a 360 assignment referencing a globex reviewee_id or globex cycle_id | Rejected — foreign reviewee/cycle not found within acme's tenant scope. |
| 4 | Submit feedback (`POST .../feedback/{assignmentId}/submit`) with a body `tenant_id` override | Override ignored; feedback row stamped acme; foreign assignmentId (globex) -> 404. |
| 5 | Verify persisted rows | All new feedback_360 rows carry acme's tenant_id; no row references a globex entity. |

## 6. Postconditions
- tenant_id is always server-derived; cross-tenant writes and foreign-key references are rejected.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
