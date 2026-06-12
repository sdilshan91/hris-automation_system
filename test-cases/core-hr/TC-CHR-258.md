---
id: TC-CHR-258
user_story: US-CHR-010
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-258: Invalid email format in import row -- row rejected with validation error

## 1. Test Objective
Verify that a row with an invalid email format (e.g., missing '@' or domain) is rejected with a clear validation error at the row level. This validates FR-3 data type and format validation.

## 2. Related Requirements
- User Story: US-CHR-010
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant "acme" exists with status `active` and sufficient capacity.
- An HR Officer user is authenticated in the "acme" tenant context.
- Required departments and job titles exist.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |
| File Name | bad_email.csv | 3 rows: row 2 has invalid email format |
| Row 2 | Jane,Doe,not-an-email,...,Engineering,Software Engineer,Full-Time | Invalid email format |
| Rows 1, 3 | Valid emails | Should succeed |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload `bad_email.csv` and click "Import". | System processes the file. |
| 2 | Wait for processing to complete. | Amber summary: "2 of 3 records imported successfully. 1 record failed." |
| 3 | Verify the error table. | Error entry: Row 2, field `email`, error "Invalid email format." |
| 4 | Query the `employees` table. | 2 employees created (rows 1, 3). Row 2 not imported. |

## 6. Postconditions
- 2 employees created. Row with invalid email rejected.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
