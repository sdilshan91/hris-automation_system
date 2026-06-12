---
id: TC-CHR-278
user_story: US-CHR-011
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-278: Assign then reassign manager -- 2 employment history entries with before/after

## 1. Test Objective
Verify that assigning a manager and then reassigning to a different manager produces 2 separate employment history entries, each with before and after values correctly capturing the transition. This validates NFR-5 and FR-6, and follows the test hint from Section 11.

## 2. Related Requirements
- User Story: US-CHR-011
- Functional Requirements: FR-6
- Non-Functional Requirements: NFR-5
- Acceptance Criteria: AC-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated.
- Employee E exists with no manager (`reports_to_employee_id` = null).
- Manager M1 and Manager M2 exist with status `active`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee E | emp@acme.test | No manager initially |
| Manager M1 | mgr1@acme.test | First manager |
| Manager M2 | mgr2@acme.test | Second manager (reassignment) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Assign Manager M1 to Employee E via the profile editor. | Save succeeds. `reports_to_employee_id` = M1.id. |
| 2 | Check employment history for Employee E. | One entry: before = "Not Assigned" / null, after = "M1" / M1.id, with timestamp and acting HR officer. |
| 3 | Check audit log for Employee E. | One audit entry with before/after snapshot for the manager field. |
| 4 | Now reassign Employee E's manager from M1 to M2. | Save succeeds. `reports_to_employee_id` = M2.id. |
| 5 | Check employment history for Employee E. | Two entries total: (1) null -> M1, (2) M1 -> M2. Both have timestamps and acting officer. The second entry's before value is M1 and after value is M2. |
| 6 | Check audit log for Employee E. | Two audit entries total, each with correct before/after snapshots. |
| 7 | Verify M1's direct reports no longer include E. | `GET /api/v1/tenant/employees/{M1.id}/direct-reports` does not include E. |
| 8 | Verify M2's direct reports include E. | `GET /api/v1/tenant/employees/{M2.id}/direct-reports` includes E. |

## 6. Postconditions
- Employee E has `reports_to_employee_id` = M2.id.
- Employment history contains 2 entries.
- Audit log contains 2 entries.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
