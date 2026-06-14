---
id: TC-ATT-098
user_story: US-ATT-007
module: Attendance
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-ATT-098: Summary endpoints require authn + Attendance.Read.All (HR), enforce self-scope denial for non-HR, sanitise inputs; cache served on subsequent loads / DB-fallback verified (Authn/Authz, FR-8/NFR-1)

## 1. Test Objective
Verify the security surface of the summary endpoints and the FR-8/NFR-1 cache behavior: every summary endpoint (`/summary/monthly`, `/summary/monthly/{employeeId}`, `/summary/monthly/generate`, `/summary/monthly/export`) requires authentication and the `Attendance.Read.All` permission (HR); an Employee/Manager without it is denied (403, server-side, not just hidden UI); filter/month parameters are validated and sanitised (no SQL injection / XSS); and the daily summary cache (Redis key `att_summary:{tenant_id}:{year_month}:{employee_id}`) is served on subsequent loads -- CONDITIONAL on Redis, with the DB/materialized-table fallback verified now.

## 2. Related Requirements
- User Story: US-ATT-007
- Preconditions: §2 (`Attendance.Read.All` permission)
- Functional Requirements: FR-8 (Redis cache key `att_summary:{tenant_id}:{year_month}:{employee_id}`)
- Non-Functional Requirements: NFR-1 (cache-served page load)

## 3. Preconditions
- Tenant "acme". HR Officer "Priya" (has `Attendance.Read.All`); Manager "Ben" and Employee "Sam" (do NOT have it). Month 2026-05 summary generated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| HR | Priya (Attendance.Read.All) | allowed |
| Manager/Employee | Ben / Sam (no Read.All) | denied |
| month param | 2026-05; also `2026-05'; DROP...` | injection attempt |
| cache key | att_summary:{tenant_id}:{year_month}:{employee_id} | FR-8 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call each summary endpoint with no/invalid token | 401 Unauthorized for all four endpoints. |
| 2 | As Ben/Sam (no `Attendance.Read.All`), call the summary, drill-down, generate, and export endpoints | 403 Forbidden, enforced server-side (not merely a hidden button); no summary data returned. |
| 3 | As Priya, call the same endpoints | 200 / accepted -- HR with the permission is allowed. |
| 4 | Inject SQL/XSS into the `month`, `departmentId`, and `format` parameters | Inputs are validated/parameterised; no injection executes; an invalid month/format returns a clean 400, not a 500 or data leak. |
| 5 | Load the summary twice (FR-8/NFR-1 cache) | If Redis is wired: the second load is served from the tenant-scoped cache key `att_summary:{tenant_id}:{year_month}:{employee_id}`. CONDITIONAL/DEFERRED on Redis -- verify the DB/materialized-table read path serves correct data now and that the cache-key design is tenant- and employee-scoped (see TC-ATT-ISO-010 / TC-ATT-ISO-004). |
| 6 | Verify the employee-level drill-down respects permission | Employee self-service (if exposed) is scoped to own data only; HR `Attendance.Read.All` may view any employee in-tenant; cross-employee access without the permission is denied. |

## 6. Postconditions
- All summary endpoints enforce authn + HR permission server-side, reject injection, and serve correct tenant-scoped data; cache-served behavior is asserted when Redis exists, with the DB-fallback verified now.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **Redis summary cache (FR-8/NFR-1) CONDITIONAL/DEFERRED:** the platform does not assume Redis is wired (consistent module-wide per docs/vault/modules/attendance.md and prior ATT stories). Step 5 verifies the DB/materialized-table fallback now and the tenant+employee-scoped key design (`att_summary:{tenant_id}:{year_month}:{employee_id}`); when Redis lands, assert cache-hit on subsequent loads, TTL, and invalidation-on-regeneration. **Reported to caller.**
- The exact `Attendance.Read.All` permission string should be confirmed against the PermissionCatalog (prior ATT stories used concrete strings, not wildcards).
