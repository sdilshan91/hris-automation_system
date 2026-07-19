---
id: TC-CHR-331
user_story: US-CHR-001
module: Core HR
priority: high
type: functional
status: automated
created: 2026-07-19
automated: 2026-07-19
defect:
  - ISSUE-246
---

# TC-CHR-331: Employee profile photo upload rejects WebP (un-strippable EXIF on pinned ImageSharp) — ISSUE-246

## 1. Test Objective
Verify the ISSUE-246 fix on US-CHR-001/US-CHR-008 (profile photo upload): a **WebP** image is rejected, not passed through. The pinned ImageSharp 2.1.x cannot strip WebP EXIF, so allowing WebP would persist un-stripped GPS/PII metadata. The upload must return **400** with an "Allowed types: JPEG, PNG" message and not silently accept WebP.

## 2. Related Requirements
- User Story: US-CHR-001 (also US-CHR-008 — profile photo)
- Finding: ISSUE-246 (PR #371)
- Security concern: PII/EXIF (GPS) leakage via un-stripped image metadata

## 3. Preconditions
- A created employee in the tenant.
- Uses the EF Core InMemory provider through `EmployeeService` (mirrors `EmployeeServiceTests`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| File name | photo.webp | |
| Content type | image/webp | rejected type |
| Size | 1024 bytes | within size limit (isolates the type check) |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Upload a `image/webp` photo for the employee. | Failure with status **400**; error contains "Allowed types: JPEG, PNG" and does NOT list WebP. | `EmployeeServiceTests.UploadPhoto_WebP_IsRejected_ISSUE246` |

## 6. Postconditions
- Only EXIF-strippable formats (JPEG, PNG) are accepted for profile photos; WebP cannot smuggle un-stripped metadata into storage.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test (format allowlist edge)
- [x] Security test (EXIF/PII leakage)
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite, EF Core InMemory through the real service):**
  - `EmployeeServiceTests.UploadPhoto_WebP_IsRejected_ISSUE246`
- Backing suite trait: `[Trait("TC", "TC-CHR-331")]`.
