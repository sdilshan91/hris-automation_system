---
id: TC-CHR-070
user_story: US-CHR-001
module: Core HR
priority: high
type: functional
status: blocked
created: 2026-06-12
---

# TC-CHR-070: Profile photo oversized (>5 MB) rejected (FR-6)

## 1. Test Objective
Verify that uploading a profile photo larger than 5 MB is rejected with an appropriate validation error, per FR-6 max 5 MB constraint.

## 2. Related Requirements
- User Story: US-CHR-001
- Acceptance Criteria: AC-4 (negative path)
- Functional Requirements: FR-6

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- A JPEG image file of 6 MB is prepared.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Photo file | large-photo.jpg | 6 MB, JPEG (exceeds 5 MB limit) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Employee module and click "Add Employee" | Wizard opens. |
| 2 | In the Personal Info step, attempt to upload "large-photo.jpg" (6 MB) | Upload is rejected. |
| 3 | Verify a validation error is displayed | Error message indicates the file exceeds the 5 MB limit (e.g., "Profile photo must be 5 MB or smaller."). |
| 4 | Verify the drag-and-drop zone does not show a preview | No avatar preview is displayed for the rejected file. |
| 5 | Verify no file is sent to the server (client-side validation) or API returns 400/422 | If client-side: no network request. If server-side: error response with size constraint message. |

## 6. Postconditions
- No file is stored in object storage.
- The employee creation form remains open and editable.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

> **Execution 2026-06-30 (FE, acme):** STILL BLOCKED — the create wizard renders a "Upload profile photo" control (JPEG/PNG/WebP, Max 5 MB hint), but verifying EXIF-stripping / signed-URL / server-side size rejection is backend upload behavior requiring a real file POST + storage inspection, not a browser-render check. Not an FE-render defect.

> **Execution 2026-07-03 (API, @test-runner):** BLOCKED — oversized(>5MB) rejection needs a crafted 6MB file POST to profile-photo on a throwaway employee + storage inspection — out of budget. Endpoint exists.
