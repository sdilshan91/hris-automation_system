---
id: TC-PAY-ISO-031
user_story: US-PAY-008
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PAY-ISO-031: Cross-tenant approval writes blocked; tenant_id + actor_user_id + IP server-derived; foreign workflow_instance_id / actor injection rejected

## 1. Test Objective
Verify BR-8 / FR-1 / FR-7 / FR-8: approval-action writes (submit/approve/reject/return/finalize -> `payroll_approval_history` rows + run status changes) are stamped with the SERVER-derived tenant_id (TenantInterceptor) and actor_user_id (authenticated principal) and request ip_address -- never trusting a client-supplied tenant_id, actor_user_id, or IP in the body. A request that injects a foreign tenant_id, foreign actor, or a foreign workflow_instance_id / payroll_run_id is rejected or scoped to the caller's own tenant, never writing into another tenant.

## 2. Related Requirements
- User Story: US-PAY-008
- Acceptance Criteria: AC-1, AC-2, AC-3, AC-5
- Functional Requirements: FR-1, FR-7, FR-8
- Non-Functional Requirements: NFR-5
- Business Rules: BR-5, BR-8

## 3. Preconditions
- Tenant A "acme" (run R_A AwaitingApproval, instance W_A) and Tenant B "globex" (run R_B, instance W_B).
- globex user authenticated with `Payroll.Approve`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Injected tenant_id | acme's id in request body | must be ignored |
| Injected actor_user_id | an acme user id | must be ignored (server uses principal) |
| Foreign refs | acme R_A / W_A from globex session | rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As globex, submit/approve a globex run but inject `tenant_id=acme` in the body. | The injected tenant_id is ignored; the history row + status change are stamped tenant_id=globex (server-derived); nothing is written under acme (FR-8). |
| 2 | As globex, approve a globex run but inject `actor_user_id` = an acme user. | The injected actor is ignored; actor_user_id is the authenticated globex principal; the audit attributes the action correctly (FR-7, NFR-5). |
| 3 | As globex, POST an approval action whose workflow_instance_id = acme's W_A (or payroll_run_id = acme's R_A). | Rejected (404/403/422) -- the foreign instance/run is outside globex scope; no acme history row is created; no acme status change. |
| 4 | As globex, attempt to spoof ip_address in the body. | Ignored -- ip_address is derived from the request connection, not the body (FR-7). |
| 5 | After all attempts, read acme's run R_A status + history H_A as an acme user. | Unchanged -- no globex-originated write reached acme's run/history (BR-8, FR-8). |
| 6 | Confirm globex's own legitimate approval write succeeds and is correctly tenant-stamped. | The globex action persists with tenant_id=globex, correct actor + server IP; isolation blocks only cross-tenant/forged writes. |

## 6. Postconditions
- All approval writes are server-stamped to the caller's tenant + principal + IP; no cross-tenant write, actor spoof, or foreign-ref injection succeeds.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
