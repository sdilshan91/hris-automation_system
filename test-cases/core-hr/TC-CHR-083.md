---
id: TC-CHR-083
user_story: US-CHR-001
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-083: Tenant_id set from session context, not from user input (FR-4)

## 1. Test Objective
Verify that the `tenant_id` on a new employee record is always set from the authenticated session context (via TenantInterceptor), and that any attempt to supply or override `tenant_id` in the request body is ignored or rejected.

## 2. Related Requirements
- User Story: US-CHR-001
- Functional Requirements: FR-4
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Two tenants exist: "acme" (Tenant A, ID: tenant-a-uuid) and "globex" (Tenant B, ID: tenant-b-uuid).
- A user with HR Officer role is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Authenticated as Tenant A |
| Spoofed tenant_id | tenant-b-uuid | Attempt to inject Tenant B's ID |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send a `POST /api/v1/tenant/employees` request with all valid fields PLUS `tenant_id: tenant-b-uuid` in the body | The API either ignores the tenant_id field or returns an error. |
| 2 | Verify the response is 201 Created (if tenant_id was ignored) | Employee is created. |
| 3 | Query the database for the new employee's tenant_id | `tenant_id` equals tenant-a-uuid (from the session), NOT tenant-b-uuid (from the spoofed input). |
| 4 | Verify the TenantInterceptor auto-stamped the correct tenant_id | The interceptor enforced tenant_id from ITenantContext, not from user input. |
| 5 | Verify the employee is visible in Tenant A's employee list and NOT in Tenant B's | Data isolation confirmed. |

## 6. Postconditions
- The employee belongs to Tenant A regardless of the spoofed tenant_id in the request.
- Tenant B has no new employee records.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
