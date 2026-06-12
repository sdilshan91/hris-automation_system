---
id: TC-CHR-179
user_story: US-CHR-007
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-179: Deactivate location with no active employees succeeds (soft delete)

## 1. Test Objective
Verify that deactivating a location with zero active employees succeeds, setting `is_active = false` via soft delete. The deactivated location remains visible in admin views but is hidden from assignment dropdowns. An audit log entry is recorded. This validates FR-5, FR-6, and NFR-4.

## 2. Related Requirements
- User Story: US-CHR-007
- Functional Requirements: FR-5, FR-6
- Non-Functional Requirements: NFR-4
- Business Rules: BR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- Location "Old Warehouse" exists in tenant "acme" with `is_active = true` and 0 active employees assigned.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Full access |
| Location Name | Old Warehouse | No active employees |
| Employee Count | 0 | No employees assigned |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Locations management page | Locations list loads. "Old Warehouse" shows Employee Count = 0. |
| 2 | Click the "Deactivate" action on "Old Warehouse" | A confirmation dialog appears (e.g., "Are you sure you want to deactivate Old Warehouse?"). |
| 3 | Confirm the deactivation | API request `POST /api/v1/tenant/locations/{id}/deactivate` is sent. |
| 4 | Verify success response | Response status is 200 OK. The location's `is_active` is now `false`. |
| 5 | Verify the locations list reflects the deactivation | "Old Warehouse" shows status = Inactive (e.g., grayed out or with an "Inactive" badge). It remains visible in the admin list. |
| 6 | Navigate to the Employee creation form and open the Location dropdown | "Old Warehouse" does NOT appear in the location assignment dropdown (BR-5). |
| 7 | Query the audit_log | An audit entry exists with action = "deactivate", entity = "location", entity_id = location ID. |

## 6. Postconditions
- Location "Old Warehouse" has `is_active = false` and `is_deleted = false` (soft delete, not hard delete).
- The location is hidden from employee assignment dropdowns.
- An audit_log entry records the deactivation event.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
