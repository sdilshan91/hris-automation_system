---
id: TC-CHR-257
user_story: US-CHR-010
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-257: Non-existent job_title_name in import row -- row rejected with descriptive error

## 1. Test Objective
Verify that when a row in the import file references a `job_title_name` that does not exist in the tenant's job title list, that row is rejected with a clear error message. This validates BR-3 and FR-3 for job title resolution.

## 2. Related Requirements
- User Story: US-CHR-010
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" exists with status `active` and sufficient employee capacity.
- An HR Officer user is authenticated in the "acme" tenant context.
- Department "Engineering" exists. Job title "Software Engineer" exists. Job title "Chief Happiness Officer" does NOT exist.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |
| File Name | bad_job_title.csv | 3 rows: row 2 has non-existent job title |
| Row 2 | John,Smith,john@acme.test,...,Engineering,Chief Happiness Officer,Full-Time | Invalid job title |
| Rows 1, 3 | Valid job titles | Should succeed |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload `bad_job_title.csv` and click "Import". | System processes the file. |
| 2 | Wait for processing to complete. | Amber summary: "2 of 3 records imported successfully. 1 record failed." |
| 3 | Verify the error table. | Error entry: Row 2, field `job_title_name`, error "Job title 'Chief Happiness Officer' not found in tenant." |
| 4 | Query the `employees` table. | 2 employees created (rows 1, 3). Row 2 not imported. |

## 6. Postconditions
- 2 employees created. Row with invalid job title rejected with clear error.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
