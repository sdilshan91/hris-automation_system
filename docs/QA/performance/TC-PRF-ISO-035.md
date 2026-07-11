---
id: TC-PRF-ISO-035
user_story: US-PRF-009
module: Performance Management
priority: critical
type: security
status: pass
created: 2026-06-16
---

# TC-PRF-ISO-035: Cross-tenant write block -- server-derived tenant_id on progress updates/comments (no body injection) + foreign goal_id/employee_id rejected (NFR-2)

## 1. Test Objective
Verify NFR-2 on writes: when a progress update or comment is created, the persisted tenant_id is derived SERVER-SIDE from the resolved tenant context (via TenantInterceptor), never from a client-supplied body field; and any attempt to reference a foreign-tenant goal_id or employee_id in the write body is rejected. A client cannot stamp another tenant's id onto a new update/comment, nor attach an update to a goal that belongs to another tenant.

## 2. Related Requirements
- User Story: US-PRF-009
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-1 (create update), FR-8 (comment)
- Data Requirements: S7 (tenant_id auto-stamped)

## 3. Preconditions
- Tenant "acme" employee "Sam Lee" authenticated (acme context).
- Tenant "globex" has a known goalId (foreign) and employeeId.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Acting tenant | acme (Sam) | server-derived |
| Injected tenant_id | globex_id | must be ignored |
| Foreign goal_id | globex goalId | must be rejected |
| Foreign employee_id | globex employeeId | must be rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Sam (acme), POST an update with a body that injects `tenant_id = globex_id` | The injected value is IGNORED; the persisted update has tenant_id = acme (server-derived via TenantInterceptor). |
| 2 | Inspect the persisted update | tenant_id = acme; no globex-stamped row exists. |
| 3 | As Sam, POST an update referencing a foreign `goal_id` (a globex goal) | Rejected -- the foreign goal is not in acme scope (not found / 403); no update is created. |
| 4 | As Sam, POST an update/comment injecting a foreign `employee_id` (a globex employee) | Rejected/ignored -- the update binds to Sam's acme identity (server-resolved); no cross-tenant binding persists. |
| 5 | Add a comment injecting `tenant_id = globex_id` | The comment persists with tenant_id = acme; the injected id is ignored. |
| 6 | Verify at the DB level | All new goal_progress_updates / goal_comments rows carry tenant_id = acme; zero rows were written under globex from this acme session. |

## 6. Postconditions
- tenant_id is always server-derived; client body injection is ignored and foreign goal/employee references are rejected. No cross-tenant write occurred.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
