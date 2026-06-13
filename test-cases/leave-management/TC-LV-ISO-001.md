---
id: TC-LV-ISO-001
user_story: US-LV-001
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-ISO-001: Tenant A cannot see Tenant B's leave types

## 1. Test Objective
Verify that leave type data is fully tenant-isolated: a user authenticated in Tenant A cannot see, list, or retrieve any leave types belonging to Tenant B. This tests EF Core global query filters and ensures no cross-tenant data leakage via the API.

## 2. Related Requirements
- User Story: US-LV-001
- Non-Functional Requirements: NFR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with leave types: "Annual Leave", "Sick Leave".
- Tenant "globex" exists with leave types: "Globex PTO", "Globex Medical Leave".
- A user with `Leave.Configure` permission is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Has Annual Leave, Sick Leave |
| Tenant B | globex | Has Globex PTO, Globex Medical Leave |
| Auth Context | acme | User authenticated in Tenant A |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer in "acme" tenant | JWT contains `tenant_id` for acme. |
| 2 | Send `GET /api/v1/leave-types` | Response returns only "Annual Leave" and "Sick Leave". Zero results from globex. |
| 3 | Verify "Globex PTO" and "Globex Medical Leave" are NOT in the response | No cross-tenant leave types visible. |
| 4 | Attempt `GET /api/v1/leave-types/{globex_pto_id}` using the UUID of Globex PTO | Response is 404 Not Found (EF global query filter excludes it). |
| 5 | Switch to "globex" tenant context and verify | `GET /api/v1/leave-types` returns "Globex PTO" and "Globex Medical Leave" only. No acme leave types. |
| 6 | Verify at database level | Direct SQL `SELECT * FROM leave_type WHERE tenant_id = acme_id` returns only acme's types; `SELECT * FROM leave_type WHERE tenant_id = globex_id` returns only globex's types. |

## 6. Postconditions
- No cross-tenant data exposure occurred.
- EF Core global query filters correctly scope all leave type queries.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
