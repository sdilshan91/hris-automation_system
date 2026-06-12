---
id: TC-CHR-181
user_story: US-CHR-007
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-181: Tenant with zero locations operates without errors (BR-6)

## 1. Test Objective
Verify that a tenant with no locations defined can operate normally: the Locations management page shows an empty state, employee creation works without a location assignment, and the system does not produce errors when location-related features are accessed. This validates BR-6.

## 2. Related Requirements
- User Story: US-CHR-007
- Business Rules: BR-6

## 3. Preconditions
- Tenant "newcorp" exists with status `active` and subdomain `newcorp.yourhrm.com`.
- A user with Tenant Admin role is authenticated in the "newcorp" tenant context.
- No locations exist in tenant "newcorp".
- At least one employee exists in tenant "newcorp" (location assignment is null).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | newcorp.yourhrm.com | Active tenant, no locations |
| User Role | Tenant Admin | Full access |
| Location Count | 0 | No locations created |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Locations management page | Page loads with an empty state message (e.g., "No locations created yet") and an "Add Location" button. No error or crash. |
| 2 | Verify the `GET /api/v1/tenant/locations` API response | Response returns 200 OK with an empty array `[]` and total count = 0. |
| 3 | Navigate to the Employee creation form | Form loads. Location field shows an empty dropdown or "No locations available" placeholder. |
| 4 | Create an employee without selecting a location | Employee creation succeeds. The employee record has `location_id = null`. |
| 5 | Navigate to the Employee directory | Directory loads normally. Location filter dropdown is empty or shows "No locations". |
| 6 | Attempt to filter the directory by location | Filter applies without error; all employees are shown (since none have a location). |

## 6. Postconditions
- Tenant "newcorp" operates correctly with zero locations.
- No errors or crashes occurred in location-dependent features.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
