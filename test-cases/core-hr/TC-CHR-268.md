---
id: TC-CHR-268
user_story: US-CHR-011
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-268: Assign reporting manager to employee -- happy path

## 1. Test Objective
Verify that an HR Officer can assign a reporting manager to an employee via the employee profile Employment Details section, that the `reports_to_employee_id` FK is set on the employee record, that the manager's direct reports list includes the employee, and that the change is recorded in the employment history timeline with an audit log capturing before/after values. This validates AC-1, AC-2, FR-1, FR-2, and FR-6.

## 2. Related Requirements
- User Story: US-CHR-011
- Acceptance Criteria: AC-1, AC-2
- Functional Requirements: FR-1, FR-2, FR-6
- Non-Functional Requirements: NFR-5

## 3. Preconditions
- Tenant "acme" exists with status `active` and subdomain `acme.yourhrm.com`.
- An HR Officer user is authenticated in the "acme" tenant context.
- Employee E (e.g., "John Doe", status `active`) exists with no manager assigned.
- Employee M (e.g., "Sarah Manager", status `active`) exists and is a different employee from E.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona per US-CHR-011 |
| Employee E | John Doe (john.doe@acme.test) | No manager assigned initially |
| Manager M | Sarah Manager (sarah.mgr@acme.test) | Active employee |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Employee E's profile, Employment Details section. | The "Reporting Manager" field is visible and displays "Not Assigned". An edit button is present. |
| 2 | Click the edit button on the Reporting Manager field. | A manager selector modal/dropdown opens with employee search/autocomplete. Only active employees are listed. |
| 3 | Search for "Sarah Manager" in the autocomplete. | The search returns Manager M, showing avatar (32px), name, department, and job title. |
| 4 | Select Manager M and save the form. | A success toast appears. The Reporting Manager field now shows Manager M as a mini-card (avatar + name + job title). |
| 5 | Verify the API response or DB: `GET /api/v1/tenant/employees/{E.id}`. | The employee record has `reports_to_employee_id` set to Manager M's UUID. |
| 6 | Verify Manager M's direct reports: `GET /api/v1/tenant/employees/{M.id}/direct-reports`. | The response array contains Employee E with employee_id, name, job title, department, status, and avatar_url. |
| 7 | Check the employment history timeline on Employee E's profile. | A new entry exists showing "Reporting Manager changed from Not Assigned to Sarah Manager" with timestamp and acting officer. |
| 8 | Check the audit log for Employee E. | An audit record exists with before value (null/Not Assigned) and after value (Sarah Manager's ID/name). |

## 6. Postconditions
- Employee E has `reports_to_employee_id` = Manager M's UUID.
- Manager M's direct reports include Employee E.
- Employment history contains one entry for the manager assignment.
- Audit log contains one entry with before/after snapshot.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
