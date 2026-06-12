---
id: TC-CHR-139
user_story: US-CHR-003
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-139: Manager sees only reporting chain (BR-2) -- deferred but visibility tier applied

## 1. Test Objective
Verify that a Manager-role user sees the Manager visibility tier (same fields as HR Officer) but acknowledge the deferred reporting-chain scope (BR-2). Currently, all tenant employees are visible to Managers. When the reporting hierarchy is implemented, this test must be extended to verify filtering to direct/indirect reports only.

## 2. Related Requirements
- User Story: US-CHR-003
- Business Rules: BR-2
- Functional Requirements: FR-9

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Manager role is authenticated in "acme" (has `Employee.View.Team` permission).
- 30 employees exist; 5 would be in the manager's reporting chain (if implemented).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User Role | Manager | Employee.View.Team permission |
| Visible fields | All fields including email, phone, dateOfJoining | Same as Full tier |
| Scope | All tenant employees (deferred) | BR-2 deferred; no ManagerId filtering |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Manager in "acme" tenant | JWT contains Manager-level permissions including `Employee.View.Team`. |
| 2 | Send `GET /api/v1/tenant/employees/directory?page=1&pageSize=20` | Response is 200 OK. |
| 3 | Verify field visibility | All fields are present including email, phone, dateOfJoining, employmentType (Manager tier = Full visibility). |
| 4 | Verify scope (current behavior) | All 30 tenant employees are returned (reporting chain filtering deferred). |
| 5 | Document: when `Employee.ReportsToEmployeeId` is added, re-test | Only direct/indirect reports should appear. |

## 6. Postconditions
- No data was modified.
- Reporting chain filtering is deferred; test documents the current expected behavior.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
