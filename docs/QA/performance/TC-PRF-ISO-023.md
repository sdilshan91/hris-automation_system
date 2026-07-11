---
id: TC-PRF-ISO-023
user_story: US-PRF-006
module: Performance Management
priority: critical
type: security
status: pass
created: 2026-06-16
---

# TC-PRF-ISO-023: Cross-tenant write block -- server-derived tenant_id on notes/sign-offs; foreign review/employee rejected (NFR-2)

## 1. Test Objective
Verify NFR-2 on the write path: when a manager adds meeting notes or a sign-off/dispute is recorded, the `tenant_id` stamped on review_meeting_notes and review_signoffs is DERIVED SERVER-SIDE from the resolved tenant context (TenantInterceptor), never trusted from the request body. A client-supplied `tenantId` in the payload is ignored. References to a foreign-tenant reviewId / employeeId are rejected so a row cannot be created that links across tenants.

## 2. Related Requirements
- User Story: US-PRF-006
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-1 (notes), FR-3 (sign-off), FR-7 (audit)
- Data Requirements: S7 (tenant_id stamped; append-only signoffs)

## 3. Preconditions
- Tenant "acme" (manager Maya, employee Liam, submitted manager review) and tenant "globex" (employee Ben, its own review) exist.
- Manager Maya authenticated in acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Injected tenantId | globex_id (in body) | must be ignored |
| Foreign reviewId | globex review | must be rejected |
| Foreign employeeId | globex Ben | must be rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Maya (acme), `POST .../reviews/{liamReviewId}/meeting-notes` with `tenantId: <globex_id>` injected in the body | 200/201 but the persisted notes row is stamped tenant_id=acme (server-derived via TenantInterceptor); the injected globex id is ignored (NFR-2). |
| 2 | As Maya, request sign-off / record a sign-off with `tenantId` injected in the body | The review_signoffs row is stamped tenant_id=acme; injected value ignored. |
| 3 | As Maya, attempt to add meeting notes referencing a globex reviewId (`POST .../reviews/{globex_reviewId}/meeting-notes`) | 404/403 -- the foreign review is invisible under acme's query filter; no cross-tenant row created. |
| 4 | As Maya, attempt to create a sign-off referencing a foreign employeeId (globex Ben) | Rejected -- the foreign employee FK is not resolvable within acme; no row created (NFR-2). |
| 5 | Verify persisted rows | Every review_meeting_notes / review_signoffs row created in this test carries tenant_id=acme; none reference globex entities. |

## 6. Postconditions
- All written notes/sign-off rows are tenant-stamped server-side to acme; body-injected tenant ids are ignored; foreign-tenant references are rejected.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
