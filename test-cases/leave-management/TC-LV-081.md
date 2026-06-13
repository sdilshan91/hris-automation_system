---
id: TC-LV-081
user_story: US-LV-004
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-081: Manager scope -- Manager A sees only their direct reports, not Manager B's team

## 1. Test Objective
Verify that the pending queue is strictly scoped to the authenticated manager's direct reports (`manager_employee_id`): Manager A cannot see pending requests belonging to Manager B's team, even within the same tenant. (Test Hint: Manager A should only see requests from their direct reports, not from Manager B's team.)

## 2. Related Requirements
- User Story: US-LV-004
- Functional Requirements: FR-1
- Non-Functional Requirements: NFR-3 (manager scope limited to direct reports)
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" is active (single tenant -- this is an intra-tenant scope test, not cross-tenant).
- Manager A ("Robert Lee") and Manager B ("Sara Kim") both have `Leave.Approve.Team`.
- Robert's direct reports: Jane Smith, Alan Park (each with pending requests).
- Sara's direct reports: Mike Olsen, Lena Vo (each with pending requests).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Manager A | Robert Lee | Reports: Jane, Alan |
| Manager B | Sara Kim | Reports: Mike, Lena |
| Robert pending | 2 (Jane, Alan) | Visible to Robert only |
| Sara pending | 2 (Mike, Lena) | Visible to Sara only |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Robert, call `GET /api/v1/leaves/pending` | Returns exactly Jane's and Alan's pending requests; Mike's and Lena's requests are absent. |
| 2 | As Sara, call `GET /api/v1/leaves/pending` | Returns exactly Mike's and Lena's pending requests; Jane's and Alan's are absent. |
| 3 | As Robert, attempt to filter by Mike's employee id (`?employeeId={mike}`) | Returns an empty result -- Mike is not Robert's direct report (BR-1); no leakage. |
| 4 | As Robert, attempt `GET /api/v1/leaves/pending/{mike_request_id}` for a known request id from Sara's team | Response 404/403 -- not within Robert's scope. |
| 5 | Verify the scope is enforced server-side | The direct-reports filter is applied in the query (`manager_employee_id = managerEmployeeId`), not merely hidden client-side. |

## 6. Postconditions
- No data mutated.
- Each manager sees only their own direct reports' pending requests; no intra-tenant cross-team leakage.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
