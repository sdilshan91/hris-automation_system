---
id: TC-CHR-237
user_story: US-CHR-009
module: Core HR
priority: high
type: functional
status: pass
created: 2026-06-12
---

# TC-CHR-237: Suspended employee excluded from active headcount but data fully retained (BR-5)

## 1. Test Objective
Verify that a suspended employee is excluded from active headcount reports/queries but their data remains fully intact and accessible to HR Officers. This validates BR-5.

## 2. Related Requirements
- User Story: US-CHR-009
- Business Rules: BR-5
- Functional Requirements: FR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated in the "acme" tenant context.
- Employee "Frank Lee" (`emp-008-uuid`) exists with status `active`.
- Active headcount query currently includes this employee.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee | Frank Lee (emp-008-uuid) | Status: active |
| New Status | suspended | Valid transition |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Query active headcount: `GET /api/v1/tenant/employees?activeOnly=true`. Note the count. | Frank Lee appears in the active list. Count = N. |
| 2 | Change Frank Lee's status to `suspended`. | 200 OK. Status = suspended. |
| 3 | Query active headcount again: `GET /api/v1/tenant/employees?activeOnly=true`. | Frank Lee does NOT appear. Count = N - 1. |
| 4 | Query all employees (without activeOnly filter): `GET /api/v1/tenant/employees`. | Frank Lee DOES appear in the full list with status "suspended". |
| 5 | Navigate to Frank Lee's profile as HR Officer. | Profile loads completely. All personal data, employment history, documents, etc. are accessible. Nothing has been deleted or masked. |
| 6 | Verify the employee record in the database. | `employees` table: record exists with `status = suspended`, `is_deleted = false`. All data fields are intact. |

## 6. Postconditions
- Employee is suspended and excluded from active headcount.
- All employee data is fully retained and accessible.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

> **Execution 2026-06-30:** STILL BLOCKED — employee status-change is a profile/detail action reached via the Employee Directory (crashed, **BUG-099**), and this assertion (probation reminder job / future-dated background apply / suspended-excluded-from-headcount) is backend/job behavior needing a write + job execution, not a browser-render check.

> **Execution 2026-07-01 (API, acme, tenantadmin):** **PASS.** Subject EMP-0030 "Pq1 Test" (`019efd93-a863-7b69-b4a8-cf37f7f4d600`), originally Active — mutated then **restored to Active immediately**. Suspend (`POST /employees/{id}/status` `{newStatus:Suspended, effectiveDate:2026-07-01}`) → 200. Active headcount `?activeOnly=true` dropped 28→27 (suspended excluded from active headcount, BR-5 ✓). Full list `GET /employees` (34) still contains it with status "Suspended" — data retained, total unchanged, `isActive:false` but not deleted (soft-delete flag never set by status change). Profile `GET /{id}/profile` still loads fully (name intact). Restored to Active → headcount back to 28. (Note: status change requires `effectiveDate` — omitting it returns 400 "Effective date is required.")
