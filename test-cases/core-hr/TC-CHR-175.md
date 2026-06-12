---
id: TC-CHR-175
user_story: US-CHR-007
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-175: Duplicate location name within same tenant is rejected

## 1. Test Objective
Verify that the system rejects creation of a location with a name that already exists (active) within the same tenant. The API should return a validation error and the UI should display an appropriate error message. This validates FR-2 and BR-1.

## 2. Related Requirements
- User Story: US-CHR-007
- Functional Requirements: FR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- Location "Colombo Head Office" already exists in tenant "acme" with `is_active = true`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Full access |
| Existing Location | Colombo Head Office | Already exists in this tenant |
| Duplicate Name | Colombo Head Office | Exact same name |
| Time Zone | Asia/Colombo | Required field |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Locations management page | Locations list loads; "Colombo Head Office" is visible. |
| 2 | Click "Add Location" | Location creation form opens. |
| 3 | Enter "Colombo Head Office" in the Location Name field | Field accepts input. |
| 4 | Select "Asia/Colombo" as the Time Zone | Time zone selected. |
| 5 | Click "Save" | Form submits to `POST /api/v1/tenant/locations`. |
| 6 | Verify the API returns an error | Response status is 409 Conflict or 422 Unprocessable Entity. Error body includes a message indicating the location name already exists (e.g., "A location with this name already exists"). |
| 7 | Verify the UI displays the error | An error toast or inline validation message appears indicating the duplicate name conflict. |
| 8 | Verify no new location was created | The locations list still shows only one "Colombo Head Office". |

## 6. Postconditions
- No duplicate location record was created.
- The existing "Colombo Head Office" location is unchanged.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
