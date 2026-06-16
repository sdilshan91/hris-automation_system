---
id: TC-PAY-ISO-029
user_story: US-PAY-008
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PAY-ISO-029: Tenant B can never see, list, or retrieve Tenant A's payroll approval workflow state or approval history (cross-tenant read isolation)

## 1. Test Objective
Verify BR-8 / FR-7 / FR-8: the payroll approval surface -- the run's workflow state (AwaitingApproval/Approved/Rejected), the "Pending Approvals" queue, and the `payroll_approval_history` rows -- is tenant-scoped by EF Core global query filters + TenantInterceptor. A user authenticated in Tenant B can NEVER see, enumerate, or retrieve Tenant A's pending approvals, workflow instances, or approval history, even by guessing a Tenant A payroll_run_id / workflow_instance_id / approval_history_id. (US-PAY-008 BR-8/FR-7 speak of tenant-scoped isolation; this platform enforces via EF query filters -- if Postgres RLS is later added on `payroll_approval_history`, extend Step 5 to assert session-level isolation as defense-in-depth.)

## 2. Related Requirements
- User Story: US-PAY-008
- Acceptance Criteria: AC-2
- Functional Requirements: FR-7, FR-8
- Business Rules: BR-8
- Data Requirements: S7 (tenant_id on payroll_approval_history, RLS-enforced)

## 3. Preconditions
- Tenant A "acme": a run in AwaitingApproval + a run with Submitted/Approved/Rejected history rows (instance W_A, history H_A).
- Tenant B "globex": its own runs/approvals; user authenticated in globex with `Payroll.Approve` + `Payroll.*.All`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | owns approvals |
| Tenant B | globex | attacker context |
| Targets | acme payroll_run_id, workflow_instance_id W_A, approval_history_id H_A | A's data |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As a globex user (`X-Tenant-Subdomain: globex`), open the Pending Approvals queue. | Only globex's pending runs returned; zero acme rows in the list, badge count, or filters (BR-8). |
| 2 | As globex, GET the approval state / review page for acme's payroll_run_id. | 404 Not Found (filtered out of globex scope); no acme summary, status, or approver leaked. |
| 3 | As globex, GET the approval history for acme's run / workflow instance W_A. | 404/empty -- no acme history rows (actor, comments, IP, timestamps) served (FR-7, BR-8). |
| 4 | As globex, GET the specific approval_history_id H_A directly. | 404 Not Found -- filtered out; no acme audit data leaked. |
| 5 | Direct DB cross-check: query `payroll_approval_history` for H_A with tenant context = globex. | EF global query filter returns zero rows (tenant_id=acme != globex). (If RLS is added, a globex DB session also returns zero.) |
| 6 | Confirm globex's own approvals + history remain fully accessible. | globex queue/review/history work normally -- isolation blocks only cross-tenant access (BR-8). |

## 6. Postconditions
- Tenant B cannot read or enumerate Tenant A's approval workflow state or history by any path.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
