---
id: TC-RPT-ISO-002
user_story: US-RPT-001
module: Reports & Analytics
priority: critical
type: security
status: fail
exec_note: "2026-06-30 API iso-fixture probe: no-tenant-context correctly rejected (no X-Tenant-Subdomain -> HTTP 400 'No tenant context resolved'); BUT cross-tenant access via foreign X-Tenant-Subdomain header LEAKS (BUG-003). Mixed: rejection arm holds, header-spoof arm breaches -> fail on the breach."
created: 2026-06-17
---

# TC-RPT-ISO-002: API rejects requests without valid tenant context; cross-tenant ID injection returns 404 not 403 (AC-5)

## 1. Test Objective
Verify the reporting API rejects requests that arrive without a resolvable tenant context, and that
injecting another tenant's department/location/employee IDs (or a foreign tenant_id) into report
parameters cannot pull cross-tenant data. Cross-tenant ID access returns 404 (existence not
disclosed) rather than 403.

> PLATFORM NOTE: AC-5 / NFR-2 name PostgreSQL RLS. This platform enforces tenant context via the
> `TenantResolutionMiddleware` -> scoped `ITenantContext`, EF Core global query filters (read), and
> the `TenantInterceptor` (write stamping). RLS is deferred defense-in-depth. These steps assert the
> mechanism in force today; cross-tenant ID injection asserts 404, not 403.

## 2. Related Requirements
- User Story: US-RPT-001
- Acceptance Criteria: AC-5
- Functional Requirements: FR-7 (tenant_id in query context), FR-2 (filter validation)
- Non-Functional: NFR-2 (tenant isolation)

## 3. Preconditions
- Tenant A and Tenant B active. `hrA` authenticated in Tenant A.
- Tenant B has department `deptB1`, location `locB1`, employee `empB1` (all foreign to A).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| foreign department_id | deptB1 | Tenant B |
| foreign location_id | locB1 | Tenant B |
| spoofed tenant_id param/header | Tenant B id | injection attempt |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call the report endpoint with NO tenant context (no subdomain / no X-Tenant-Subdomain, no resolvable tenant) | Request rejected — no tenant resolved; no report returned |
| 2 | As `hrA`, request Headcount Summary with department_ids = [deptB1] | EF query filter scopes to Tenant A; deptB1 yields zero in-tenant matches OR 404/validation — Tenant B data never returned; response treats deptB1 as not-found within A |
| 3 | As `hrA`, request with location_ids = [locB1] | Same: no Tenant B location data; not-found/empty within A |
| 4 | As `hrA`, attempt to fetch a single foreign resource by id (e.g. drill-down to empB1) | 404 Not Found (existence not disclosed), NOT 403 |
| 5 | As `hrA`, inject a spoofed tenant_id param/header = Tenant B's id | Ignored; the authoritative tenant remains A (from resolution/JWT); no Tenant B data returned |
| 6 | Confirm none of steps 2-5 leak Tenant B counts, names, or existence in the response body | Zero cross-tenant disclosure |

## 6. Postconditions
- Missing tenant context rejected; foreign IDs return 404/empty; spoofed tenant_id ignored.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
