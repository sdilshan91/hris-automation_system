---
id: TC-LV-028
user_story: US-LV-002
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-028: Per-employee override takes precedence over all rule-based entitlements

## 1. Test Objective
Verify that when an HR Officer sets a per-employee override for a specific leave type, the override takes absolute precedence over all rule-based entitlements (including the most specific rule) for that employee and leave type combination.

## 2. Related Requirements
- User Story: US-LV-002
- Acceptance Criteria: AC-3
- Functional Requirements: FR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with `Leave.Configure` permission is authenticated in the "acme" tenant context.
- Leave type "Annual Leave" exists and is active.
- Entitlement rule: "Annual Leave" + Department "Engineering" + Job Level "Senior" = 25 days.
- Employee "Jane Smith" is in Engineering, Senior Level -- she would normally get 25 days.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee | Jane Smith | Engineering, Senior Level |
| Leave Type | Annual Leave | Active leave type |
| Rule-Based Entitlement | 25.00 days | From dept + level rule |
| Override Entitlement | 30.00 days | Per-employee override |
| Override Reason | Executive bonus leave entitlement | Required text field |
| Leave Year | 2026 | Current leave year |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify Jane Smith's current entitlement for "Annual Leave" is 25 days (from rule) | Balance API returns 25.00 days. |
| 2 | Navigate to Jane Smith's employee profile, "Leave" tab | Leave tab loads showing current balances per leave type. |
| 3 | Click "Set Override" for "Annual Leave" | Override form appears with fields: Entitlement Days, Reason, Leave Year. |
| 4 | Enter Override Entitlement = 30.00, Reason = "Executive bonus leave entitlement", Leave Year = 2026 | Fields accept input; no validation errors. |
| 5 | Click "Save Override" | API call `POST /api/v1/leave-entitlement-overrides` returns 201 Created. |
| 6 | Verify response body contains `override_id`, `employee_id`, `leave_type_id`, `entitlement_days: 30.00`, `reason`, `leave_year: 2026` | All fields present and correct. |
| 7 | Trigger the Hangfire accrual recalculation for Jane Smith | Job executes. |
| 8 | Query Jane Smith's leave balance for "Annual Leave" | Balance shows 30.00 days (override, not 25 from rule). |
| 9 | Verify the `leave_ledger` entry reflects 30.00 days accrual | Ledger entry type "accrual" with 30.00 days. |
| 10 | Verify that another employee "John Doe" (same Engineering, Senior) still gets 25 days | John's balance is unaffected by Jane's override. |
| 11 | Verify override is visible in the UI with an indicator (e.g., badge/icon) showing it is overridden | Override indicator distinguishes this from rule-based entitlement. |
| 12 | Remove the override for Jane Smith | API call `DELETE /api/v1/leave-entitlement-overrides/{override_id}` returns 200 OK. |
| 13 | Trigger recalculation for Jane Smith | Job executes. |
| 14 | Verify Jane Smith's entitlement reverts to 25 days (rule-based) | Balance reverts to the rule-based 25.00 days. |

## 6. Postconditions
- `leave_entitlement_override` record created when override is set, removed when cleared.
- Override takes absolute precedence over all rules when present.
- Removing the override causes the employee to fall back to rule-based entitlement.
- Other employees are unaffected by a per-employee override.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
