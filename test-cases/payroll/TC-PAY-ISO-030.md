---
id: TC-PAY-ISO-030
user_story: US-PAY-008
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PAY-ISO-030: Approval-workflow APIs reject missing/invalid/mismatched tenant context; no submit/approve/reject/history IDOR

## 1. Test Objective
Verify BR-8 / FR-1 / FR-7 / FR-8: every approval-workflow endpoint (submit, approve, reject, return, finalize, queue list, history read) requires a valid, resolved tenant context and rejects requests with missing, malformed, or mismatched tenant context. There is no IDOR: supplying a Tenant A payroll_run_id / workflow_instance_id / approval_history_id from a Tenant B session never acts on or reveals Tenant A's data.

## 2. Related Requirements
- User Story: US-PAY-008
- Acceptance Criteria: AC-1, AC-2, AC-3, AC-5
- Functional Requirements: FR-1, FR-7, FR-8
- Business Rules: BR-8

## 3. Preconditions
- Tenant A "acme" with a run in AwaitingApproval (run R_A, instance W_A) + history H_A.
- Tenant B "globex" user authenticated with `Payroll.Approve` + `Payroll.*.All`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Missing context | no subdomain / no X-Tenant-Subdomain | reject |
| Mismatched | globex token + `X-Tenant-Subdomain: acme` | reject |
| IDOR targets | R_A, W_A, H_A | A's ids in B's session |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call submit/approve/reject/return/finalize/queue/history with NO tenant context (no subdomain, no header). | Rejected -- tenant resolution fails (400/401); no action taken, no data returned. |
| 2 | Call the same endpoints with an invalid/unknown tenant subdomain. | Rejected -- tenant cannot be resolved; no fallback to another tenant. |
| 3 | As a globex-authenticated user, send `X-Tenant-Subdomain: acme` (token/context mismatch). | Rejected -- the resolved tenant must match the authenticated user's tenant; no acme action/read. |
| 4 | As globex, POST Approve / Reject / Return / Finalize targeting acme's run R_A / instance W_A. | 404/403 -- the run is outside globex scope; no acme status change; no maker-checker bypass; nothing leaked (IDOR blocked). |
| 5 | As globex, GET acme's approval history H_A by id. | 404 Not Found -- no acme actor/comment/IP data returned. |
| 6 | Confirm globex's own approval endpoints work with correct context. | All actions succeed within globex; isolation blocks only missing/mismatched/cross-tenant access. |

## 6. Postconditions
- Approval-workflow APIs are unusable without a valid matching tenant context; no cross-tenant IDOR on runs, instances, or history.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
