---
id: TC-CHR-172
user_story: US-CHR-007
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-172: Create a new office location with all fields (happy path)

## 1. Test Objective
Verify that a Tenant Admin can navigate to the Locations management page, click "Add Location", fill in all fields (name, address, time zone, phone, status), submit the form, and see the new location appear in the locations list with correct data. This validates AC-1 and AC-2.

## 2. Related Requirements
- User Story: US-CHR-007
- Acceptance Criteria: AC-1, AC-2
- Functional Requirements: FR-1, FR-3, FR-4
- Non-Functional Requirements: NFR-4
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active` and subdomain `acme.yourhrm.com`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- No location named "Colombo Head Office" exists in tenant "acme".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Full access |
| Location Name | Colombo Head Office | Required, unique per tenant |
| Address Line 1 | 42 Galle Road | Optional |
| Address Line 2 | Floor 5 | Optional |
| City | Colombo | Optional |
| State/Province | Western Province | Optional |
| Country | Sri Lanka | From standard ISO 3166 list |
| Postal Code | 00300 | Optional |
| Time Zone | Asia/Colombo | Required, IANA format |
| Phone | +94112345678 | Optional |
| Status | Active | Default true |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `https://acme.yourhrm.com/locations` | Locations management page loads with card-based table. |
| 2 | Click the "Add Location" button (top-right) | A slide-over panel or modal card opens with the location form. |
| 3 | Verify form fields are present | Form contains: Location Name (required), Address section (collapsible with street, city, state/province, country, postal code), Time Zone (required, searchable dropdown), Phone, and Status toggle. |
| 4 | Enter "Colombo Head Office" in the Location Name field | Field accepts input. |
| 5 | Expand the Address section and fill in all address fields | Address Line 1: "42 Galle Road", Address Line 2: "Floor 5", City: "Colombo", State/Province: "Western Province", Country: "Sri Lanka" (selected from searchable dropdown with flag icon), Postal Code: "00300". |
| 6 | Select "Asia/Colombo" from the Time Zone searchable dropdown | Time zone is selected. Common zones are highlighted at the top of the list. |
| 7 | Enter "+94112345678" in the Phone field | Field accepts input. |
| 8 | Leave Status as Active (default) | Status toggle is on/active. |
| 9 | Click "Save" or "Create" button | Form submits successfully. |
| 10 | Verify success feedback | A success toast notification appears (e.g., "Location created successfully"). |
| 11 | Verify the new location appears in the locations list | "Colombo Head Office" row/card shows: Name = "Colombo Head Office", City = "Colombo", Country = "Sri Lanka", Time Zone = "Asia/Colombo", Employee Count = 0, Status = Active. |
| 12 | Verify the API request `POST /api/v1/tenant/locations` was sent | Request body contains all submitted fields. Response status is 201 Created. Response body contains `location_id` (UUID), `tenant_id` matching the session tenant. |
| 13 | Verify an audit_log entry was created for the location creation | Audit log contains an entry with action "create", entity "location", and the new location ID. |

## 6. Postconditions
- A new location record "Colombo Head Office" exists in tenant "acme" with `is_active = true` and `is_deleted = false`.
- The location has auto-populated `created_at`, `created_by`, `updated_at`, `updated_by` audit columns.
- An audit_log entry records the creation event.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
