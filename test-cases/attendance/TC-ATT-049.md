---
id: TC-ATT-049
user_story: US-ATT-004
module: Attendance
priority: high
type: performance
status: draft
created: 2026-06-14
---

# TC-ATT-049: Approval queue loads within 2 seconds at P95 for 50 pending requests (performance)

## 1. Test Objective
Verify NFR-1: the approval queue page/endpoint returns within 2 seconds at the 95th percentile when the manager has up to 50 PENDING regularization requests across their direct reports, including the joins needed to show employee name, date, requested times, reason, and submission date.

## 2. Related Requirements
- User Story: US-ATT-004
- Non-Functional: NFR-1 (queue loads < 2s P95 for up to 50 pending requests)
- Functional Requirements: FR-1 (queue list), FR-7 (direct-report scoping)

## 3. Preconditions
- Tenant "acme", manager "Dana Wells" authenticated with `Attendance.Approve.Team`.
- Dana has 50 PENDING regularizations across her direct reports (seeded), plus decided/other-team requests as noise.
- Representative dataset size for the tenant (e.g. tens of thousands of attendance/regularization rows) so query plans are realistic.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Pending queue size | 50 | NFR-1 target volume |
| Concurrency | representative manager load | warm cache and cold cache both measured |
| Metric | P95 end-to-end | <= 2000ms |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Drive >= 200 `GET .../approval-queue` calls as Dana under representative load | Collect end-to-end latency distribution. |
| 2 | Evaluate P95 | P95 latency <= 2000ms with 50 pending rows and their joins. |
| 3 | Evaluate filtered queries (by employee/date) | Filtered queue queries also stay within the 2s P95 budget. |
| 4 | Confirm scope correctness under load | Each response still contains only Dana's direct-report PENDING rows (no scope leakage when fast-pathing). |
| 5 | Confirm supporting indexes | Queries use indexes on (tenant_id, manager scope, status) / regularization date; no full scans on the regularization or employee tables at this volume. |

## 6. Postconditions
- The approval queue meets the 2s P95 budget at 50 pending requests while preserving correct scoping.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
