---
id: TC-CHR-ISO-025
user_story: US-CHR-007
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-025: Tenant A cannot see Tenant B's locations

## 1. Test Objective
Verify that the Locations management feature is fully tenant-isolated: a user authenticated in Tenant A sees only Tenant A's locations. Zero locations from Tenant B appear in the list, search, or any API response. This tests EF Core global query filters and the `tenant_id` scoping per FR-8, NFR-2.

## 2. Related Requirements
- User Story: US-CHR-007
- Functional Requirements: FR-1, FR-8
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with locations: "Colombo Office", "London Branch".
- Tenant "globex" exists with locations: "New York HQ", "Tokyo Office".
- HR Officer is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Has Colombo Office, London Branch |
| Tenant B | globex | Has New York HQ, Tokyo Office |
| Auth Context | acme | HR Officer in Tenant A |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer in "acme" tenant | JWT contains tenant_id for acme. |
| 2 | Send `GET /api/v1/tenant/locations` | Response returns only "Colombo Office" and "London Branch". No "New York HQ" or "Tokyo Office". |
| 3 | Verify the response body contains exactly 2 locations | Total count = 2. Both location records have `tenant_id` matching acme. |
| 4 | Send `GET /api/v1/tenant/locations/{globex-location-id}` (using the ID of globex's "New York HQ") | Response returns 404 Not Found (the EF global query filter excludes it). Not 403 (to avoid confirming the ID exists). |
| 5 | Navigate to the Locations management page in the UI | Only "Colombo Office" and "London Branch" are visible. No globex locations. |
| 6 | Switch to "globex" tenant context and send `GET /api/v1/tenant/locations` | Response returns only "New York HQ" and "Tokyo Office". Zero acme locations. |
| 7 | From globex context, attempt to access acme's "Colombo Office" by ID | Response returns 404 Not Found. |

## 6. Postconditions
- No cross-tenant data exposure occurred.
- EF Core global query filters correctly scoped all location queries by tenant_id.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
