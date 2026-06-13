---
id: TC-LV-048
user_story: US-LV-003
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-048: Submit a valid leave request (happy path) -- Pending status, confirmation, notification queued

## 1. Test Objective
Verify that an authenticated Employee with the `Leave.Apply` permission can apply for leave by selecting an active leave type, a valid start/end date range, and a reason, and that on submission the system creates a `leave_request` with status "Pending", queues a `leave-requested` notification to the employee's reporting manager, and shows a confirmation with the request ID.

## 2. Related Requirements
- User Story: US-LV-003
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-5, FR-6
- Business Rules: BR-6

## 3. Preconditions
- Tenant "acme" exists with status `active` and subdomain `acme.yourhrm.com`.
- Employee "Jane Smith" has an active employee record in "acme" and is authenticated.
- Jane has the `Leave.Apply` permission (default for Employee role).
- Leave type "Annual Leave" exists and is active (US-LV-001).
- Jane has a calculated Annual Leave balance of at least 3 days for the current leave year (US-LV-002).
- Jane has a reporting manager assigned (`manager_employee_id`) in Core HR.
- The notification service is available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Employee | Jane Smith | Has Leave.Apply, balance >= 3 days |
| Leave Type | Annual Leave | Active, full-day |
| Start Date | 2026-07-06 (Mon) | Future, within window, no holiday |
| End Date | 2026-07-08 (Wed) | Working days only |
| Is Half Day | false | Full-day request |
| Reason | "Family vacation" | Free text |
| Expected total_days | 3.00 | Mon-Wed, no weekend/holiday |
| Manager | Robert Lee | Jane's reporting manager |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Leave Application page at `https://acme.yourhrm.com/leaves/apply` | Leave application form loads with fields: leave type dropdown, start/end date pickers, half-day toggle, reason text area, optional attachment area. |
| 2 | Select Leave Type = "Annual Leave" | Current balance, requested days, and projected remaining balance display inline (real-time balance per FR-2). |
| 3 | Select Start Date = 2026-07-06 and End Date = 2026-07-08 | The "days calculated" chip updates to 3 days (weekends/holidays excluded). |
| 4 | Enter Reason = "Family vacation" | Field accepts input; no validation errors. |
| 5 | Click "Submit" | Loading indicator shown; submit button disabled to prevent double-submit. |
| 6 | Observe API call `POST /api/v1/leaves` | Request sent with `X-Tenant-Subdomain: acme` header and body containing `leaveTypeId`, `startDate`, `endDate`, `isHalfDay:false`, `reason`. Response is 201 Created. |
| 7 | Verify response body | Contains `leave_request_id` (UUID), `tenant_id` matching acme, `employee_id` matching Jane, `status: "Pending"`, `total_days: 3.00`, `requested_at` timestamp. |
| 8 | Verify the UI confirmation | A green toast/confirmation shows the request ID; the page transitions to the "My Leaves" list with the new request visible as "Pending". |
| 9 | Verify a `leave-requested` notification is queued to the manager | A notification event targeting Robert Lee (Jane's `manager_employee_id`) is enqueued via the notification service. |
| 10 | Verify the `leave_request` row in the database | Row exists with `status = 'Pending'`, `tenant_id` from session context, audit columns populated. |

## 6. Postconditions
- A new `leave_request` record exists with `status = 'Pending'` and `tenant_id` from session context.
- `requested_at`, `created_at`, and `created_by` are populated.
- A `leave-requested` notification has been queued to the employee's reporting manager.
- The leave balance is not yet decremented as approved (request is still Pending); pending days are reflected for overlap/balance projection.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
