---
id: TC-CHR-247
user_story: US-CHR-010
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-247: Disallowed file type (.pdf) rejected with clear error message

## 1. Test Objective
Verify that the system rejects files that are not CSV (.csv) or Excel (.xlsx) format. Uploading a PDF, executable, or other unsupported type should produce an immediate error without any processing. This validates FR-1 file type enforcement.

## 2. Related Requirements
- User Story: US-CHR-010
- Acceptance Criteria: AC-2 (negative path)
- Functional Requirements: FR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |
| File Name | employees.pdf | Unsupported file type |
| File Size | 500 KB | Under size limit but wrong format |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the bulk import page and advance to Step 2 (Upload File). | Upload zone is visible. |
| 2 | Attempt to select `employees.pdf` via the file picker. | Either the file picker filter restricts selection to `.csv` and `.xlsx` only, OR the file is accepted in the picker but rejected on validation. |
| 3 | If the file was selectable, click "Import". | An error message is displayed: "Unsupported file type. Please upload a CSV (.csv) or Excel (.xlsx) file." No processing occurs. |
| 4 | Repeat with `employees.exe`. | Same rejection behavior. |
| 5 | Repeat with `employees.txt`. | Same rejection behavior. |
| 6 | Verify no employees were created. | Employee count unchanged. |

## 6. Postconditions
- No employees created. Only CSV and XLSX are accepted.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
