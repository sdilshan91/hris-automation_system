---
id: TC-LV-166
user_story: US-LV-008
module: Leave Management
priority: high
type: integration
status: draft
created: 2026-06-14
---

# TC-LV-166: Ledger entry shape -- carry_forward is positive (new year) and expired is negative (reduces old balance) (FR-6, Section 7)

## 1. Test Objective
Verify the exact shape of the ledger entries the year-end job writes (FR-6, Section 7): the `carry_forward` entry is a positive amount dated the first day of the new leave year; the `expired` entry is a negative amount dated the expiry/year-end date; both carry the correct `transaction_type`, employee, leave type, and leave year.

## 2. Related Requirements
- User Story: US-LV-008
- Functional Requirements: FR-6
- Data Requirements: Section 7 (leave_ledger transaction types; tracking table)

## 3. Preconditions
- Tenant "acme"; employee "Sam" with 8 unused Annual Leave days at 2026 year-end; `carry_forward_limit = 5`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| carry_forward entry | days = +5, date = 2027-01-01 | new leave year start |
| expired entry | days = -3, date = year-end/expiry | reduces old balance |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run the year-end job; read the `carry_forward` row | `transaction_type='carry_forward'`, days = +5 (positive), `transaction_date` = first day of new leave year (2027-01-01), correct `leave_year`/employee/type (FR-6). |
| 2 | Read the `expired` row | `transaction_type='expired'`, days = -3 (negative), `transaction_date` = expiry/year-end date (FR-6). |
| 3 | Verify the optional tracking row | A `leave_carry_forward_tracking` row exists with `from_year=2026`, `to_year=2027`, `carried_days=5`, `expiry_date=2027-03-31`, `expired_days` populated, and `status` set (Section 7). |
| 4 | Verify balance arithmetic | The signed amounts net correctly into the running balance: +5 raises the 2027 opening balance; -3 closed out the 2026 excess. |

## 6. Postconditions
- Ledger entries match the FR-6 / Section 7 spec for sign, date, type, and scope; tracking row is consistent.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
