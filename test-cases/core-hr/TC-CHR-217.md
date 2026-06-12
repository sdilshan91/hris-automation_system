---
id: TC-CHR-217
user_story: US-CHR-009
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-217: Change employee status from Active to Suspended -- status updated, history entry created, audit log recorded, portal access disabled (happy path)

## 1. Test Objective
Verify that an HR Officer can change an employee's status from "active" to "suspended" by providing a reason and effective date. Confirm the employee status updates, an employment history entry is recorded with the actor/reason/effective date, an audit log entry is created with before/after snapshots, and the employee's self-service portal access is disabled. This validates the primary success scenario end-to-end.

## 2. Related Requirements
- User Story: US-CHR-009
- Acceptance Criteria: AC-1, AC-2
- Functional Requirements: FR-3, FR-4, FR-5
- Non-Functional Requirements: NFR-5
- Business Rules: BR-1, BR-2

## 3. Preconditions
- Tenant "acme" exists with status `active` and subdomain `acme.yourhrm.com`.
- An HR Officer user (`hr-officer-uuid`) is authenticated in the "acme" tenant context.
- Employee "John Smith" (`emp-001-uuid`) exists in tenant "acme" with status `active`.
- The employee has a linked user account with portal access enabled.
- No prior status changes exist for this employee (status has been `active` since creation).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized to change status (BR-2) |
| Employee | John Smith (emp-001-uuid) | Current status: active |
| New Status | suspended | Valid transition from active (FR-2) |
| Reason | Pending disciplinary investigation | Required text (FR-3) |
| Effective Date | 2026-06-12 (today) | Required date (FR-3) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `https://acme.yourhrm.com/employees/emp-001-uuid` and verify the employee profile loads. | Profile displays with status badge showing "Active" in green (`bg-green-100 text-green-800`). A "Change Status" button is visible next to the badge. |
| 2 | Click "Change Status". | A modal/form appears showing the current status "Active" and a "New Status" dropdown. The dropdown lists only valid transitions: Suspended, Terminated, Inactive. "Probation" is NOT listed (active -> probation is not a valid transition per FR-2). |
| 3 | Select "Suspended" from the New Status dropdown. | "Suspended" is selected. The form shows a required "Reason" textarea and a required "Effective Date" date picker. |
| 4 | Enter "Pending disciplinary investigation" in the Reason field. | Text is accepted. |
| 5 | Set Effective Date to today (2026-06-12). | Date is selected. |
| 6 | Click "Confirm" / "Submit". | A confirmation dialog appears: "Are you sure you want to change John Smith's status from Active to Suspended? This will disable portal access and pause leave accrual." |
| 7 | Click "Confirm" on the confirmation dialog. | A success toast appears (e.g., "Status changed to Suspended"). The modal closes. |
| 8 | Verify the API request `POST /api/v1/tenant/employees/emp-001-uuid/status` (or equivalent endpoint) was sent. | Request body includes `{ newStatus: "suspended", reason: "Pending disciplinary investigation", effectiveDate: "2026-06-12" }`. Response status is 200 OK. |
| 9 | Verify the employee profile badge updates. | Badge now shows "Suspended" in gray (`bg-gray-100 text-gray-800`). |
| 10 | Query the employment history / status change log table. | A new record exists with: `employee_id` = emp-001-uuid, `change_type` = "status_change", `previous_value` = "active", `new_value` = "suspended", `reason` = "Pending disciplinary investigation", `effective_date` = 2026-06-12, `changed_by` = hr-officer-uuid, `changed_at` ~ now(), `tenant_id` = acme tenant UUID. |
| 11 | Query the audit log for this operation. | An audit entry exists with before snapshot (`{ status: "active" }`) and after snapshot (`{ status: "suspended" }`), including the actor and timestamp. |
| 12 | Verify portal access is disabled for the employee's linked user account. | The employee's user account `is_active` or equivalent portal access flag is set to `false`. Attempting to log in as the employee returns an appropriate error (e.g., "Your account has been suspended"). |

## 6. Postconditions
- Employee status is "suspended" in the database.
- Employment history contains one "status_change" entry.
- Audit log contains a before/after snapshot entry for the change.
- Employee's portal access is disabled.
- Leave accrual is paused (DEFERRED -- leave module not yet built; verify the hook/event is dispatched if implemented).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
