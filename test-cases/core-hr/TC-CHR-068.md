---
id: TC-CHR-068
user_story: US-CHR-001
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-068: Same email allowed in different tenant (AC-3, BR-2)

## 1. Test Objective
Verify that the same email address can be used for employees in different tenants, confirming that email uniqueness is scoped to the tenant only, not globally. This validates correct multi-tenant isolation of the uniqueness constraint.

## 2. Related Requirements
- User Story: US-CHR-001
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3
- Business Rules: BR-2
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Two tenants exist: "acme" and "globex", both with status `active`.
- HR Officer users exist in both tenants.
- An employee with email "john.doe@example.com" already exists in the "acme" tenant.
- No employee with email "john.doe@example.com" exists in the "globex" tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme.yourhrm.com | Has employee with john.doe@example.com |
| Tenant B | globex.yourhrm.com | No employee with this email |
| email | john.doe@example.com | Same email, different tenant |
| first_name | Bob | Different person in Tenant B |
| last_name | Wilson | Different person in Tenant B |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer in Tenant B ("globex") | Session established in Tenant B context. |
| 2 | Navigate to the Employee module and click "Add Employee" | Multi-step wizard opens. |
| 3 | Fill in first_name = "Bob", last_name = "Wilson", email = "john.doe@example.com" | Fields accept the values. |
| 4 | Fill in all other mandatory fields | Fields populated with valid Tenant B data. |
| 5 | Submit the form | Employee is created successfully. Response is 201 Created. |
| 6 | Verify the new employee "Bob Wilson" exists in Tenant B with email "john.doe@example.com" | Record exists with correct tenant_id for Tenant B. |
| 7 | Verify Tenant A still has its own "John Doe" with the same email | Tenant A's data is unchanged. |
| 8 | Verify database: both records have different tenant_id values but the same email | Unique constraint is scoped to tenant, not global. |

## 6. Postconditions
- Tenant A has an employee with email "john.doe@example.com" (John Doe).
- Tenant B has a separate employee with the same email "john.doe@example.com" (Bob Wilson).
- No cross-tenant data leakage.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
