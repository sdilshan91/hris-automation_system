---
id: TC-CHR-173
user_story: US-CHR-007
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-173: New location appears in employee assignment dropdowns and holiday calendar configuration

## 1. Test Objective
Verify that after creating a new location, it becomes immediately available in the employee assignment dropdown (when editing/creating an employee) and in the holiday calendar location-scoping configuration. This validates the second half of AC-2.

## 2. Related Requirements
- User Story: US-CHR-007
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1
- Business Rules: BR-2, BR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- A location "Colombo Head Office" has just been created (from TC-CHR-172 or equivalent setup).
- At least one employee exists in the "acme" tenant for testing the assignment dropdown.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Full access |
| Location Name | Colombo Head Office | Just created, active |
| Existing Employee | John Doe (EMP-0001) | Available for assignment |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Employee creation or edit form | Employee form loads with all fields including a Location dropdown. |
| 2 | Open the Location dropdown/selector | A searchable dropdown appears listing all active locations for the tenant. |
| 3 | Verify "Colombo Head Office" appears in the dropdown | "Colombo Head Office" is listed as an available option. |
| 4 | Select "Colombo Head Office" and save the employee record | Employee is assigned to "Colombo Head Office". API response confirms `location_id` is set. |
| 5 | Navigate back to the Locations list page | "Colombo Head Office" now shows Employee Count = 1. |
| 6 | Verify the employee's profile shows the assigned location | Employee profile/record displays "Colombo Head Office" as their primary location. |

## 6. Postconditions
- The employee "John Doe" is assigned to "Colombo Head Office".
- The location's employee count reflects the assignment.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
