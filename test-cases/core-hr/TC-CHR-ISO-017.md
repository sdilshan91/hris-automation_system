---
id: TC-CHR-ISO-017
user_story: US-CHR-003
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-017: Tenant A directory shows zero Tenant B employees

## 1. Test Objective
Verify that the Employee Directory is fully tenant-isolated: a user authenticated in Tenant A sees only Tenant A's employees. Zero employees from Tenant B appear in the directory listing, search results, or exports. This tests EF Core global query filters and the `tenant_id` scoping per NFR-3, FR-7.

## 2. Related Requirements
- User Story: US-CHR-003
- Functional Requirements: FR-7
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" exists with employees: "Alice Adams", "Bob Baker" (both active).
- Tenant "globex" exists with employees: "Carol Chen", "Dave Daniels" (both active).
- HR Officer is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Has Alice Adams, Bob Baker |
| Tenant B | globex | Has Carol Chen, Dave Daniels |
| Auth Context | acme | HR Officer in Tenant A |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer in "acme" tenant | JWT contains tenant_id for acme. |
| 2 | Send `GET /api/v1/tenant/employees/directory?page=1&pageSize=50` | Response returns only "Alice Adams" and "Bob Baker". Total count is 2. |
| 3 | Verify "Carol Chen" and "Dave Daniels" are NOT in the response | Iterate through all returned records; no globex employee IDs or names present. |
| 4 | Search for "Carol" in the directory | Response returns 0 results, even though Carol exists in globex. |
| 5 | Search for "Dave" in the directory | Response returns 0 results. |
| 6 | Export CSV from acme | CSV contains exactly 2 rows: Alice Adams and Bob Baker. No globex data. |
| 7 | Switch to "globex" tenant context and repeat | `GET /api/v1/tenant/employees/directory` returns only "Carol Chen" and "Dave Daniels". Zero acme employees. |

## 6. Postconditions
- No cross-tenant data exposure occurred.
- EF Core global query filters correctly scoped all directory queries.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
