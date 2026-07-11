---
id: TC-RPT-ISO-010
user_story: US-RPT-003
module: Reports & Analytics
priority: critical
type: security
status: fail
exec_note: "2026-06-30 API iso-fixture probe: no-tenant-context correctly rejected (no X-Tenant-Subdomain -> HTTP 400 'No tenant context resolved'); BUT cross-tenant access via foreign X-Tenant-Subdomain header LEAKS (BUG-003). Mixed: rejection arm holds, header-spoof arm breaches -> fail on the breach."
created: 2026-06-17
---

# TC-RPT-ISO-010: No-tenant-context rejected; cross-tenant run/department ID injection -> 404 (not 403); spoofed tenant_id ignored (AC-5)

## 1. Test Objective
Verify the payroll-report API enforces tenant context server-side: a request with no resolvable tenant
is rejected, a `payroll_run_id` / `department_id` belonging to another tenant injected into a filter
yields 404 (existence not disclosed — NOT 403), and any client-supplied tenant identifier is ignored
in favor of the resolved `ITenantContext`. Validates AC-5, FR-8, NFR-2.

> PLATFORM ACCURACY: isolation is enforced by `TenantResolutionMiddleware` -> scoped `ITenantContext`
> + EF global query filter (read) + `TenantInterceptor` (write). Cross-tenant IDs fall outside the
> query filter and therefore resolve to NOT FOUND — assert 404, not 403 (consistent with
> TC-RPT-ISO-002/006 and prior modules). Postgres RLS is deferred defense-in-depth.

## 2. Related Requirements
- User Story: US-RPT-003
- Acceptance Criteria: AC-5
- Functional Requirements: FR-2 (filters), FR-8 (tenant_id from session)
- Non-Functional: NFR-2

## 3. Preconditions
- Tenant A and Tenant B active with known payroll data.
- `hrA` authenticated in Tenant A; a known Tenant B `payroll_run_id` and `department_id`.
- Ability to issue a request with no resolvable tenant (unknown/missing subdomain / `X-Tenant-Subdomain`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant B payroll_run_id | uuid (Tenant B) | injected into Tenant A request |
| Tenant B department_id | uuid (Tenant B) | injected as filter |
| spoofed tenant header/claim | Tenant B subdomain/id | must be IGNORED |
| no-tenant request | missing/unknown subdomain | rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call GET /api/v1/payroll/reports/PayrollSummary with no resolvable tenant context | Rejected (no tenant resolved) — no payroll data returned |
| 2 | As `hrA`, request Run Summary with `payroll_run_id` = a Tenant B run | 404 Not Found (NOT 403) — Tenant B run invisible under A's query filter |
| 3 | As `hrA`, pass `department_ids` = a Tenant B department | Treated as not-found / empty result; zero Tenant B rows leak |
| 4 | As `hrA`, send a spoofed tenant identifier (header/claim) for Tenant B while authenticated in A | Spoof IGNORED; report scoped to Tenant A via resolved ITenantContext (FR-8) |
| 5 | As `hrA`, request Bank Advice with a Tenant B run id | 404; no Tenant B accounts disclosed |
| 6 | Confirm error bodies disclose no Tenant B existence/details | Generic not-found; no leakage of B's IDs/names |

## 6. Postconditions
- No-tenant rejected; cross-tenant IDs 404; spoofed tenant ignored; scope from server context.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
