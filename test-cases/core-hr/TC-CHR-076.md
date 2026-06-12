---
id: TC-CHR-076
user_story: US-CHR-001
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-076: Employee_no configurable pattern per tenant (FR-2)

## 1. Test Objective
Verify that the employee_no auto-generation pattern is configurable per tenant (e.g., "EMP-{NNNN}", "STAFF-{NNNN}", custom prefixes), and that changing the pattern applies to new employees without affecting existing ones.

## 2. Related Requirements
- User Story: US-CHR-001
- Acceptance Criteria: AC-2
- Functional Requirements: FR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active` and default pattern "EMP-{NNNN}".
- One employee already exists with employee_no "EMP-0001".
- A user with Tenant Admin role is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Default pattern | EMP-{NNNN} | Current tenant pattern |
| New pattern | STAFF-{NNNN} | Updated tenant pattern |
| Existing employee_no | EMP-0001 | Should remain unchanged |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify existing employee has employee_no "EMP-0001" | Confirmed. |
| 2 | As Tenant Admin, update the employee_no pattern to "STAFF-{NNNN}" | Pattern is saved successfully. |
| 3 | Create a new employee as HR Officer | Employee created. |
| 4 | Verify the new employee's employee_no uses the updated pattern | employee_no = "STAFF-0002" (sequence continues from where it left off). |
| 5 | Verify the existing employee's employee_no is still "EMP-0001" | Existing records are not retroactively changed. |

## 6. Postconditions
- New employees use the updated pattern.
- Existing employee_no values are unchanged.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
