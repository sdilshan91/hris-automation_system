---
id: TC-LV-009
user_story: US-LV-001
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-009: Reorder leave types via display_order

## 1. Test Objective
Verify that leave types can be reordered by updating their `display_order` field, and that the leave types list and employee-facing dropdowns reflect the updated order.

## 2. Related Requirements
- User Story: US-LV-001
- Functional Requirements: FR-3
- UI/UX Notes: Section 8 (drag-and-drop reordering)

## 3. Preconditions
- Tenant "acme" has three active leave types: "Annual Leave" (display_order = 1), "Sick Leave" (display_order = 2), "Casual Leave" (display_order = 3).
- A user with `Leave.Configure` permission is authenticated.

## 4. Test Data
| Leave Type | Original Order | New Order |
|------------|---------------|-----------|
| Annual Leave | 1 | 2 |
| Sick Leave | 2 | 3 |
| Casual Leave | 3 | 1 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Leave Types configuration page | Leave types listed in order: Annual Leave (1), Sick Leave (2), Casual Leave (3). |
| 2 | Drag "Casual Leave" to the first position (or use reorder controls) | UI shows new order: Casual Leave, Annual Leave, Sick Leave. |
| 3 | Observe API call to update display order | `PATCH /api/v1/leave-types/reorder` (or individual PATCH calls) with new display_order values. Response 200 OK. |
| 4 | Refresh the Leave Types page | Leave types displayed in new order: Casual Leave (1), Annual Leave (2), Sick Leave (3). |
| 5 | Verify `GET /api/v1/leave-types?sort=display_order` returns leave types in updated order | Response array: Casual Leave, Annual Leave, Sick Leave. |
| 6 | Verify employee leave application dropdown reflects the new order (FORWARD-LOOKING) | Dropdown lists types in display_order sequence. Mark DEFERRED if leave-request module not built. |

## 6. Postconditions
- `display_order` values in the database reflect the new ordering.
- All list views and dropdowns use `display_order` for sorting.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
