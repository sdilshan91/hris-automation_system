---
id: TC-RPT-ISO-006
user_story: US-RPT-002
module: Reports & Analytics
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-RPT-ISO-006: No-tenant-context rejected; cross-tenant leave/attendance ID injection → 404 not 403; spoofed tenant_id ignored (AC-5)

## 1. Test Objective
Verify the leave/attendance reporting API rejects requests without a resolvable tenant context, and
that injecting another tenant's department/employee/leave-type/shift IDs (or a foreign tenant_id)
into report parameters cannot pull cross-tenant data. Cross-tenant single-resource access returns 404
(existence not disclosed), not 403.

> PLATFORM NOTE: AC-5 / NFR-2 name PostgreSQL RLS. This platform enforces tenant context via
> `TenantResolutionMiddleware` -> scoped `ITenantContext`, EF Core global query filters (read), and
> `TenantInterceptor` (write stamping). RLS is deferred defense-in-depth. These steps assert the
> mechanism in force today; cross-tenant ID injection asserts 404, not 403.

## 2. Related Requirements
- User Story: US-RPT-002
- Acceptance Criteria: AC-5
- Functional Requirements: FR-7 (tenant_id in query context), FR-2 (filter validation)
- Non-Functional: NFR-2 (tenant isolation)

## 3. Preconditions
- Tenant A and Tenant B active. `hrA` authenticated in Tenant A.
- Tenant B owns deptB1, empB1, leaveTypeB1, shiftB1 (all foreign to A).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| foreign department_id | deptB1 | Tenant B |
| foreign employee_id | empB1 | Tenant B |
| foreign leave_type_id | leaveTypeB1 | Tenant B |
| foreign shift_id | shiftB1 | Tenant B |
| spoofed tenant_id param/header | Tenant B id | injection attempt |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call the report endpoint with NO tenant context (no subdomain / no X-Tenant-Subdomain) | Request rejected — no tenant resolved; no report returned |
| 2 | As `hrA`, request Leave Utilization with department_ids=[deptB1] | EF filter scopes to A; deptB1 yields zero in-tenant matches/empty; Tenant B data never returned |
| 3 | As `hrA`, request Leave Balance with employee_id=empB1 | Not-found/empty within A; a direct drill-down fetch of empB1 -> 404 (not 403) |
| 4 | As `hrA`, request with leave_type_ids=[leaveTypeB1] and Overtime with shift_ids=[shiftB1] | No Tenant B leave-type/shift data; empty/not-found within A |
| 5 | As `hrA`, inject a spoofed tenant_id param/header = Tenant B's id | Ignored; authoritative tenant remains A (from resolution/JWT); no Tenant B data returned |
| 6 | Confirm none of steps 2–5 leak Tenant B counts, names, or existence | Zero cross-tenant disclosure in any response body |

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
