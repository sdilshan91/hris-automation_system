---
id: TC-LV-001
user_story: US-LV-001
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-001: Create a leave type with full configuration (happy path)

## 1. Test Objective
Verify that an HR Officer can create a new leave type with all configurable fields (name, code, color, annual entitlement, accrual frequency, carry-forward rules, probation eligibility, document rules, gender applicability, and advanced settings) and that it is saved correctly, scoped to the current tenant only.

## 2. Related Requirements
- User Story: US-LV-001
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-2
- Non-Functional Requirements: NFR-2, NFR-3
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active` and subdomain `acme.yourhrm.com`.
- A user with `Leave.Configure` permission is authenticated in the "acme" tenant context.
- No leave type named "Annual Leave" exists in the "acme" tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer with Leave.Configure | Authorized role |
| Name | Annual Leave | Required, varchar(100) |
| Code | AL | Required, varchar(20) |
| Color | #4CAF50 | Hex color tag |
| Description | Standard annual paid leave | Optional |
| Annual Entitlement | 20.00 | numeric(5,2) |
| Accrual Frequency | monthly | monthly/quarterly/yearly/upfront |
| Carry Forward Limit | 5.00 | numeric(5,2) |
| Carry Forward Expiry | 3 | months |
| Probation Eligible | false | boolean |
| Encashable | true | boolean |
| Max Encash Days | 10.00 | numeric(5,2) |
| Half Day Allowed | true | boolean |
| Hourly Allowed | false | boolean |
| Documents Required | false | boolean |
| Gender | all | all/male/female |
| Max Consecutive Days | 15 | int |
| Negative Balance Allowed | false | boolean |
| Display Order | 1 | int |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Leave Types configuration page at `https://acme.yourhrm.com/leave/types` | Leave types list page loads with table/card list (or empty state if no types exist). |
| 2 | Click the "Add Leave Type" button | A slide-over panel appears with smooth slide-in animation containing grouped fields: Basic Info, Entitlement Rules, Carry-Forward, Document Rules, Advanced. |
| 3 | Fill in all Basic Info fields: Name = "Annual Leave", Code = "AL", Color = "#4CAF50", Description = "Standard annual paid leave" | Fields accept input; no validation errors. Color picker shows green preview. |
| 4 | Fill in Entitlement Rules: Annual Entitlement = 20, Accrual Frequency = "Monthly", Probation Eligible = false | Fields accept input; accrual frequency is a dropdown. |
| 5 | Fill in Carry-Forward: Carry Forward Limit = 5, Carry Forward Expiry = 3 months | Fields accept numeric input. |
| 6 | Fill in Advanced: Encashable = true, Max Encash Days = 10, Half Day = true, Hourly = false, Gender = "All", Max Consecutive = 15, Negative Balance = false | Toggle switches and dropdowns work. |
| 7 | Leave Documents Required as false | Document threshold field is hidden/disabled. |
| 8 | Click "Save" button | Loading indicator appears; button is disabled to prevent double-submit. |
| 9 | Observe API call `POST /api/v1/leave-types` with full body | Request sent with `X-Tenant-Subdomain: acme` header. Response status is 201 Created. |
| 10 | Verify response body contains new leave type with `leave_type_id` (UUID), `tenant_id` matching acme's tenant ID, all submitted field values, `is_active: true`, `is_deleted: false` | All fields present and correct. |
| 11 | Verify the leave type appears in the leave types list | "Annual Leave" row is visible with color tag, name, code, entitlement, accrual frequency, status (Active). |
| 12 | Verify audit log entry exists for the create operation | Audit record contains `action: leave_type_created`, `entity_id`, `tenant_id`, `user_id`, and a JSON snapshot of the created values. |

## 6. Postconditions
- A new `leave_type` record exists with `tenant_id` set from session context.
- `is_active` is `true`, `is_deleted` is `false`.
- `created_at` and `created_by` are populated.
- An audit log entry of type `leave_type_created` has been recorded with after-snapshot.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
