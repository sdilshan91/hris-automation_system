---
id: TC-CHR-075
user_story: US-CHR-001
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-075: Custom fields persisted to JSONB and shown on profile (AC-6)

## 1. Test Objective
Verify that when an HR Officer fills custom fields configured by the Tenant Admin during employee creation, the values are persisted in the `custom_fields` JSONB column and are visible on the employee's profile page.

## 2. Related Requirements
- User Story: US-CHR-001
- Acceptance Criteria: AC-6
- Functional Requirements: FR-9

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Tenant Admin has configured 3 custom fields: "Blood Type" (dropdown: A, B, AB, O), "LinkedIn URL" (text), "T-Shirt Size" (dropdown: S, M, L, XL).
- A user with HR Officer role is authenticated in the "acme" tenant context.
- Department and job title exist in the tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Custom field 1 | Blood Type = "O" | Dropdown custom field |
| Custom field 2 | LinkedIn URL = "https://linkedin.com/in/johndoe" | Text custom field |
| Custom field 3 | T-Shirt Size = "L" | Dropdown custom field |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Employee module and click "Add Employee" | Wizard opens. |
| 2 | Verify that custom fields (Blood Type, LinkedIn URL, T-Shirt Size) are rendered dynamically within the form | Custom fields appear in the appropriate section with correct input types (dropdowns, text). |
| 3 | Fill in all mandatory fields plus: Blood Type = "O", LinkedIn URL = "https://linkedin.com/in/johndoe", T-Shirt Size = "L" | All fields accept values. |
| 4 | Submit the form | Employee created successfully (201 Created). |
| 5 | Query the database: `SELECT custom_fields FROM employees WHERE employee_id = '{new_id}'` | JSONB column contains: `{"blood_type": "O", "linkedin_url": "https://linkedin.com/in/johndoe", "tshirt_size": "L"}` (or equivalent schema). |
| 6 | Navigate to the newly created employee's profile page | Profile page loads. |
| 7 | Verify custom fields are displayed on the profile | Blood Type shows "O", LinkedIn URL shows the URL (possibly as a link), T-Shirt Size shows "L". |

## 6. Postconditions
- Employee record has custom_fields JSONB column populated with the 3 configured values.
- Custom field values are visible on the employee profile page.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
