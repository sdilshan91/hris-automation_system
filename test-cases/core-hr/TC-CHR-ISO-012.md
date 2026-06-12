---
id: TC-CHR-ISO-012
user_story: US-CHR-001
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-012: Cache keys for employees are tenant-scoped

## 1. Test Objective
Verify that any caching of employee data (Redis or in-memory) uses tenant-scoped cache keys, preventing Tenant A from receiving cached employee data belonging to Tenant B.

## 2. Related Requirements
- User Story: US-CHR-001
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Two tenants exist: "acme" (Tenant A) and "globex" (Tenant B), both with employees.
- Redis (or equivalent cache) is configured and connected.
- Employee list or employee detail queries use caching.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme.yourhrm.com | Has employees |
| Tenant B | globex.yourhrm.com | Has employees |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Tenant A, request `GET /api/v1/tenant/employees` (populates cache) | Response returns Tenant A's employees. Cache is populated. |
| 2 | Inspect the Redis cache keys related to employees | Cache keys include the tenant ID as a prefix or segment (e.g., `tenant:{tenantA_id}:employees:list`). |
| 3 | As Tenant B, request `GET /api/v1/tenant/employees` | Response returns Tenant B's employees (not Tenant A's cached data). |
| 4 | Inspect the Redis cache keys | Tenant B has its own separate cache key (e.g., `tenant:{tenantB_id}:employees:list`). |
| 5 | Verify no cache key exists without a tenant prefix for employee data | All employee-related cache keys are tenant-scoped. |
| 6 | Clear Tenant A's cache and verify Tenant B's cache is unaffected | Tenant B's cached data remains intact. |

## 6. Postconditions
- Cache keys are tenant-isolated.
- No cross-tenant cache pollution is possible.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
