---
id: TC-LV-219
user_story: US-LV-011
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-219: Compulsory leave (company shutdown) bulk-assign — deduct from balance first, LOP only if insufficient (FR-6 / BR-4)

## 1. Test Objective
Verify the compulsory-leave bulk assignment (FR-6, BR-4): HR bulk-assigns a specific leave type for all employees for specific shutdown dates; for each employee the days are deducted from their relevant leave balance FIRST, and only the shortfall becomes LOP when the balance is insufficient.

## 2. Related Requirements
- User Story: US-LV-011
- Functional Requirements: FR-6
- Business Rules: BR-4
- Data: `compulsory_leave` table (§7); `lop_source = compulsory`
- Test Hint §11 (compulsory leave)

## 3. Preconditions
- Tenant "acme"; LOP type + the chosen shutdown leave type (e.g. Annual) exist.
- Employees: "Ada" balance 3 (>= shutdown), "Ben" balance 1 (< shutdown).
- HR Officer "Asha" authenticated with `Leave.Manage`/`HR.Officer`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Shutdown dates | 2 working days | applies to all |
| Shutdown type | Annual | balance deducted first |
| Ada balance | 3 | sufficient |
| Ben balance | 1 | 1 short |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Asha bulk-assigns the 2-day shutdown to all employees (Annual) | A `compulsory_leave` record (or per-employee requests) is created for the dates, tenant acme. |
| 2 | Inspect Ada (balance 3) | 2 days deducted from Ada's Annual balance (now 1); NO LOP entry for Ada (balance was sufficient). |
| 3 | Inspect Ben (balance 1) | 1 day deducted from Ben's Annual balance (now 0); the remaining 1 day is recorded as LOP (`is_lop = true`, `lop_source = compulsory`). |
| 4 | Verify lop-summary | Ben's lop-summary for the period shows 1 LOP day; Ada's shows 0. |
| 5 | Verify notification + audit | Affected employees notified (BR-6 seam); bulk action audited (NFR-4). |

## 6. Postconditions
- Shutdown days deduct balance first and spill to LOP only on shortfall; LOP source = compulsory.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
