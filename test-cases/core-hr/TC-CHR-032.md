---
id: TC-CHR-032
user_story: US-CHR-004
module: Core HR
priority: high
type: security
status: draft
created: 2026-06-11
---

# TC-CHR-032: TenantInterceptor auto-stamps tenant_id on new department records

## 1. Test Objective
Verify that the TenantInterceptor (EF Core SaveChanges interceptor) automatically stamps the correct `tenant_id` on newly created department records from the session context, and that a client cannot override the tenant_id via the API request body.

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-2 (tenant_id set from session context)
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with status `active` (tenant_id = UUID_ACME).
- A user with Tenant Admin role is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Auth Context Tenant | acme (UUID_ACME) | Session tenant |
| Spoofed Tenant ID | UUID_GLOBEX | Attempted override |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/departments` with body `{ name: "New Dept" }` (no tenant_id in body) | Department created with `tenant_id = UUID_ACME` (auto-stamped from session). |
| 2 | Verify the created department's `tenant_id` in the database | Matches UUID_ACME exactly. |
| 3 | Send `POST /api/v1/departments` with body `{ name: "Spoofed Dept", tenant_id: "UUID_GLOBEX" }` | Either: (a) `tenant_id` field in the body is ignored and the department is created with UUID_ACME, or (b) API returns 400 for unknown/disallowed field. |
| 4 | Verify the created department's `tenant_id` in the database | Matches UUID_ACME, NOT UUID_GLOBEX. Client cannot override tenant_id. |

## 6. Postconditions
- All departments have correct tenant_id from the session context.
- Client-provided tenant_id in the request body is never honored.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
