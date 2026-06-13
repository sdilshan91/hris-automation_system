---
id: TC-LV-026
user_story: US-LV-002
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-026: Create entitlement rule mapping leave type to department and job level (happy path)

## 1. Test Objective
Verify that an HR Officer can create an entitlement rule mapping "Annual Leave" to "Engineering Department, Senior Level" with 25 days/year, and that employees matching the criteria receive 25 days upon the next accrual calculation.

## 2. Related Requirements
- User Story: US-LV-002
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-2
- Non-Functional Requirements: NFR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active` and subdomain `acme.yourhrm.com`.
- A user with `Leave.Configure` permission is authenticated in the "acme" tenant context.
- Leave type "Annual Leave" exists and is active (US-LV-001).
- Department "Engineering" exists with at least 2 employees at "Senior" job level.
- No existing entitlement rule for "Annual Leave" + "Engineering" + "Senior" in "acme" tenant.
- Hangfire infrastructure is running and available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer with Leave.Configure | Authorized role |
| Leave Type | Annual Leave | Pre-existing active type |
| Department | Engineering | Pre-existing department |
| Job Level | Senior | Pre-existing job level |
| Entitlement Days | 25.00 | numeric(5,2) |
| Effective From | 2026-01-01 | Start of current leave year |
| Is Active | true | Active rule |
| Employee A | Jane Smith | Engineering, Senior Level |
| Employee B | John Doe | Engineering, Senior Level |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Leave Entitlement configuration page at `https://acme.yourhrm.com/leave/entitlements` | Entitlement rules matrix page loads showing leave types as rows and departments/levels as columns (or empty state). |
| 2 | Click "Add Entitlement Rule" button | A form/dialog appears with fields: Leave Type (dropdown), Department (dropdown, optional), Job Level (dropdown, optional), Job Title (dropdown, optional), Employment Type (dropdown, optional), Entitlement Days (numeric), Effective From (date), Effective To (date, optional). |
| 3 | Select Leave Type = "Annual Leave", Department = "Engineering", Job Level = "Senior", Entitlement Days = 25.00, Effective From = 2026-01-01 | Fields accept input; no validation errors. |
| 4 | Click "Save" button | Loading indicator appears; button is disabled to prevent double-submit. |
| 5 | Observe API call `POST /api/v1/leave-entitlement-rules` with full body | Request sent with `X-Tenant-Subdomain: acme` header. Response status is 201 Created. |
| 6 | Verify response body contains new rule with `rule_id` (UUID), `tenant_id` matching acme's tenant ID, `leave_type_id`, `department_id`, `job_level_id`, `entitlement_days: 25.00`, `is_active: true` | All fields present and correct. |
| 7 | Verify the rule appears in the entitlement matrix at the intersection of "Annual Leave" row and "Engineering / Senior" column showing "25" | Rule is visible in the matrix UI. |
| 8 | Trigger the Hangfire accrual recalculation job (or wait for scheduled run) | Job executes successfully. |
| 9 | Query Employee A (Jane Smith, Engineering, Senior) leave balance for "Annual Leave" | Balance shows 25.00 days (or pro-rated if mid-year). |
| 10 | Query Employee B (John Doe, Engineering, Senior) leave balance for "Annual Leave" | Balance shows 25.00 days (or pro-rated if mid-year). |
| 11 | Verify `leave_ledger` entries created for both employees | Ledger entries of type "accrual" with amount 25.00 (or pro-rated) exist for both employees. |
| 12 | Verify audit log entry exists for the rule creation | Audit record contains action type, `rule_id`, `tenant_id`, `user_id`, and timestamp. |

## 6. Postconditions
- A new `leave_entitlement_rule` record exists with `tenant_id` set from session context.
- `is_active` is `true`.
- `created_at` and `created_by` are populated.
- Matching employees have updated leave balances via `leave_ledger` accrual entries.
- An audit log entry for the rule creation has been recorded.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
