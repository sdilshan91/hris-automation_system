---
id: TC-CHR-276
user_story: US-CHR-011
module: Core HR
priority: high
type: functional
status: fail
created: 2026-06-12
---

# TC-CHR-276: Employee with no manager (null FK) works and appears as org-tree root

## 1. Test Objective
Verify that an employee can exist without a reporting manager (`reports_to_employee_id` = null), that this state does not cause errors, and that such employees appear as root nodes in the org tree reporting structure view. This validates FR-8.

## 2. Related Requirements
- User Story: US-CHR-011
- Functional Requirements: FR-8
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated.
- Employee R (e.g., "CEO") exists with `reports_to_employee_id` = null.
- At least one other employee exists with a manager assigned.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee R | ceo@acme.test | No manager (null FK), acts as root |
| Employee E | emp@acme.test | Reports to R |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | View Employee R's profile, Employment Details section. | Reporting Manager field shows "Not Assigned". No errors on page load. |
| 2 | Query Employee R via API: `GET /api/v1/tenant/employees/{R.id}`. | `reports_to_employee_id` is null. Response is 200 OK with complete employee data. |
| 3 | Navigate to the org tree page and switch to the "Reporting Structure" view. | The reporting structure view loads without errors. |
| 4 | Verify Employee R appears as a root node. | Employee R is displayed at the top level (no parent node above). |
| 5 | Verify Employee E appears under Employee R in the reporting tree. | Employee E is shown as a child of Employee R. |
| 6 | Verify that no "Not Found" or null-reference errors appear in the browser console. | Console is clean of JavaScript errors related to null manager. |

## 6. Postconditions
- No state change; read-only verification.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

> **Execution 2026-06-30 (FE, acme):** STILL BLOCKED — requires the employee profile/detail view and/or the org-tree hierarchy to verify the reporting-manager field / null-root / unlimited-reports / hierarchy / breadcrumb. The Employee Directory list is crashed (**BUG-099**) so profiles aren't reachable by in-app click, and the org tree renders no nodes (**ISSUE-207**). The "My Team" view (`/employees/my-team`) renders correctly with an empty-state for tenantadmin. Not separately runnable in this FE sweep.

> **Execution 2026-07-01 (API, acme, tenantadmin):** **FAIL (step 2) — ISSUE-218.** The org-tree reporting view DOES render null-manager employees as root nodes (17 roots with `parentId:null`), so the "appears as org-tree root" behaviour (steps 3-5) is correct. But step 2 fails: `GET /employees/{id}` response has NO `reports_to_employee_id` field at all (see ISSUE-218) — a client cannot read the null-manager state from the employee detail endpoint. Employee load itself is 200/clean. Steps 1/6 are FE (profile page render, console). Marked fail on the concrete API-arm miss.
