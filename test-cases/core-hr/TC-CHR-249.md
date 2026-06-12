---
id: TC-CHR-249
user_story: US-CHR-010
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-249: Async large file -- 1000+ rows queued as Hangfire background job with progress indicator

## 1. Test Objective
Verify that when an import file contains more than 500 rows (the FR-7 threshold), the system queues the import as an asynchronous Hangfire background job. The UI displays a progress indicator with percentage and estimated time, and the user is notified upon completion. This validates AC-4, FR-7, and NFR-1.

## 2. Related Requirements
- User Story: US-CHR-010
- Acceptance Criteria: AC-4
- Functional Requirements: FR-7
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- Tenant "acme" exists with status `active` and sufficient employee capacity (plan limit > 1000 or unlimited).
- An HR Officer user is authenticated in the "acme" tenant context.
- Departments and job titles referenced in the file exist in tenant "acme".
- Hangfire is running and configured for background job processing.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |
| File Name | large_import_1000.csv | 1000 valid rows |
| File Size | ~500 KB | Under 25 MB limit |
| Threshold | 500 rows | FR-7: files > 500 rows processed async |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload `large_import_1000.csv` and click "Import". | The system accepts the file and detects > 500 rows. |
| 2 | Observe the UI response. | Instead of synchronous processing, the system displays: "Your import has been queued for background processing. You'll be notified when the import completes. You can navigate away from this page." A progress bar is shown. |
| 3 | Verify a Hangfire background job was created. | Query the Hangfire job storage: a new job of type "BulkEmployeeImport" (or equivalent) exists with status "Enqueued" or "Processing", scoped to tenant "acme" with the file reference. |
| 4 | Monitor the progress indicator. | The progress bar updates (e.g., 10%, 25%, 50%, 75%, 100%) as batches are processed. Estimated time remaining is shown (approximate). |
| 5 | Navigate away from the page and return. | The progress indicator still reflects the current state (the job continues in the background). |
| 6 | Wait for the job to complete. | The progress bar reaches 100%. A summary is displayed: "1000 of 1000 records imported successfully." |
| 7 | Verify in-app notification. | An in-app notification is delivered to the HR Officer: "Bulk import completed: 1000 employees imported from large_import_1000.csv." (DEFERRED: email notification depends on Notification module.) |
| 8 | Query the `employees` table. | 1000 new employees exist with correct `tenant_id` and auto-generated `employee_no` values. |
| 9 | Verify audit log. | Import logged with: total = 1000, success = 1000, failure = 0, processing mode = "async". |

## 6. Postconditions
- 1000 new employees created in "acme" tenant.
- Hangfire job completed successfully.
- In-app notification delivered. (Email notification DEFERRED -- pending Notification module.)

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
