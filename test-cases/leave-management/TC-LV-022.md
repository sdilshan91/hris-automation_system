---
id: TC-LV-022
user_story: US-LV-001
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-022: Required fields validation -- name, code, entitlement missing

## 1. Test Objective
Verify that submitting a leave type create form with missing required fields (name, code, annual_entitlement) is rejected with field-level validation errors both client-side and server-side.

## 2. Related Requirements
- User Story: US-LV-001
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-2

## 3. Preconditions
- Tenant "acme" exists.
- A user with `Leave.Configure` permission is authenticated.

## 4. Test Data
| Scenario | Name | Code | Entitlement | Expected Error |
|----------|------|------|-------------|----------------|
| All empty | (empty) | (empty) | (empty) | Multiple field errors |
| Name only missing | (empty) | AL | 20 | "Name is required" |
| Code only missing | Annual Leave | (empty) | 20 | "Code is required" |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Add Leave Type" and immediately click Save without filling any fields | Client-side validation highlights required fields: Name, Code, and other required fields. Error messages displayed below each field. |
| 2 | Bypass client validation and send `POST /api/v1/leave-types` with empty body `{}` | API returns 400 Bad Request with validation errors listing all missing required fields. |
| 3 | Send `POST /api/v1/leave-types` with `{ code: "AL", annual_entitlement: 20 }` (name missing) | API returns 400. Validation error: "Name is required." |
| 4 | Send `POST /api/v1/leave-types` with `{ name: "Annual Leave", annual_entitlement: 20 }` (code missing) | API returns 400. Validation error: "Code is required." |
| 5 | Verify no records were created | `SELECT count(*) FROM leave_type WHERE tenant_id = acme_id` returns the count before the test. |

## 6. Postconditions
- No leave type records created with missing required fields.
- Validation errors are descriptive and field-specific.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
