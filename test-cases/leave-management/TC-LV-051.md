---
id: TC-LV-051
user_story: US-LV-003
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-051: Sick leave over document threshold without attachment is rejected

## 1. Test Objective
Verify that when an Employee applies for a sick leave that exceeds the configured document day threshold and submits without attaching a required document, the system returns a validation error and does not create the request. (Test Hint: apply for sick leave exceeding threshold without attachment; verify error.)

## 2. Related Requirements
- User Story: US-LV-003
- Acceptance Criteria: AC-3
- Functional Requirements: FR-1

## 3. Preconditions
- Tenant "acme" is active; Employee "Jane Smith" is authenticated with `Leave.Apply`.
- Leave type "Sick Leave" is active with `documents_required = true` and `document_day_threshold = 2` (medical certificate required for sick leave exceeding 2 days, per US-LV-001 default seed).
- Jane has a Sick Leave balance of at least 5 days.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee | Jane Smith | Sick Leave balance >= 5 |
| Leave Type | Sick Leave | documents_required = true, threshold = 2 |
| Start Date | 2026-07-06 (Mon) | -- |
| End Date | 2026-07-08 (Wed) | 3 working days, exceeds threshold of 2 |
| Attachment | (none) | No medical certificate uploaded |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Leave Application page and select Leave Type = "Sick Leave" | The attachment area indicates a document is required when the request exceeds the threshold. |
| 2 | Select Start Date = 2026-07-06, End Date = 2026-07-08 (3 days) | Requested days chip shows 3.0; a hint appears: "Medical certificate is required for sick leave exceeding 2 days." |
| 3 | Leave the attachment empty and click Submit | Client-side validation blocks submission with the error: "Medical certificate is required for sick leave exceeding 2 days." |
| 4 | Force the API call `POST /api/v1/leaves` without `attachments` | Server returns 400 Bad Request (or 422) with the same error message. No request is created. |
| 5 | Attach a valid PDF medical certificate and resubmit | Submission succeeds (201 Created); request is created with `attachment_urls` populated. |
| 6 | Repeat with a 2-day sick request (at the threshold) and no attachment | Submission succeeds without an attachment (threshold is "exceeding 2 days", so exactly 2 days does not require a document). |

## 6. Postconditions
- No `leave_request` is created for the over-threshold request lacking a document.
- A request created with a valid attachment stores `attachment_urls` under the tenant-scoped path.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
