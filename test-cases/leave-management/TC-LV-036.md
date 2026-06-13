---
id: TC-LV-036
user_story: US-LV-002
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-036: Hangfire accrual job creates correct leave_ledger entries

## 1. Test Objective
Verify that the Hangfire recurring accrual job processes entitlement calculations correctly and creates the appropriate `leave_ledger` transaction entries (type = "accrual") for all eligible employees. Test across different accrual frequencies (monthly, quarterly, yearly, upfront).

## 2. Related Requirements
- User Story: US-LV-002
- Functional Requirements: FR-5
- Data Requirements: Section 7 (leave_ledger table)

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Leave types configured with different accrual frequencies:
  - "Annual Leave": yearly accrual, 20 days.
  - "Sick Leave": upfront accrual, 7 days.
  - "Casual Leave": monthly accrual, 12 days (1 day/month).
  - "Study Leave": quarterly accrual, 8 days (2 days/quarter).
- At least 3 active employees exist in "acme" tenant.
- Hangfire infrastructure is running.

## 4. Test Data
| Leave Type | Accrual Frequency | Annual Days | Per-Period Accrual | Ledger Entry Pattern |
|------------|-------------------|-------------|-------------------|---------------------|
| Annual Leave | Yearly | 20.00 | 20.00 (once) | 1 entry per year |
| Sick Leave | Upfront | 7.00 | 7.00 (once at year start) | 1 entry at year start |
| Casual Leave | Monthly | 12.00 | 1.00 (per month) | 12 entries per year |
| Study Leave | Quarterly | 8.00 | 2.00 (per quarter) | 4 entries per year |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Trigger the Hangfire accrual job for "acme" tenant (or wait for scheduled run at midnight tenant-local-time) | Job starts processing. |
| 2 | Verify job processes all active employees in the tenant | Job logs indicate N employees processed (matches active employee count). |
| 3 | For "Annual Leave" (yearly): check `leave_ledger` for Employee A | One "accrual" entry with amount = 20.00 for the current leave year. |
| 4 | For "Sick Leave" (upfront): check `leave_ledger` for Employee A | One "accrual" entry with full amount = 7.00 created at the start of the leave year. |
| 5 | For "Casual Leave" (monthly): check `leave_ledger` for Employee A after month boundary | One "accrual" entry with amount = 1.00 for the current month. |
| 6 | For "Study Leave" (quarterly): check `leave_ledger` for Employee A after quarter boundary | One "accrual" entry with amount = 2.00 for the current quarter. |
| 7 | Verify each `leave_ledger` entry has: `ledger_id`, `tenant_id`, `employee_id`, `leave_type_id`, `transaction_type = 'accrual'`, `amount`, `balance_after`, `effective_date`, `created_at` | All required columns populated correctly. |
| 8 | Verify idempotency: trigger the same accrual job again for the same period | No duplicate ledger entries are created. The job skips already-processed periods. |
| 9 | Verify that terminated/inactive employees are NOT processed by the accrual job | No new accrual entries for terminated employees. |
| 10 | Verify the computed balance = SUM of all ledger entries for the leave year | Balance from API matches the sum of accrual entries minus any "used" entries. |

## 6. Postconditions
- `leave_ledger` contains correct accrual entries for all active employees.
- Different accrual frequencies produce entries at the correct periodicity.
- The job is idempotent and does not create duplicates.
- Terminated/inactive employees are excluded from processing.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
