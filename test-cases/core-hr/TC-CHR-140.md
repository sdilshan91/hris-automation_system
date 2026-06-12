---
id: TC-CHR-140
user_story: US-CHR-003
module: Core HR
priority: high
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-140: Export respects role-based visibility (BR-4)

## 1. Test Objective
Verify that the export (CSV and Excel) respects the same role-based field visibility as the UI. An Employee role export must exclude sensitive fields (email, phone, dateOfJoining, employmentType). An HR Officer export includes all fields. This validates BR-4, FR-8, FR-9.

## 2. Related Requirements
- User Story: US-CHR-003
- Business Rules: BR-4
- Functional Requirements: FR-8, FR-9

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Two users: one with HR Officer role, one with Employee role, both in "acme".
- 20 employees exist.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| HR Officer export columns | employee_no, first_name, last_name, email, phone, department_name, job_title_name, status, date_of_joining, location, employment_type | All fields |
| Employee export columns | employee_no, first_name, last_name, department_name, job_title_name, status, location | Sensitive fields stripped |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer and export CSV | Downloaded CSV header contains all 11 fields including email, phone, date_of_joining, employment_type. |
| 2 | Verify HR Officer CSV data rows | All fields populated; 20 rows of data. |
| 3 | Authenticate as Employee role and export CSV | Downloaded CSV header excludes email, phone, date_of_joining, employment_type. |
| 4 | Verify Employee CSV data rows | Only basic fields present; no column for email or phone. |
| 5 | Authenticate as Employee role and export Excel | Same as step 3/4 but in .xlsx format -- sensitive columns are absent. |
| 6 | Verify no column re-ordering trick exposes hidden data | Even if the Employee manipulates the export URL to request `format=Csv`, the backend enforces field visibility based on the authenticated user's permissions. |

## 6. Postconditions
- No data was modified.
- Role-based visibility consistently applied across API, UI, and exports.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
