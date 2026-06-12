---
id: TC-CHR-269
user_story: US-CHR-011
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-269: Reporting Manager field displays current manager or "Not Assigned"

## 1. Test Objective
Verify that the Reporting Manager field on an employee's profile Employment Details section correctly displays the current manager as a mini-card (avatar + name + job title) when assigned, or "Not Assigned" text when no manager is set. This validates AC-1.

## 2. Related Requirements
- User Story: US-CHR-011
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated.
- Employee A exists with Manager M assigned (`reports_to_employee_id` = M.id).
- Employee B exists with no manager assigned (`reports_to_employee_id` = null).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee A | alice@acme.test | Has manager M assigned |
| Manager M | manager@acme.test | Active, job title "Engineering Manager" |
| Employee B | bob@acme.test | No manager assigned |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Employee A's profile, Employment Details section. | The "Reporting Manager" field displays Manager M as a mini-card with: avatar (32px), name, and job title ("Engineering Manager"). |
| 2 | Verify the mini-card is clickable. | Clicking the manager card navigates to Manager M's profile page or shows a preview. |
| 3 | Verify an edit button is present next to the manager field. | An edit icon/button is visible for the HR Officer role. |
| 4 | Navigate to Employee B's profile, Employment Details section. | The "Reporting Manager" field displays "Not Assigned" text. |
| 5 | Verify an edit button is present next to Employee B's manager field. | An edit icon/button is visible, allowing the HR Officer to assign a manager. |

## 6. Postconditions
- No state change; this is a read-only verification.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
