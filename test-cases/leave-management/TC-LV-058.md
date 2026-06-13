---
id: TC-LV-058
user_story: US-LV-003
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-058: Gender-restricted leave type is not visible or appliable to ineligible employees

## 1. Test Objective
Verify that gender-restricted leave types are not shown in the leave application dropdown for ineligible employees, and that the API rejects a direct submission for a leave type the employee's gender is not eligible for. (Test Hint: verify a male employee cannot see Maternity leave type.)

## 2. Related Requirements
- User Story: US-LV-003
- Business Rules: BR-4

## 3. Preconditions
- Tenant "acme" is active.
- Leave type "Maternity Leave" exists, active, `gender = Female` (per US-LV-001 seed).
- Leave type "Paternity Leave" exists, active, `gender = Male`.
- Employee "John Doe" has gender Male and is authenticated with `Leave.Apply`.
- Employee "Jane Smith" has gender Female and is authenticated with `Leave.Apply`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Maternity Leave | gender = Female | Restricted |
| Paternity Leave | gender = Male | Restricted |
| Employee John Doe | Male | Should not see Maternity |
| Employee Jane Smith | Female | Should not see Paternity |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as John Doe (Male); open the Leave Application page | The leave type dropdown lists Annual, Sick, Casual, Paternity, etc., but does NOT list Maternity Leave. |
| 2 | Inspect the `GET` leave-types-for-apply response for John | "Maternity Leave" is absent from the eligible-types payload. |
| 3 | Force a `POST /api/v1/leaves` for John with `leaveTypeId` = Maternity Leave | Server returns 403 Forbidden (or 400) -- "You are not eligible for this leave type." No request created. |
| 4 | Authenticate as Jane Smith (Female); open the Leave Application page | The dropdown lists Maternity Leave but does NOT list Paternity Leave. |
| 5 | Force a `POST /api/v1/leaves` for Jane with `leaveTypeId` = Paternity Leave | Server returns 403/400 -- ineligible. No request created. |
| 6 | Verify an "All"-gender leave type (e.g., Annual Leave) is visible and appliable to both | Both employees can see and apply for gender-neutral leave types. |

## 6. Postconditions
- No leave request is created for an ineligible gender-restricted leave type.
- Eligibility filtering is enforced both in the UI dropdown and server-side on submission.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
