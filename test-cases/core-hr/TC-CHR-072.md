---
id: TC-CHR-072
user_story: US-CHR-001
module: Core HR
priority: high
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-072: Malware scan seam invoked on profile photo upload (NFR-3)

## 1. Test Objective
Verify that the profile photo upload pipeline invokes the malware scanning seam (ClamAV integration) before persisting the file to object storage, per NFR-3. A file flagged by the scanner must be rejected.

## 2. Related Requirements
- User Story: US-CHR-001
- Acceptance Criteria: AC-4
- Functional Requirements: FR-6
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- ClamAV (or equivalent malware scan service) is configured and running.
- A test file containing the EICAR test string (standard antivirus test) embedded in a JPEG container is prepared.
- A clean JPEG file is also prepared.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Infected file | eicar-test.jpg | Contains EICAR test signature in JPEG container |
| Clean file | clean-photo.jpg | 1 MB, valid JPEG, no threats |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload "clean-photo.jpg" as profile photo during employee creation | File passes malware scan; upload succeeds. |
| 2 | Verify application logs show malware scan was invoked for "clean-photo.jpg" | Log entry confirms scan was performed (e.g., "Malware scan passed for file: clean-photo.jpg"). |
| 3 | Upload "eicar-test.jpg" as profile photo during employee creation | File fails malware scan. |
| 4 | Verify the upload is rejected with an appropriate error | Error message indicates the file was flagged (e.g., "The uploaded file could not be processed. Please try a different image."). |
| 5 | Verify the infected file is NOT stored in object storage | No file exists at the expected storage path. |
| 6 | Verify application logs record the malware detection event | Log entry confirms threat detected (without exposing internal scan details to the user). |

## 6. Postconditions
- Clean files are stored after passing malware scan.
- Infected files are rejected and not persisted.
- Scan events are logged for audit.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
