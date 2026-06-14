---
id: TC-LV-143
user_story: US-LV-007
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-143: Holiday CRUD lifecycle -- edit fields and deactivate (soft) excludes from calc and calendar (FR-1, BR-4)

## 1. Test Objective
Verify the full CRUD lifecycle scoped to the tenant: a holiday can be updated (name, date, type, location, description, recurring) and deactivated; once deactivated it is excluded from leave-day calculation and the default calendar/list views but retained for history (FR-1, BR-4).

## 2. Related Requirements
- User Story: US-LV-007
- Functional Requirements: FR-1, FR-2
- Business Rules: BR-4

## 3. Preconditions
- Tenant "acme" active; HR Officer "Priya" authenticated with `Holiday.Edit` / `Holiday.Deactivate` / `Holiday.View`.
- An active holiday "Provisional Holiday" on 2026-03-17 (Public, tenant-wide).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| New name | "St. Patrick's Day" | edit |
| New type | Restricted | edit |
| New description | "Observed regionally" | edit |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | PUT `/api/v1/holidays/{id}` updating name/type/description | 200 OK; the holiday reflects the new values; audit fields (UpdatedBy/UpdatedAt) stamped. |
| 2 | Re-list for 2026 | The updated holiday is shown with new name and type. |
| 3 | POST `/api/v1/holidays/{id}/deactivate` | 200 OK; `is_active=false`. The holiday no longer appears in the default (active-only) list and no longer excludes a leave day. |
| 4 | GET with `activeOnly=false` | The deactivated holiday is still retrievable for history (not destroyed). |

## 6. Postconditions
- Holiday updates persist; deactivation retains history while removing it from active behaviour.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
