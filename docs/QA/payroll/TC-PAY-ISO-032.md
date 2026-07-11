---
id: TC-PAY-ISO-032
user_story: US-PAY-008
module: Payroll
priority: high
type: security
status: blocked
created: 2026-06-16
exec_note: "2026-06-30: pending-approvals queue/badge-count cache + approval SignalR group isolation needs a submitted-for-approval payroll RUN in each iso tenant (runs are attendance-gated; no run data seeded). Underlying tenant-context binding is header-driven (BUG-003 confirmed on payroll config surface, see TC-PAY-ISO-004) so a leak is expected, but the specific approval-queue/SignalR arms are unrunnable here. Keep blocked: needs payroll-run data."
---

# TC-PAY-ISO-032: Pending-approvals queue / badge-count caches and the approval SignalR notification group are tenant-scoped (no cross-tenant row/count/notification leak)

## 1. Test Objective
Verify BR-8 / FR-7 / NFR-1: any caching of the "Pending Approvals" queue or its badge count is keyed per tenant (and per approver), and the real-time approval notification channel (SignalR group) is tenant-scoped -- so a submit/approve/reject in Tenant A never appears in Tenant B's queue, badge count, or live notifications. (CONDITIONAL: if the queue/count are computed on demand without a cache layer today, this asserts no shared/global cache key is used and queries are always tenant-filtered; the SignalR group-naming assertion still holds.)

## 2. Related Requirements
- User Story: US-PAY-008
- Acceptance Criteria: AC-1, AC-2
- Functional Requirements: FR-7, FR-8
- Non-Functional Requirements: NFR-1
- Business Rules: BR-8

## 3. Preconditions
- Tenant A "acme" + Tenant B "globex", each with an approver subscribed to live approval notifications.
- Cache layer (if any) and SignalR available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Cache key | `tenant:{tenantId}:payroll:approvals:pending:{approverId}` | tenant+approver scoped |
| SignalR group | per-tenant approval group | NFR-1 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme, submit a run for approval; inspect the cache key used for the pending-approvals queue/badge. | The key includes the tenant_id (and approver id); no shared/global key (FR-8). If no cache exists, the queue/count are computed with always-tenant-filtered queries. |
| 2 | As globex, load the Pending Approvals queue + badge count. | globex's count reflects ONLY globex pending runs; the acme submission in step 1 does not appear or increment globex's count (BR-8). |
| 3 | Submit/approve/reject in acme while a globex approver is connected to SignalR. | The acme approval notification is delivered ONLY to the acme tenant group; the globex approver receives NOTHING (FR-7, NFR-1, BR-8). |
| 4 | Submit/approve in globex; confirm acme approvers do not receive it. | Symmetric -- the globex notification reaches only the globex group; no acme leak. |
| 5 | Invalidate/refresh acme's pending-approvals cache after an approve (run leaves AwaitingApproval). | Only acme's cache entry is invalidated; globex's cached queue/count is untouched (FR-8). |
| 6 | Confirm each tenant's own queue badge + live notifications work normally. | Per-tenant queue, count, and SignalR notifications function; isolation blocks only cross-tenant leakage. |

## 6. Postconditions
- Approval queue/badge caches and SignalR notification groups are tenant(+approver)-scoped; no cross-tenant row, count, or notification leak.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
