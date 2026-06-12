---
id: TC-CHR-245
user_story: US-CHR-010
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-245: Missing required field (first_name) in import row -- row rejected with field-level error

## 1. Test Objective
Verify that when a row in the import file is missing a required field (`first_name`), that row is rejected and the error report identifies the row number, the missing field name, and a clear "required" error message. This validates FR-3 row-level validation.

## 2. Related Requirements
- User Story: US-CHR-010
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3, FR-4, FR-8

## 3. Preconditions
- Tenant "acme" exists with status `active` and sufficient employee capacity.
- An HR Officer user is authenticated in the "acme" tenant context.
- Required departments and job titles exist.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |
| File Name | missing_required_field.csv | 3 rows: row 2 missing first_name |
| Row 2 | ,Smith,john@acme.test,...,Engineering,Software Engineer,Full-Time | first_name is empty |
| Rows 1, 3 | All required fields present | Should succeed |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload `missing_required_field.csv` and click "Import". | System processes the file. |
| 2 | Wait for processing to complete. | Amber summary: "2 of 3 records imported successfully. 1 record failed." |
| 3 | Verify the error table. | Error entry: Row 2, field `first_name`, error "first_name is required." |
| 4 | Query the `employees` table. | 2 employees created (rows 1, 3). Row 2 not imported. |

## 6. Postconditions
- 2 employees created. Row with missing required field reported with specific error.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
