---
id: TC-CHR-ISO-001
user_story: US-CHR-004
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-11
---

# TC-CHR-ISO-001: Tenant A cannot see Tenant B's departments

## 1. Test Objective
Verify that department data is fully tenant-isolated: a user authenticated in Tenant A cannot see, list, or retrieve any departments belonging to Tenant B. This tests EF Core global query filters and RLS enforcement per NFR-2.

## 2. Related Requirements
- User Story: US-CHR-004
- Non-Functional Requirements: NFR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with departments: "Engineering", "Marketing".
- Tenant "globex" exists with departments: "Globex R&D", "Globex Sales".
- A user with Tenant Admin role is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Has Engineering, Marketing |
| Tenant B | globex | Has Globex R&D, Globex Sales |
| Auth Context | acme | User authenticated in Tenant A |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Tenant Admin in "acme" tenant | JWT contains `tenant_id` for acme. |
| 2 | Send `GET /api/v1/departments` | Response returns only "Engineering" and "Marketing". Zero results from globex. |
| 3 | Verify "Globex R&D" and "Globex Sales" are NOT in the response | No cross-tenant departments are visible. |
| 4 | Attempt `GET /api/v1/departments/{globex_rd_id}` using the UUID of Globex R&D | Response is 404 Not Found (EF global query filter excludes it). |
| 5 | Switch to "globex" tenant context and verify | `GET /api/v1/departments` returns "Globex R&D" and "Globex Sales" only. No acme departments. |
| 6 | Verify at database level: departments exist for both tenants but query filters enforce isolation | Direct SQL `SELECT * FROM department WHERE tenant_id = acme_id` returns only acme's departments; vice versa for globex. |

## 6. Postconditions
- No cross-tenant data exposure occurred.
- EF Core global query filters correctly scope all queries.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
