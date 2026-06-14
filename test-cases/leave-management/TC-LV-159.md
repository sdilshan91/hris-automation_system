---
id: TC-LV-159
user_story: US-LV-008
module: Leave Management
priority: critical
type: performance
status: draft
created: 2026-06-14
---

# TC-LV-159: Year-end carry-forward job for 5,000 employees completes within 5 minutes (NFR-1)

## 1. Test Objective
Verify the performance SLA: `ProcessLeaveYearEndJob` processes 5,000 employees (across leave types) for a tenant's year-end and completes within 5 minutes, using batched processing and bounded memory (NFR-1).

## 2. Related Requirements
- User Story: US-LV-008
- Non-Functional Requirements: NFR-1
- Functional Requirements: FR-2

## 3. Preconditions
- Tenant "acme" seeded with 5,000 active employees, each with non-zero unused balances across 2-3 carry-forward-eligible leave types at year-end.
- Hangfire worker available; representative DB instance.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee count | 5,000 | NFR-1 target scale |
| Leave types/employee | 2-3 | realistic |
| SLA | <= 5 minutes | wall-clock |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Trigger `ProcessLeaveYearEndJob` for the tenant year-end and time it end-to-end | Total wall-clock runtime <= 5 minutes (NFR-1). |
| 2 | Verify correctness at scale | All eligible employees receive the correct `carry_forward`/`expired` ledger entries (sample-audit a subset against BR-1/BR-2). |
| 3 | Monitor memory/DB during the run | Memory stays bounded (batched, e.g. ~500/page like LeaveAccrualJob); no unbounded result-set load. |
| 4 | Re-run to confirm idempotency does not balloon runtime | A second run (no-op for processed rows) completes quickly without duplicating work (cross-ref TC-LV-153). |

## 6. Postconditions
- 5,000-employee year-end processing completes within SLA with correct, non-duplicated results.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
