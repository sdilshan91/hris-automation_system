---
id: TC-CHR-299
user_story: US-CHR-012
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-299: Reorder custom fields via display_order

## 1. Test Objective
Verify that custom fields can be reordered on the management page (via drag-and-drop on desktop or arrow buttons on mobile) and that the new display_order is persisted. After reordering, employee creation and profile edit forms render the fields in the updated order. This validates FR-8.

## 2. Related Requirements
- User Story: US-CHR-012
- Functional Requirements: FR-8
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Three custom fields exist for Employee entity: "T-Shirt Size" (order 1), "Project Code" (order 2), "Union Membership" (order 3).
- Tenant Admin is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Field 1 | T-Shirt Size | display_order = 1 |
| Field 2 | Project Code | display_order = 2 |
| Field 3 | Union Membership | display_order = 3 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Settings > Custom Fields. | Fields listed in order: T-Shirt Size, Project Code, Union Membership. |
| 2 | Drag "Union Membership" above "T-Shirt Size" (or use up-arrow button). | The list reorders to: Union Membership, T-Shirt Size, Project Code. A save/confirmation toast appears. |
| 3 | Refresh the management page. | The order persists: Union Membership (order 1), T-Shirt Size (order 2), Project Code (order 3). |
| 4 | Navigate to the employee creation form. | The Custom Fields section shows fields in the new order: Union Membership, T-Shirt Size, Project Code. |
| 5 | Navigate to an existing employee profile edit form. | The Custom Fields section shows fields in the same updated order. |
| 6 | Verify via API: `GET /api/v1/tenant/custom-fields?entityType=Employee`. | Fields returned ordered by `display_order`: Union Membership (1), T-Shirt Size (2), Project Code (3). |

## 6. Postconditions
- Custom field display_order values are updated and persisted.
- Forms render fields in the new order.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
