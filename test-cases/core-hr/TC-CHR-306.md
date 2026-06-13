---
id: TC-CHR-306
user_story: US-CHR-012
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-306: Dropdown options -- adding succeeds; removing in-use option shows warning

## 1. Test Objective
Verify that dropdown options can be added to an existing custom field definition freely, but removing an option that is currently in use by employee records triggers a warning and blocks the removal. This validates BR-6.

## 2. Related Requirements
- User Story: US-CHR-012
- Business Rules: BR-6

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Custom field "T-Shirt Size" (dropdown) exists with options ["S", "M", "L", "XL"].
- Employee "Jane" has tshirt_size = "L", Employee "John" has tshirt_size = "M".
- Tenant Admin is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Custom Field | T-Shirt Size | Options: S, M, L, XL |
| In-use options | L (Jane), M (John) | These cannot be removed |
| Not-in-use options | S, XL | These can potentially be removed |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Settings > Custom Fields. Click edit on "T-Shirt Size". | The edit form shows existing options as chips: S, M, L, XL. |
| 2 | Add a new option "XXL" via the tag input. Save. | Success. Options are now: S, M, L, XL, XXL. |
| 3 | Attempt to remove the "L" option chip (click the remove icon). | A warning is displayed: "This option is currently in use by 1 employee record. It cannot be removed while in use." (or similar). The removal is blocked. |
| 4 | Attempt to remove the "M" option chip. | Same warning: option is in use by 1 employee record. Removal blocked. |
| 5 | Attempt to remove the "S" option chip (not in use). | The option is removed (or a confirmation is shown and proceeds). Save succeeds. Options are now: M, L, XL, XXL. |
| 6 | Verify via API that options are updated correctly. | `GET /api/v1/tenant/custom-fields/{id}` shows options: ["M", "L", "XL", "XXL"]. |

## 6. Postconditions
- In-use dropdown options cannot be removed.
- Options not in use can be removed.
- New options can be added freely.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
