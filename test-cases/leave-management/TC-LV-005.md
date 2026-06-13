---
id: TC-LV-005
user_story: US-LV-001
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-005: Configure documents-required threshold and enforcement on apply

## 1. Test Objective
Verify that an HR Officer can configure a "documents required" rule with a day threshold on a leave type, and that the system enforces document upload when employees apply for leave exceeding that threshold.

## 2. Related Requirements
- User Story: US-LV-001
- Acceptance Criteria: AC-5
- Functional Requirements: FR-1, FR-2

## 3. Preconditions
- Tenant "acme" has an active leave type "Sick Leave" with `documents_required = false`.
- A user with `Leave.Configure` permission is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Leave Type | Sick Leave | Existing |
| Documents Required | true | Enabling |
| Document Day Threshold | 2 | Days after which document is required |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Leave Types and click Edit on "Sick Leave" | Slide-over panel opens with existing configuration. Documents Required toggle is off. |
| 2 | Toggle "Documents Required" to true | Document Day Threshold field becomes visible/enabled. |
| 3 | Enter 2 in the "Document Day Threshold" field | Field accepts the value. |
| 4 | Click Save | API call `PUT /api/v1/leave-types/{id}` with `{ documents_required: true, document_day_threshold: 2 }`. Response 200 OK. |
| 5 | Verify the saved leave type shows documents_required = true, document_day_threshold = 2 | Confirmed in response and on re-loading the edit panel. |
| 6 | As an employee, attempt to apply for 1 day of Sick Leave WITHOUT attaching a document (FORWARD-LOOKING) | Application is accepted without document (1 day <= 2 day threshold). Mark DEFERRED if leave-request module not built. |
| 7 | As an employee, attempt to apply for 3 days of Sick Leave WITHOUT attaching a document (FORWARD-LOOKING) | Application is rejected with validation error: "Supporting documents are required for Sick Leave exceeding 2 days." Mark DEFERRED if leave-request module not built. |
| 8 | As an employee, apply for 3 days of Sick Leave WITH a valid document attached (FORWARD-LOOKING) | Application is accepted. Mark DEFERRED if leave-request module not built. |
| 9 | Verify audit log captures the configuration change | Audit record with before `{ documents_required: false }` and after `{ documents_required: true, document_day_threshold: 2 }`. |

## 6. Postconditions
- "Sick Leave" type has `documents_required = true` and `document_day_threshold = 2`.
- Document enforcement is active for leave applications exceeding the threshold.
- Audit trail records the configuration change.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
