---
id: TC-LV-010
user_story: US-LV-001
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-010: Gender-specific leave type only shown to matching gender employees

## 1. Test Objective
Verify that a leave type configured with a specific gender applicability (e.g., "female" for Maternity Leave) only appears in the leave application dropdown for employees matching that gender. Employees of other genders should not see the type.

## 2. Related Requirements
- User Story: US-LV-001
- Functional Requirements: FR-2
- Business Rules: BR-4

## 3. Preconditions
- Tenant "acme" has an active leave type "Maternity Leave" with `gender = female`.
- Tenant "acme" has an active leave type "Paternity Leave" with `gender = male`.
- Tenant "acme" has an active leave type "Annual Leave" with `gender = all`.
- Employee "Jane Doe" exists with gender = female.
- Employee "John Smith" exists with gender = male.

## 4. Test Data
| Leave Type | Gender | Jane (female) sees? | John (male) sees? |
|------------|--------|---------------------|-------------------|
| Annual Leave | all | Yes | Yes |
| Maternity Leave | female | Yes | No |
| Paternity Leave | male | No | Yes |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As HR Officer, create leave type "Maternity Leave" with gender = "female", 90 days entitlement | Leave type created successfully with `gender: "female"`. |
| 2 | Create leave type "Paternity Leave" with gender = "male", 14 days entitlement | Leave type created with `gender: "male"`. |
| 3 | Call `GET /api/v1/leave-types?active=true&employee_id={jane_id}` (or equivalent endpoint that filters by employee gender) (FORWARD-LOOKING) | Response includes "Annual Leave" and "Maternity Leave" but NOT "Paternity Leave". Mark DEFERRED if leave-request filtering not built. |
| 4 | Call `GET /api/v1/leave-types?active=true&employee_id={john_id}` (FORWARD-LOOKING) | Response includes "Annual Leave" and "Paternity Leave" but NOT "Maternity Leave". Mark DEFERRED if not built. |
| 5 | Verify the Leave Types configuration list (admin view) shows ALL types regardless of gender filter | HR Officer sees Annual Leave, Maternity Leave, and Paternity Leave in the admin list. Gender is displayed as a column/badge. |

## 6. Postconditions
- Gender-specific leave types are correctly filtered based on employee gender.
- Admin configuration view remains unfiltered.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
