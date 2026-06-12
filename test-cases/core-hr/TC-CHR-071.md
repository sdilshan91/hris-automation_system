---
id: TC-CHR-071
user_story: US-CHR-001
module: Core HR
priority: high
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-071: Profile photo disallowed MIME type (.exe) rejected (FR-6)

## 1. Test Objective
Verify that uploading a file with a disallowed MIME type (e.g., .exe, .bat, .sh) as a profile photo is rejected. Only JPEG, PNG, and WebP are accepted per FR-6.

## 2. Related Requirements
- User Story: US-CHR-001
- Acceptance Criteria: AC-4 (negative path)
- Functional Requirements: FR-6

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- An executable file "malicious.exe" (1 MB) is prepared.
- A file "sneaky.jpg.exe" (1 MB) is prepared (double extension attack).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| File 1 | malicious.exe | Executable, disallowed MIME type |
| File 2 | sneaky.jpg.exe | Double extension attack |
| File 3 | renamed.png (actually .exe binary) | MIME type mismatch |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Employee module and click "Add Employee" | Wizard opens. |
| 2 | Attempt to upload "malicious.exe" as profile photo | Upload is rejected. |
| 3 | Verify validation error indicates unsupported file type | Error message: "Only JPEG, PNG, and WebP images are allowed." or similar. |
| 4 | Attempt to upload "sneaky.jpg.exe" (double extension) | Upload is rejected. The system validates actual MIME type, not just extension. |
| 5 | Attempt to upload "renamed.png" (exe binary renamed to .png) | Upload is rejected. Server validates the actual file content/magic bytes, not just the extension. |
| 6 | Verify no files are stored in object storage | Object storage path for this employee has no files. |

## 6. Postconditions
- No disallowed files are stored in object storage.
- The form remains open and editable.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
