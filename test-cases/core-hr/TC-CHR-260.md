---
id: TC-CHR-260
user_story: US-CHR-010
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-260: Async import completion notification -- user notified in-app when background job finishes

## 1. Test Objective
Verify that when an async import (> 500 rows) completes, the user receives an in-app notification with a summary of the import results. Email notification is DEFERRED pending the Notification module. This validates AC-4.

## 2. Related Requirements
- User Story: US-CHR-010
- Acceptance Criteria: AC-4
- Functional Requirements: FR-7

## 3. Preconditions
- Tenant "acme" exists with status `active` and sufficient capacity.
- An HR Officer user is authenticated.
- A file with 600 rows (above the 500-row async threshold) is ready.
- Hangfire background job processing is active.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |
| File Name | async_600_rows.csv | 600 valid rows, triggers async processing |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload `async_600_rows.csv` and click "Import". | System queues import as a Hangfire background job. UI shows progress indicator and message about background processing. |
| 2 | Navigate away from the import page to the dashboard. | The import continues in the background. |
| 3 | Wait for the background job to complete. | (Monitor Hangfire dashboard or wait for the job to finish.) |
| 4 | Check the notification bell/indicator in the app header. | An in-app notification appears: "Bulk import completed: 600 employees imported from async_600_rows.csv." (or similar summary with success/failure counts). |
| 5 | Click the notification. | The notification links to the import results page or shows the summary detail. |
| 6 | (DEFERRED) Verify email notification was sent. | DEFERRED -- email notification dispatch depends on the Notification module which is not yet built. When built, verify an email is sent to the HR Officer's email with the import summary. |

## 6. Postconditions
- 600 employees created.
- In-app notification delivered.
- Email notification DEFERRED.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
