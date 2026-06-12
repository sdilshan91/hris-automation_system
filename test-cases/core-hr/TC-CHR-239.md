---
id: TC-CHR-239
user_story: US-CHR-010
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-239: Download import template -- CSV and Excel formats contain correct headers, sample data, and field descriptions

## 1. Test Objective
Verify that the bulk import page provides downloadable template files in both CSV and Excel formats, and that each template contains column headers matching the import schema defined in US-CHR-010 Section 7, at least one sample data row, and a column description guide. This validates AC-1 and FR-2.

## 2. Related Requirements
- User Story: US-CHR-010
- Acceptance Criteria: AC-1
- Functional Requirements: FR-2

## 3. Preconditions
- Tenant "acme" exists with status `active` and subdomain `acme.yourhrm.com`.
- An HR Officer user is authenticated in the "acme" tenant context.
- The bulk import page is accessible at the expected route (e.g., `/employees/import`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona per US-CHR-010 |
| Expected Headers | first_name, last_name, email, phone, date_of_birth, gender, date_of_joining, department_name, job_title_name, employment_type, location_name, status | Per Section 7 Data Requirements |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the bulk import page. | The page loads with a 3-step card layout: Download Template, Upload File, Review Results. Step 1 card is active. |
| 2 | Locate the "Download CSV Template" link in the Step 1 card. | A download link for CSV template is visible. |
| 3 | Click the CSV template download link. | A `.csv` file is downloaded. |
| 4 | Open the CSV file and inspect headers (row 1). | Headers match the schema: `first_name`, `last_name`, `email`, `phone`, `date_of_birth`, `gender`, `date_of_joining`, `department_name`, `job_title_name`, `employment_type`, `location_name`, `status`. |
| 5 | Inspect row 2 of the CSV file. | At least one sample data row is present with realistic example values (e.g., "Jane", "Doe", "jane.doe@example.com", "+1234567890", "1990-01-15", "Female", "2026-01-10", "Engineering", "Software Engineer", "Full-Time", "HQ", "active"). |
| 6 | Locate the "Download Excel Template" link in the Step 1 card. | A download link for Excel template is visible. |
| 7 | Click the Excel template download link. | A `.xlsx` file is downloaded. |
| 8 | Open the Excel file and inspect headers (row 1). | Headers match the same schema as the CSV template. |
| 9 | Inspect sample data rows in the Excel file. | At least one sample data row is present with realistic example values. |
| 10 | Verify a column description guide is available (either as a second sheet in Excel, a collapsible section on the page, or inline notes). | Descriptions indicate which fields are required, expected formats (e.g., "E.164 format" for phone, "YYYY-MM-DD" for dates), valid values for enums (e.g., "Full-Time/Part-Time/Contract/Intern" for employment_type), and that `department_name` and `job_title_name` must match existing tenant records. |

## 6. Postconditions
- No system state change; template downloads are read-only operations.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
