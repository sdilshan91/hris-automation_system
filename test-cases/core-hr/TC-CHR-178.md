---
id: TC-CHR-178
user_story: US-CHR-007
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-178: Deactivation blocked when location has active employees assigned

## 1. Test Objective
Verify that when a Tenant Admin attempts to deactivate a location that has active employees assigned to it, the system blocks the deactivation and displays a warning message stating how many active employees are at the location. This validates AC-3 and FR-5.

## 2. Related Requirements
- User Story: US-CHR-007
- Acceptance Criteria: AC-3
- Functional Requirements: FR-5, FR-7
- Business Rules: BR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- Location "London Office" exists in tenant "acme" with `is_active = true`.
- 3 active employees are assigned to "London Office": "Alice Adams", "Bob Baker", "Carol Chen".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Full access |
| Location Name | London Office | Has 3 active employees |
| Active Employee Count | 3 | Alice Adams, Bob Baker, Carol Chen |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Locations management page | Locations list loads. "London Office" shows Employee Count badge = 3. |
| 2 | Click the "Deactivate" action on "London Office" | A confirmation dialog or warning appears. |
| 3 | Verify the warning message content | The system displays: "This location has 3 active employees. Reassign them before deactivating." (exact count from FR-7). |
| 4 | Verify the deactivation is blocked | The "Confirm Deactivate" button is either disabled or absent. Alternatively, if the user proceeds, the API returns a 409 Conflict or 422 with error code indicating active employees exist. |
| 5 | Verify the location remains active | "London Office" still shows `is_active = true` in the locations list. |
| 6 | Verify the API response (if request was sent) | `POST /api/v1/tenant/locations/{id}/deactivate` returns an error response with the employee count and a descriptive message. |
| 7 | Verify no audit_log entry for deactivation was created | No deactivation audit event exists for this location. |

## 6. Postconditions
- Location "London Office" remains active.
- All 3 employees remain assigned to "London Office".
- No deactivation audit entry was recorded.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
