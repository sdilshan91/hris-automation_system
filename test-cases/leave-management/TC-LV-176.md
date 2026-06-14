---
id: TC-LV-176
user_story: US-LV-009
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-176: HR Officer with Leave.ViewAll sees the entire organization's leave calendar (BR-3)

## 1. Test Objective
Verify a user holding the `Leave.ViewAll` permission (e.g. HR Officer) can view the whole tenant organization's leave calendar -- across all departments and managers -- rather than being scoped to a single team or department.

## 2. Related Requirements
- User Story: US-LV-009
- Business Rules: BR-3
- Functional Requirements: FR-1, FR-2
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme"; HR Officer "Priya" with `Leave.ViewAll`.
- Employees across multiple departments/teams (Sam, Ravi, Lena) have approved + pending leaves.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Permission | Leave.ViewAll | org-wide access |
| Expected | Sam, Ravi, Lena (all teams) | full org |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Priya loads the Team Leave Calendar | Leaves from all departments/teams appear (Sam, Ravi, Lena), not just one team. |
| 2 | Confirm detail level | As an all-access HR role, Priya sees full detail (leave type, status incl. pending) per BR-3 (org-wide manager-equivalent visibility). |
| 3 | Confirm tenant boundary still holds | Only acme employees appear; no other tenant's employees (cross-ref TC-LV-ISO-033). |
| 4 | Filter by department | The department filter narrows within the org but Priya can select any department. |

## 6. Postconditions
- HR Officer with Leave.ViewAll sees the whole tenant org's calendar; tenant boundary preserved.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
