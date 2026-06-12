---
id: TC-CHR-ISO-021
user_story: US-CHR-006
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-021: Tenant A org tree shows zero Tenant B departments and employees

## 1. Test Objective
Verify that the Organization Tree is fully tenant-isolated: a user authenticated in Tenant A sees only Tenant A's departments and employees in both department hierarchy and reporting structure views. Zero departments or employees from Tenant B appear in any view, search, or export. This tests EF Core global query filters and the `tenant_id` scoping per FR-8, NFR-3.

## 2. Related Requirements
- User Story: US-CHR-006
- Functional Requirements: FR-8
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" exists with departments: "Engineering" (root), "Backend" (child). Employees: "Alice Adams", "Bob Baker".
- Tenant "globex" exists with departments: "Marketing" (root), "Digital" (child). Employees: "Carol Chen", "Dave Daniels".
- HR Officer is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Has Engineering, Backend; Alice Adams, Bob Baker |
| Tenant B | globex | Has Marketing, Digital; Carol Chen, Dave Daniels |
| Auth Context | acme | HR Officer in Tenant A |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer in "acme" tenant | JWT contains tenant_id for acme. |
| 2 | Send `GET /api/v1/org-tree?view=department&depth=10` | Response returns only "Engineering" and "Backend" departments. No "Marketing" or "Digital". |
| 3 | Verify no globex employees are present in the response | Iterate through all returned nodes; "Carol Chen" and "Dave Daniels" are NOT present. Employee counts reflect only acme employees. |
| 4 | Send `GET /api/v1/org-tree?view=reporting&depth=10` | Response returns only acme manager/report relationships. No globex people. |
| 5 | Search for "Carol" in the org tree | Search returns 0 results, even though Carol exists in globex. |
| 6 | Search for "Marketing" in the org tree | Search returns 0 results, even though Marketing exists in globex. |
| 7 | Export PNG from acme org tree | The exported image contains only acme departments and employees. No globex data. |
| 8 | Switch to "globex" tenant context and load org tree | `GET /api/v1/org-tree?view=department&depth=10` returns only "Marketing" and "Digital". Zero acme departments or employees. |

## 6. Postconditions
- No cross-tenant data exposure occurred.
- EF Core global query filters correctly scoped all org-tree queries.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
