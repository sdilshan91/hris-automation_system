---
id: TC-CHR-246
user_story: US-CHR-010
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-246: File exceeding 25 MB limit rejected with clear error message

## 1. Test Objective
Verify that the system rejects an import file larger than 25 MB (BR-7) before processing begins, displaying a clear error message about the file size limit. No rows should be processed or imported.

## 2. Related Requirements
- User Story: US-CHR-010
- Acceptance Criteria: AC-2 (negative path)
- Functional Requirements: FR-1
- Business Rules: BR-7

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |
| File Name | oversized_import.csv | > 25 MB CSV file |
| File Size | 26 MB | Exceeds BR-7 limit of 25 MB |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the bulk import page and advance to Step 2 (Upload File). | Upload zone is visible. |
| 2 | Select `oversized_import.csv` (26 MB) via the file picker. | File name and size (26 MB) are displayed. |
| 3 | Click "Import" (or the file is rejected on selection, depending on implementation). | An error message is displayed: "File size exceeds the maximum limit of 25 MB. Please reduce the file size and try again." The system does NOT proceed to processing. |
| 4 | Verify no API request to the import endpoint was sent (or the API returns 413/400 immediately). | No import processing occurred. No employees were created. |
| 5 | Verify no records were added to the `employees` table. | Employee count unchanged. |

## 6. Postconditions
- No employees created. No partial processing occurred. System state unchanged.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
