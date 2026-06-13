---
id: TC-LV-063
user_story: US-LV-003
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-063: Attachment validation (type, size, count) and tenant-scoped storage path

## 1. Test Objective
Verify that file attachments on a leave request are validated for type, size, and count per the configured constraints, and that accepted files are stored under the tenant-scoped blob path `{tenantId}/leaves/{requestId}/`.

## 2. Related Requirements
- User Story: US-LV-003
- Functional Requirements: FR-1
- Non-Functional Requirements: NFR-3
- Assumptions: Section 10 (5MB/file, max 3 files, PDF/JPG/PNG)

## 3. Preconditions
- Tenant "acme" is active; Employee "Jane Smith" is authenticated with `Leave.Apply`.
- A leave type requiring a document (e.g., Sick Leave over threshold) is selected.
- Tenant-scoped blob storage is available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Valid file | certificate.pdf (2 MB) | Allowed type and size |
| Oversize file | scan.pdf (8 MB) | Exceeds 5 MB/file limit |
| Disallowed type | note.exe / note.html / note.svg | Not PDF/JPG/PNG |
| Too many files | 4 valid files | Exceeds max of 3 |
| Storage path | acme-tenant-id/leaves/{requestId}/ | Tenant-scoped |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Attach certificate.pdf (2 MB) and submit | Upload succeeds; request created; `attachment_urls` populated. |
| 2 | Verify the stored object path | File stored at `{acme tenantId}/leaves/{requestId}/...` -- tenant and request scoped, not in another tenant's path. |
| 3 | Attempt to attach scan.pdf (8 MB) | Rejected with "File exceeds 5 MB limit." No upload. |
| 4 | Attempt to attach note.exe (or .html/.svg) | Rejected with "Unsupported file type. Allowed: PDF, JPG, PNG." |
| 5 | Attempt to attach 4 files | Rejected with "A maximum of 3 files is allowed." |
| 6 | Attach exactly 3 valid files (boundary) | Accepted; all 3 stored under the tenant-scoped path. |
| 7 | Attempt to fetch another tenant's attachment URL while authenticated in acme | Access denied (403/404); attachments are not cross-tenant accessible. |

## 6. Postconditions
- Only valid files (type, size, count) are accepted.
- Stored files reside under the tenant-scoped path and are not accessible cross-tenant.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
