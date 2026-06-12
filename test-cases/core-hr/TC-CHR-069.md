---
id: TC-CHR-069
user_story: US-CHR-001
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-069: Profile photo upload with EXIF stripping and signed URL (AC-4)

## 1. Test Objective
Verify that uploading a valid profile photo (JPEG, PNG, or WebP, under 5 MB) during employee creation results in the photo being stored at the correct tenant-isolated path, EXIF data stripped, and a signed URL returned for display.

## 2. Related Requirements
- User Story: US-CHR-001
- Acceptance Criteria: AC-4
- Functional Requirements: FR-6
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- Object storage (Azure Blob / S3 / MinIO) is available and configured.
- A valid JPEG image file (2 MB, with EXIF metadata including GPS coordinates) is prepared.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Photo file | profile-with-exif.jpg | 2 MB, JPEG, contains EXIF GPS data |
| Expected storage path | {tenantId}/core-hr/{employeeId}/profile/profile-with-exif.jpg | Tenant-isolated |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Employee module and click "Add Employee" | Wizard opens. |
| 2 | In the Personal Info step, locate the profile photo upload area | Drag-and-drop zone with circular crop preview is visible. |
| 3 | Upload "profile-with-exif.jpg" (2 MB JPEG with EXIF GPS data) | File is accepted; avatar preview shows the uploaded image in circular crop. |
| 4 | Fill in all mandatory fields and submit the form | Employee created successfully (201 Created). |
| 5 | Verify the response includes a signed URL for the profile photo | URL is present, contains a signature/token parameter, and is time-limited. |
| 6 | Access the signed URL in a browser | The image loads correctly. |
| 7 | Download the stored image from object storage and inspect EXIF metadata | EXIF data (including GPS coordinates) has been stripped. Only essential image data remains. |
| 8 | Verify the storage path is `{tenantId}/core-hr/{employeeId}/profile/profile-with-exif.jpg` | Path matches AC-4 specification with tenant-isolated structure. |
| 9 | Verify the employee profile page displays the photo via the signed URL | Photo is visible on the employee's profile. |

## 6. Postconditions
- The profile photo is stored at the tenant-isolated path in object storage.
- EXIF metadata has been stripped from the stored image.
- A time-limited signed URL is available for display.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
