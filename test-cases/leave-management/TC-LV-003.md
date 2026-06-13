---
id: TC-LV-003
user_story: US-LV-001
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-003: Duplicate leave type name rejected (case-insensitive)

## 1. Test Objective
Verify that creating a leave type with a name that already exists within the same tenant is rejected with the exact validation error "A leave type with this name already exists", and that the check is case-insensitive (e.g., "Annual Leave" vs "annual leave").

## 2. Related Requirements
- User Story: US-LV-001
- Acceptance Criteria: AC-3
- Functional Requirements: FR-1
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with an active leave type named "Annual Leave".
- A user with `Leave.Configure` permission is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Existing Name | Annual Leave | Already exists in acme |
| Attempt 1 | Annual Leave | Exact duplicate |
| Attempt 2 | annual leave | Lowercase variant |
| Attempt 3 | ANNUAL LEAVE | Uppercase variant |
| Attempt 4 | Annual  Leave | Extra space (different string) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Leave Types page and click "Add Leave Type" | Slide-over panel appears with empty form. |
| 2 | Enter "Annual Leave" (exact match) in Name field, fill required fields, click Save | API returns 409 Conflict or 422 Unprocessable Entity. Error message displayed: "A leave type with this name already exists". No record created. |
| 3 | Clear and enter "annual leave" (all lowercase) in Name field, click Save | Same validation error displayed: "A leave type with this name already exists". Case-insensitive check blocks creation. |
| 4 | Clear and enter "ANNUAL LEAVE" (all uppercase) in Name field, click Save | Same validation error. Case-insensitive uniqueness enforced. |
| 5 | Verify no duplicate records exist in the database | `SELECT count(*) FROM leave_type WHERE tenant_id = acme_id AND lower(name) = 'annual leave'` returns 1 (original only). |

## 6. Postconditions
- No duplicate leave type records were created.
- Original "Annual Leave" leave type remains unchanged.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
