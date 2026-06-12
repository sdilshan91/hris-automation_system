---
id: TC-CHR-050
user_story: US-CHR-005
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-050: Employment types reference entity supports defined values

## 1. Test Objective
Verify that the system provides and correctly exposes the employment type reference values (Full-Time, Part-Time, Contract, Intern) as specified in FR-6, and that they are usable alongside job titles.

## 2. Related Requirements
- User Story: US-CHR-005
- Functional Requirements: FR-6

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- Employment types are seeded or configured in the system.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Employment Type 1 | Full-Time | Standard reference value |
| Employment Type 2 | Part-Time | Standard reference value |
| Employment Type 3 | Contract | Standard reference value |
| Employment Type 4 | Intern | Standard reference value |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/employment-types` (or equivalent reference data endpoint) | Response status is 200 OK. Response body contains exactly four employment types: "Full-Time", "Part-Time", "Contract", "Intern". |
| 2 | Verify each employment type has an identifier (ID or enum value) and a display name | Each entry has a consistent structure (e.g., `{ id: ..., name: "Full-Time" }`). |
| 3 | Verify the employment types are available in the UI as a dropdown or selection when relevant (e.g., in an employee form, if available) | The four types are listed in the dropdown. |
| 4 | Verify that employment types are not tenant-specific (they are system reference data or enum) | The same values are available across all tenants. |
| 5 | Verify that new employment types cannot be created via the API (if they are a fixed reference/enum) | A `POST` to the employment types endpoint returns 404 or 405 (endpoint does not exist for creation), or the feature is restricted. |

## 6. Postconditions
- No data is modified; this is a read-only verification of reference data.
- Employment types remain available for use alongside job titles.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
