---
id: TC-ATT-095
user_story: US-ATT-007
module: Attendance
priority: high
type: integration
status: draft
created: 2026-06-14
---

# TC-ATT-095: Large export (> 1,000 employees) is processed asynchronously via Hangfire with a download-link notification (FR-7)

## 1. Test Objective
Verify FR-7: when an export targets more than 1,000 employees, the system does NOT block the request synchronously -- it enqueues a Hangfire background job, returns an accepted/queued response, generates the file, and makes it available with a download link delivered via notification. The notification DISPATCH is DEFERRED on the Notification System (US-NTF); the queueing seam, threshold, and file availability are verified now.

## 2. Related Requirements
- User Story: US-ATT-007
- Functional Requirements: FR-6 (export formats), FR-7 (async > 1,000 employees via Hangfire + notification)

## 3. Preconditions
- Tenant "bigco" with > 1,000 active employees and a generated month summary (2026-05).
- HR Officer "Quinn" authenticated with `Attendance.Read.All`.
- Tenant "acme" with < 1,000 employees (sync control).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| bigco employees | > 1,000 | async threshold |
| acme employees | < 1,000 | sync control |
| month | 2026-05 | selected period |
| format | xlsx | also csv/pdf |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Quinn (bigco), `GET /summary/monthly/export?month=2026-05&format=xlsx` | The request returns promptly with an accepted/queued response (job id / "export is being prepared"), NOT a blocking file download; a Hangfire job is enqueued (FR-7). |
| 2 | Await the Hangfire job | The job completes and produces the export file (all > 1,000 rows), tenant-scoped to bigco, with data matching the summary. |
| 3 | Verify the download link / notification SEAM | A notification record/seam is created for Quinn (recipient = requesting HR, tenant-scoped, payload references the export/download link). In-app/email DELIVERY is DEFERRED on US-NTF -- assert the seam, not the delivered notification. |
| 4 | Download via the link | The completed file downloads and is a valid xlsx with all rows. |
| 5 | Sync control (acme, < 1,000) | The export returns synchronously as a direct file download (TC-ATT-087 path), confirming the threshold gates sync-vs-async. |
| 6 | Boundary -- exactly 1,000 vs 1,001 | Assert which path each takes against the implemented threshold semantics (> 1,000 is async); flag if the boundary inclusivity is ambiguous. |

## 6. Postconditions
- Large exports run asynchronously via Hangfire and surface a tenant-scoped download link through the notification seam; small exports stay synchronous; no request blocks on a large file.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **Notification dispatch (FR-7) DEFERRED on US-NTF:** the Notification System is not built. This TC verifies the export-ready notification SEAM (recipient = requesting HR, tenant-scoped, payload references the download link) now and DEFERS the in-app/email delivery + badge assertions until US-NTF lands. Consistent with US-ATT-003 TC-ATT-032, US-ATT-006 TC-ATT-071, and leave-management notification handling. **Reported to caller.**
- Where the generated file is persisted (e.g. blob storage `{tenantId}/...`) may be CONDITIONAL on a Blob Storage layer, mirroring US-LV-012 TC-LV-240; assert the tenant-scoped path and DEFER blob-persistence specifics if not wired.
