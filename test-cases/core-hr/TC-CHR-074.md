---
id: TC-CHR-074
user_story: US-CHR-001
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-074: Plan employee limit reached -- creation blocked (AC-5, FR-5)

## 1. Test Objective
Verify that when a tenant has reached its maximum employee count per subscription plan, the system blocks creation of a new employee and displays the message: "Employee limit reached for your current plan. Please upgrade or contact your administrator."

## 2. Related Requirements
- User Story: US-CHR-001
- Acceptance Criteria: AC-5
- Functional Requirements: FR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- The "acme" tenant's subscription plan has a maximum of 5 employees (MaxEmployees = 5).
- Exactly 5 active employees already exist in the "acme" tenant.
- A user with HR Officer role is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Plan limit | 5 | MaxEmployees = 5 |
| Current employees | 5 | At limit |
| New employee | Alice Johnson | 6th employee attempt |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify the tenant currently has 5 active employees | Employee count matches plan limit. |
| 2 | Navigate to the Employee module and click "Add Employee" | The system either: (a) prevents opening the wizard with the limit message, or (b) allows opening but blocks on submission. |
| 3 | If the wizard opens, fill in all mandatory fields for "Alice Johnson" and submit | Submission is blocked. |
| 4 | Verify the error message displayed is: "Employee limit reached for your current plan. Please upgrade or contact your administrator." | Exact message matches AC-5 specification. |
| 5 | Verify no new employee record was created in the database | Employee count remains 5. |
| 6 | Verify the API response is 403 or 422 with the plan-limit error code | API enforces the limit server-side (FR-5). |

## 6. Postconditions
- The tenant still has exactly 5 employees.
- No 6th employee record was created.
- The plan limit enforcement is server-side, not just client-side.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
