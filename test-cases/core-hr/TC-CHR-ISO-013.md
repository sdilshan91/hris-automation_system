---
id: TC-CHR-ISO-013
user_story: US-CHR-002
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-013: Tenant A cannot view or edit Tenant B's employee profiles

## 1. Test Objective
Verify that employee profile data is fully tenant-isolated: a user authenticated in Tenant A cannot view, list, or edit any employee profiles belonging to Tenant B. The API returns 404 (not 403) to avoid leaking record existence. This tests EF Core global query filters and RLS enforcement per NFR-3 and FR-7.

## 2. Related Requirements
- User Story: US-CHR-002
- Functional Requirements: FR-7
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" exists with employees: "Jane Doe", "John Smith".
- Tenant "globex" exists with employees: "Eve Rogers", "Max Power".
- HR Officer is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Has Jane Doe, John Smith |
| Tenant B | globex | Has Eve Rogers, Max Power |
| Auth Context | acme | HR Officer in Tenant A |
| Globex Employee ID | {eve_rogers_id} | UUID of Eve Rogers |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer in "acme" tenant | JWT contains tenant_id for acme. |
| 2 | Send `GET /api/v1/tenant/employees` | Response returns only "Jane Doe" and "John Smith". Zero results from globex. |
| 3 | Verify "Eve Rogers" and "Max Power" are NOT in the response | No cross-tenant employee data visible. |
| 4 | Send `GET /api/v1/tenant/employees/{eve_rogers_id}` | Response is 404 Not Found. NOT 403. |
| 5 | Send `PATCH /api/v1/tenant/employees/{eve_rogers_id}` with body `{ "phone": "hacked", "xmin": "0" }` | Response is 404 Not Found. |
| 6 | Switch to "globex" tenant context and verify | `GET /api/v1/tenant/employees` returns "Eve Rogers" and "Max Power" only. No acme employees. |
| 7 | Verify Eve Rogers' data is unchanged | Phone and all other fields are the same as before the PATCH attempt. |

## 6. Postconditions
- No cross-tenant data exposure occurred.
- No cross-tenant data modification occurred.
- EF Core global query filters correctly scope all profile queries.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
