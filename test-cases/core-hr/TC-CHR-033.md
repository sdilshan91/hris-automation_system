---
id: TC-CHR-033
user_story: US-CHR-004
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-033: Deactivated department hidden from dropdowns but visible in admin view

## 1. Test Objective
Verify that deactivated departments are hidden from selection dropdowns (parent department picker, employee assignment picker) but remain visible in the admin department list with inactive status, per FR-7.

## 2. Related Requirements
- User Story: US-CHR-004
- Functional Requirements: FR-7
- Business Rules: BR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated.
- Department "Old Projects" exists and has been deactivated (`is_active = false`).
- Department "Engineering" exists and is active.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Deactivated Dept | Old Projects | is_active = false |
| Active Dept | Engineering | is_active = true |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the admin Departments management page | Both "Old Projects" (Status: Inactive) and "Engineering" (Status: Active) are visible. |
| 2 | Click "Add Department" and open the Parent Department dropdown | "Engineering" appears in the dropdown. "Old Projects" does NOT appear. |
| 3 | Click Edit on "Engineering" and open the Parent Department dropdown | "Old Projects" does NOT appear as a selectable parent option. |
| 4 | Verify that the API endpoint for listing departments in dropdowns/pickers filters out inactive departments | `GET /api/v1/departments?active_only=true` (or equivalent) does not include "Old Projects". |
| 5 | Verify that the admin list API includes inactive departments when requested | `GET /api/v1/departments?include_inactive=true` (or default admin view) includes "Old Projects" with `is_active: false`. |

## 6. Postconditions
- Deactivated departments are excluded from operational dropdowns.
- Admin views still show deactivated departments for management purposes.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
