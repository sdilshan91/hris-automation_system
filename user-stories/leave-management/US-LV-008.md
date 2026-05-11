---
id: US-LV-008
module: Leave Management
priority: Should Have
persona: HR Officer / Tenant Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-LV-008: Leave Carry-Forward and Expiry Rules

## 1. Description
**As an** HR Officer or Tenant Admin,
**I want to** configure carry-forward limits and expiry rules for each leave type,
**So that** unused leave balances are handled according to company policy at the end of each leave year.

## 2. Preconditions
- Leave types have been configured (US-LV-001) with carry-forward fields.
- Leave entitlements and balances have been calculated (US-LV-002).
- A leave year cycle is defined for the tenant (calendar year or fiscal year).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | HR Officer configures Annual Leave with carry-forward limit of 5 days and expiry of 3 months | The leave year ends | A Hangfire job processes all employees: unused balance up to 5 days is carried forward to the new year as a `carry_forward` ledger entry; excess is forfeited as an `expired` ledger entry |
| AC-2 | An employee has 8 unused Annual Leave days at year-end with a 5-day carry-forward limit | The carry-forward job runs | 5 days are carried forward; 3 days are logged as expired; the employee's new year balance starts with 5 (carry-forward) + new entitlement |
| AC-3 | Carried-forward days have a 3-month expiry | 3 months pass into the new leave year | A Hangfire job expires unused carry-forward days by creating an `expired` ledger entry; Redis cache is invalidated |
| AC-4 | A leave type is configured with carry-forward limit = 0 (no carry-forward) | The leave year ends | All unused balance for that type is forfeited; `expired` ledger entries are created |
| AC-5 | HR Officer previews the carry-forward impact before year-end | They click "Preview Carry-Forward" | A report shows each employee's projected carry-forward and forfeiture amounts for review before the job executes |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: Carry-forward configuration fields on leave type: `carry_forward_limit (numeric)`, `carry_forward_expiry_months (int)`.
- FR-2: Hangfire recurring job: `ProcessLeaveYearEndJob` — runs on the last day of the leave year (or first day of new year); calculates carry-forward and expiry for each employee and leave type.
- FR-3: Hangfire recurring job: `ProcessCarryForwardExpiryJob` — runs monthly; checks for carried-forward balances past their expiry date and creates `expired` ledger entries.
- FR-4: Both jobs must restore tenant context and process each tenant independently.
- FR-5: Preview API: `GET /api/v1/leaves/carry-forward-preview?year={year}` — returns projected carry-forward and forfeiture per employee.
- FR-6: Ledger entries created: `carry_forward` (positive, new year) and `expired` (negative, reducing old balance).
- FR-7: Redis cache invalidation for all affected employees after job completion.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Year-end carry-forward job for 5,000 employees must complete within 5 minutes.
- NFR-2: All operations tenant-isolated; job iterates over active tenants and sets tenant context per iteration.
- NFR-3: Job must be idempotent — re-running for the same year/period must not create duplicate ledger entries.
- NFR-4: Job failures must be logged via Serilog and retried via Polly (max 3 retries with exponential backoff).

## 6. Business Rules
- BR-1: Carry-forward is calculated as: `MIN(unused_balance, carry_forward_limit)`.
- BR-2: Forfeiture is: `unused_balance - carry_forward_amount` (if > 0).
- BR-3: Carry-forward expiry is calculated from the first day of the new leave year.
- BR-4: If an employee uses carried-forward days before expiry, the FIFO principle applies (carry-forward days are consumed first).
- BR-5: Encashable leave types may allow encashment of forfeitable balance instead of expiry (see leave type config).
- BR-6: Carry-forward processing does not apply to leave types with unlimited balance (e.g., some unpaid leave types).

## 7. Data Requirements
- **Table:** `leave_ledger` — new transaction types: `carry_forward`, `expired`.
- **Ledger entry for carry-forward:** `transaction_type = 'carry_forward'`, `days = positive amount`, `transaction_date = first day of new leave year`.
- **Ledger entry for expiry:** `transaction_type = 'expired'`, `days = negative amount`, `transaction_date = expiry date or year-end date`.
- **Tracking:** `leave_carry_forward_tracking` table (optional) with `employee_id`, `leave_type_id`, `from_year`, `to_year`, `carried_days`, `expiry_date`, `expired_days`, `status`.

## 8. UI/UX Notes (Notion-like)
- Carry-forward rules configured inline within the leave type edit panel (US-LV-001).
- Preview report displayed as a Notion-like table with filters by department, employee, leave type.
- Carry-forward and expired amounts shown in the employee leave balance dashboard (US-LV-006) as separate line items.
- Color coding: Carry-forward = blue, Expired/Forfeited = gray strikethrough.
- Expiring-soon indicator on dashboard: "5 carry-forward days expiring on March 31" in amber.

## 9. Dependencies
- **US-LV-001**: Leave type carry-forward fields must be configurable.
- **US-LV-002**: Entitlements and ledger must be operational.
- **US-LV-006**: Dashboard must display carry-forward and expiry info.
- **Hangfire**: For scheduled year-end and monthly expiry jobs.
- **Redis**: For cache invalidation after processing.
- **Serilog + Polly**: For logging and retry on job failures.

## 10. Assumptions & Constraints
- The leave year boundary is configurable per tenant (default: January 1 - December 31).
- The carry-forward job is designed to run once per year-end; manual re-trigger is available to HR with a confirmation dialog.
- FIFO consumption of carry-forward days is tracked via the `leave_carry_forward_tracking` table.
- The preview report is read-only and does not lock or commit any data.

## 11. Test Hints
- Test carry-forward: Employee with 8 unused days, limit = 5; verify 5 carried, 3 expired in ledger.
- Test zero carry-forward: Limit = 0; verify all unused days are expired.
- Test expiry: Carry-forward 5 days with 3-month expiry; advance clock 3 months; run expiry job; verify days expired.
- Test FIFO: Employee uses 3 days after carry-forward of 5; verify carry-forward balance = 2, new entitlement untouched.
- Test idempotency: Run year-end job twice for the same year; verify no duplicate ledger entries.
- Test tenant isolation: Job processes Tenant A and Tenant B independently; verify no cross-contamination.
- Test preview: Generate preview report; verify it matches what the actual job would produce.
