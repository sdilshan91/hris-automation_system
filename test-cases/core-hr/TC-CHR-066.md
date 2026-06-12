---
id: TC-CHR-066
user_story: US-CHR-001
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-066: Employee_no auto-generation pattern and per-tenant sequence isolation (FR-2, BR-1)

## 1. Test Objective
Verify that the system auto-generates a unique employee_no per tenant using a configurable pattern (e.g., "EMP-0001"), that the sequence is isolated per tenant (Tenant A and Tenant B both start at 0001), and that the employee_no is unique within a tenant but may repeat across tenants.

## 2. Related Requirements
- User Story: US-CHR-001
- Acceptance Criteria: AC-2
- Functional Requirements: FR-2
- Business Rules: BR-1

## 3. Preconditions
- Two tenants exist: "acme" and "globex", both with status `active`.
- HR Officer users exist in both tenants.
- No employees exist in either tenant (fresh state).
- Both tenants use the default pattern "EMP-{NNNN}".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme.yourhrm.com | Active tenant |
| Tenant B | globex.yourhrm.com | Active tenant |
| Employee 1 (Tenant A) | John Doe | First employee in Tenant A |
| Employee 2 (Tenant A) | Jane Smith | Second employee in Tenant A |
| Employee 1 (Tenant B) | Bob Wilson | First employee in Tenant B |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer in Tenant A ("acme") | Session established in Tenant A context. |
| 2 | Create employee "John Doe" in Tenant A | Employee created with employee_no = "EMP-0001". |
| 3 | Create employee "Jane Smith" in Tenant A | Employee created with employee_no = "EMP-0002". Sequence increments correctly. |
| 4 | Authenticate as HR Officer in Tenant B ("globex") | Session established in Tenant B context. |
| 5 | Create employee "Bob Wilson" in Tenant B | Employee created with employee_no = "EMP-0001". Tenant B sequence starts at 0001, independent of Tenant A. |
| 6 | Verify Tenant A employee_no values: "EMP-0001", "EMP-0002" | Both unique within Tenant A. |
| 7 | Verify Tenant B employee_no value: "EMP-0001" | Unique within Tenant B; same value as Tenant A's first employee (allowed per BR-1). |
| 8 | Verify database: each tenant has its own sequence counter | Sequence isolation confirmed at database level. |

## 6. Postconditions
- Tenant A has 2 employees with employee_no "EMP-0001" and "EMP-0002".
- Tenant B has 1 employee with employee_no "EMP-0001".
- Sequences are isolated per tenant.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
