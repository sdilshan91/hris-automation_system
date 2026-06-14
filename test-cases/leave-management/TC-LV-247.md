---
id: TC-LV-247
user_story: US-LV-012
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-247: Role-based access — an employee sees only their own data in reports (BR-2, Test Hint)

## 1. Test Objective
Verify BR-2 (employee branch): a plain employee (no reports/HR/manager permission) can only see their own data in any report they can access, and cannot read colleagues' balances or absenteeism via the report APIs.

## 2. Related Requirements
- User Story: US-LV-012
- Business Rules: BR-2
- Cross-ref: US-LV-006 (self-service balance)

## 3. Preconditions
- Tenant "acme"; employee "Mark" with no `Leave.Reports`/manager scope; other employees exist.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Role | Employee (self only) | no reports permission |
| Self | Mark | expected visible |
| Others | colleagues | must be excluded |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Mark, access any report the employee role is allowed to view | Only Mark's own rows appear (or he is redirected to his US-LV-006 dashboard); no colleague data. |
| 2 | Tamper: request another employee's `employeeId` in the report query | The server returns only Mark's own data or 403/404 — never the other employee's (no IDOR; cross-ref TC-LV-252). |
| 3 | Attempt an HR-only report (Absenteeism/Utilization full-tenant) | Denied with 403 (lacks `Leave.Reports`). |
| 4 | Confirm export scope | Any export Mark can perform contains only his own rows. |

## 6. Postconditions
- Employees see/export only their own data; cross-employee access is denied.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
