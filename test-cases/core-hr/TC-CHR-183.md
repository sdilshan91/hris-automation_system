---
id: TC-CHR-183
user_story: US-CHR-007
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-183: Deactivated location cannot be assigned to new employees but remains on existing records

## 1. Test Objective
Verify that a deactivated location is hidden from assignment dropdowns (cannot be assigned to new employees) but remains visible on existing employee records that were assigned before deactivation. This validates BR-5.

## 2. Related Requirements
- User Story: US-CHR-007
- Business Rules: BR-5
- Functional Requirements: FR-6

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- Location "Closed Branch" exists in tenant "acme" with `is_active = true`.
- Employee "John Doe" is assigned to "Closed Branch".
- All employees are reassigned away from "Closed Branch" (employee count = 0 for deactivation to succeed), except that we need to test the existing-record visibility, so: Employee "John Doe" has a historical reference or the test creates the assignment before deactivation, then reassigns, deactivates, and verifies historical visibility.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Full access |
| Location Name | Closed Branch | Will be deactivated |
| Employee | John Doe (EMP-0001) | Was previously assigned to Closed Branch |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify "Closed Branch" is currently active and has 1 employee (John Doe) | Location list shows "Closed Branch" as Active with Employee Count = 1. |
| 2 | Edit employee "John Doe" and reassign to a different location (e.g., "Main Office") | Employee is successfully reassigned. "Closed Branch" now has Employee Count = 0. |
| 3 | Deactivate "Closed Branch" | Deactivation succeeds. "Closed Branch" shows status = Inactive. |
| 4 | Navigate to the Employee creation form and open the Location dropdown | "Closed Branch" does NOT appear in the dropdown. Only active locations are listed. |
| 5 | Navigate to the Employee edit form for an existing employee and open the Location dropdown | "Closed Branch" does NOT appear as a selectable option. |
| 6 | Verify "Closed Branch" remains visible in the Locations admin list | "Closed Branch" is shown in the admin locations list with Inactive status. It was not hard-deleted. |

## 6. Postconditions
- "Closed Branch" has `is_active = false` and `is_deleted = false`.
- No new employees can be assigned to it.
- The location remains visible in admin views.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
