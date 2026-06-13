---
id: TC-LV-060
user_story: US-LV-003
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-060: User without Leave.Apply permission is denied leave submission

## 1. Test Objective
Verify that an authenticated user who lacks the `Leave.Apply` permission cannot submit a leave request, and that the API returns 403 Forbidden without creating any record.

## 2. Related Requirements
- User Story: US-LV-003
- Preconditions: Section 2 (Leave.Apply required)

## 3. Preconditions
- Tenant "acme" is active.
- A user "Audit Viewer" is authenticated in "acme" but their role does NOT include `Leave.Apply` (e.g., a read-only auditor role).
- A valid active leave type and balance exist for some employee.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | Audit Viewer | No Leave.Apply permission |
| Leave Type | Annual Leave | Active |
| Start/End | 2026-07-06 / 2026-07-08 | Otherwise valid range |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as "Audit Viewer" (no Leave.Apply) | JWT issued; permission claims do not include Leave.Apply. |
| 2 | Attempt to navigate to the Leave Application page | The "Apply for Leave" action/route is not available, or the page denies access. |
| 3 | Force a direct `POST /api/v1/leaves` with a valid body and the user's token | Server returns 403 Forbidden. No `leave_request` is created. |
| 4 | Verify audit/log entry | The denied attempt is logged (authorization failure) with user and tenant context. |
| 5 | Grant Leave.Apply to the user and retry | Submission now succeeds (201 Created), confirming the gate is the missing permission, not another error. |

## 6. Postconditions
- No `leave_request` is created while the user lacks Leave.Apply.
- The authorization decision is enforced server-side regardless of client UI.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
