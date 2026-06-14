---
id: TC-LV-240
user_story: US-LV-012
module: Leave Management
priority: high
type: integration
status: draft
created: 2026-06-14
---

# TC-LV-240: Large export (>5,000 rows) processed as a Hangfire background job with notify (AC-5, FR-5, Test Hint)

## 1. Test Objective
Verify the AC-5 / FR-5 Test Hint: a report export exceeding 5,000 rows (e.g. 6,000) is NOT generated synchronously — it is queued as a Hangfire background job, stored in tenant-scoped blob storage, and the user is notified when ready. The blob-storage persistence and notification dispatch are DEFERRED dependencies and are recorded as CONDITIONAL, with the queue/threshold decision verified live.

## 2. Related Requirements
- User Story: US-LV-012
- Acceptance Criteria: AC-5
- Functional Requirements: FR-5
- Data Requirements: §7 export path `{tenantId}/reports/leave/{reportId}.xlsx`
- Dependencies: Hangfire, Blob Storage, Notifications module

## 3. Preconditions
- Tenant "acme"; HR authenticated; a report yielding ≥6,000 rows.
- Hangfire configured (PostgreSQL storage, per platform).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Rows | 6,000 | over the 5,000 threshold |
| Threshold | 5,000 | FR-5 / NFR-2 boundary |
| Blob path | `{tenantId}/reports/leave/{reportId}.xlsx` | DEFERRED |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Trigger an export of the 6,000-row report | The request returns promptly (e.g. 202/accepted with a reportId/jobId); the file is NOT streamed inline. A Hangfire background job is enqueued. |
| 2 | Boundary: export exactly 5,000 rows | Processed synchronously (≤10s, NFR-2); export of 5,001 rows crosses into the background path. |
| 3 | (CONDITIONAL on Blob Storage) Let the background job complete | The generated XLSX is stored under the tenant-scoped path `{tenantId}/reports/leave/{reportId}.xlsx`; mark CONDITIONAL/DEFERRED until blob storage is wired (verify the seam/intended path; do not pass silently). |
| 4 | (CONDITIONAL on Notifications module) On completion | The user is notified the export is ready with a download link; the dispatch is DEFERRED on the notifications module — verify the queued/log-only seam and record CONDITIONAL. |

## 6. Postconditions
- Exports >5,000 rows are queued as background jobs (verified); blob persistence and the ready-notification are recorded CONDITIONAL/DEFERRED, not as silent passes.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
