---
id: TC-CHR-113
user_story: US-CHR-002
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-113: Multi-tenant isolation -- fetch employee from another tenant returns 404

## 1. Test Objective
Verify that when an HR Officer in Tenant A attempts to fetch an employee profile belonging to Tenant B, the API returns 404 Not Found (not 403 Forbidden) to avoid leaking the existence of the record. This validates FR-7, NFR-3, and is a mandatory multi-tenant isolation test.

## 2. Related Requirements
- User Story: US-CHR-002
- Functional Requirements: FR-7
- Non-Functional Requirements: NFR-3
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" exists with employees.
- Tenant "globex" exists with employee "Eve Rogers" (ID: {eve_rogers_id}).
- HR Officer is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Authenticated tenant |
| Tenant B | globex | Target tenant |
| Globex Employee ID | {eve_rogers_id} | Belongs to globex |
| Auth Context | acme | HR Officer in Tenant A |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer in "acme" tenant | JWT contains tenant_id for acme. |
| 2 | Send `GET /api/v1/tenant/employees/{eve_rogers_id}` (globex employee UUID) | Response status is 404 Not Found. Response body does NOT contain any globex-specific data. Error message is generic (e.g., "Employee not found"). |
| 3 | Verify the response does NOT return 403 | Status must be 404, not 403 -- returning 403 would leak that the employee exists in another tenant. |
| 4 | Send `PATCH /api/v1/tenant/employees/{eve_rogers_id}` with body `{ "phone": "hacked", "xmin": "0" }` | Response status is 404 Not Found. |
| 5 | Verify the globex employee record is unchanged | Eve Rogers' data in globex is untouched. |
| 6 | Verify no audit log entry in acme tenant for this access attempt | No audit record for eve_rogers_id in acme's audit log (optionally, a security audit entry may be logged, but no data exposure). |

## 6. Postconditions
- No cross-tenant data exposure occurred.
- Globex employee data is unchanged.
- The 404 response prevented existence leakage.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
