---
id: TC-RPT-ISO-018
user_story: US-RPT-005
module: Reports & Analytics
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-RPT-ISO-018: No-tenant-context request rejected; cross-tenant ID injection -> 404 (not 403); spoofed tenant_id ignored (AC-5, FR-8, NFR-3)

## 1. Test Objective
Verify the dashboard endpoint refuses to serve data without a resolved tenant context, that attempting to
reach another tenant's scoped resource (e.g. a manager's `?employeeId=` or a widget drill-through id belonging
to Tenant B) returns 404 (existence not disclosed, NOT 403), and that any client-supplied/spoofed `tenant_id`
(header/body/query) is IGNORED in favor of the session-resolved tenant. Validates AC-5, FR-8, NFR-3.

## 2. Related Requirements
- User Story: US-RPT-005
- Acceptance Criteria: AC-5
- Functional Requirements: FR-8 (tenant_id from session, not client)
- Non-Functional: NFR-3 (tenant isolation; RLS deferred -> EF filters); cross-tenant -> 404 (consistent with module)

## 3. Preconditions
- Tenant A (`hrA`) and Tenant B active with distinct data.
- A Tenant B employee id `EMP-B-1` and a Tenant B widget drill-through resource known to the tester.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| no tenant subdomain/context | -- | request rejected |
| cross-tenant id | EMP-B-1 | -> 404 not 403 (existence hidden) |
| spoofed tenant_id | Tenant B id in header/body | must be ignored |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/dashboard/widgets` with no resolvable tenant context (no subdomain / no `X-Tenant-Subdomain`) | request rejected (no tenant data served); TenantResolutionMiddleware blocks |
| 2 | As `hrA`, request a dashboard scoped to a Tenant B employee id (e.g. `?employeeId=EMP-B-1` on a team/drill endpoint) | 404 Not Found (existence not disclosed), NOT 403 (NFR-3, module convention) |
| 3 | As `hrA`, add a spoofed `tenant_id`/`X-Tenant-Id` for Tenant B in header/body | spoofed value IGNORED; response reflects Tenant A's session-resolved tenant only (FR-8) |
| 4 | As `hrA`, attempt a widget click-through deep link carrying a Tenant B resource id | resolves to 404 within Tenant A scope; no Tenant B data returned |
| 5 | Confirm logs/response carry no Tenant B identifiers | no leakage of the other tenant's existence/values |

## 6. Postconditions
- No-tenant-context blocked; cross-tenant access 404; spoofed tenant_id ignored.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
