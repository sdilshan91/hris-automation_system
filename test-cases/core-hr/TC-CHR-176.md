---
id: TC-CHR-176
user_story: US-CHR-007
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-176: Same location name allowed in different tenants

## 1. Test Objective
Verify that the location name uniqueness constraint is scoped to a single tenant: two different tenants can each have a location named "Head Office" without conflict. This validates FR-2 and BR-1.

## 2. Related Requirements
- User Story: US-CHR-007
- Functional Requirements: FR-2
- Non-Functional Requirements: NFR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`. A location named "Head Office" exists in "acme".
- Tenant "globex" exists with status `active`. No location named "Head Office" exists in "globex".
- A user with Tenant Admin role is authenticated in the "globex" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Already has "Head Office" |
| Tenant B | globex | Does not have "Head Office" |
| Auth Context | globex | Tenant Admin in globex |
| Location Name | Head Office | Same name as Tenant A's location |
| Time Zone | America/New_York | Required field |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Tenant Admin in "globex" tenant | JWT contains tenant_id for globex. |
| 2 | Navigate to the Locations management page | Locations list for "globex" loads. "Head Office" is NOT listed (it belongs to "acme"). |
| 3 | Click "Add Location" | Location creation form opens. |
| 4 | Enter "Head Office" in the Location Name field | Field accepts input. |
| 5 | Select "America/New_York" as the Time Zone | Time zone selected. |
| 6 | Click "Save" | Form submits via `POST /api/v1/tenant/locations`. |
| 7 | Verify success | Response status is 201 Created. Location "Head Office" is created in tenant "globex" with a new `location_id` and `tenant_id` for globex. |
| 8 | Verify the new location appears in the globex locations list | "Head Office" is listed with the correct data. |
| 9 | Switch to "acme" tenant context and verify its "Head Office" is unchanged | Acme's "Head Office" still exists with its original data. Both tenants now have a "Head Office" independently. |

## 6. Postconditions
- Tenant "acme" has one "Head Office" location.
- Tenant "globex" has one "Head Office" location.
- Both locations have distinct `location_id` and `tenant_id` values.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
