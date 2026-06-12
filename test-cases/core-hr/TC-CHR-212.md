---
id: TC-CHR-212
user_story: US-CHR-008
module: Core HR
priority: high
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-212: EXIF data stripped from image uploads

## 1. Test Objective
Verify that when an image file (JPEG or PNG) is uploaded as a document, the system strips EXIF metadata before persisting the file in object storage. This prevents leaking GPS coordinates, device information, or other sensitive metadata embedded in photos. This validates FR-5.

## 2. Related Requirements
- User Story: US-CHR-008
- Functional Requirements: FR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- Employee "Jane Doe" (emp-001-uuid) exists.
- A JPEG image "id-photo.jpg" with embedded EXIF data (GPS coordinates, camera model, date taken) is prepared.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| File | id-photo.jpg | 3 MB JPEG with EXIF: GPS=(6.9271, 79.8612), Camera=iPhone 15, DateTaken=2026-01-15 |
| Category | ID | Valid category |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify the test file "id-photo.jpg" has EXIF data before upload. | Using an EXIF viewer/tool: GPS coordinates, camera model, and date taken are present in the file metadata. |
| 2 | Upload "id-photo.jpg" to Jane Doe's record with category "ID". | Upload succeeds. 201 Created. |
| 3 | Download the stored file from object storage (directly or via the signed URL). | File downloads successfully. |
| 4 | Inspect the downloaded file for EXIF data. | Using an EXIF viewer/tool: GPS coordinates, camera model, date taken, and other EXIF tags are **absent** or stripped. The image content (pixels) is unchanged. |
| 5 | Repeat with a PNG file containing EXIF/metadata chunks. | EXIF/metadata is stripped. Image content preserved. |
| 6 | Verify the file size may differ slightly (EXIF removal reduces size). | Stored file size <= original file size. The `file_size_bytes` in the DB reflects the post-strip size. |

## 6. Postconditions
- The stored image files have no EXIF metadata.
- Image visual content is preserved.
- No sensitive metadata (GPS, device info) is accessible via the stored file.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
