---
id: TC-ADM-ISO-005
user_story: US-ADM-002
module: Admin Console
priority: critical
type: security
status: blocked
exec_note: "2026-07-03 BLOCKED persona-gap: endpoint is System-Admin/system-context only (api/v1/system/*); tenantadmin persona => 403 both arms. The TC's core assertion requires a SystemAdmin read/write baseline (aggregate scoping / deletion job / cross-tenant override resolution) not performable with a tenant persona and barred as a cross-tenant write. No leak observed."
created: 2026-06-16
---

# TC-ADM-ISO-005: Cross-tenant monitoring aggregates are correctly scoped — no per-tenant row leakage

## 1. Test Objective
Verify AC-5 / BR-2 isolation for the system-context monitoring path. The monitoring overview deliberately reads across tenants (system context, `IgnoreQueryFilters` / system tenant), so it must (a) attribute each per-tenant count to the correct `tenant_id` with zero cross-attribution, and (b) never surface another tenant's row-level data (employee records, names, salaries) — only aggregates. A name-collision probe (two tenants with identically named employees) confirms counts are partitioned by `tenant_id`, not by name.

## 2. Related Requirements
- User Story: US-ADM-002
- Acceptance Criteria: AC-5
- Business Rules: BR-1, BR-2
- Cross-cutting: mandatory multi-tenant isolation; EF query filters + system-context aggregation

## 3. Preconditions
- Two tenants: Alpha (3 active employees) and Beta (7 active employees), each with employees sharing a name (e.g. both have an "John Smith") — a collision probe.
- System Admin authenticated at `admin.yourhrm.com` (system context).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Alpha | 3 active employees | own count |
| Beta | 7 active employees | own count |
| Collision | both have employee "John Smith" | partition-by-id probe |
| Expected | Alpha gauge=3, Beta gauge=7 | no cross-attribution |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the monitoring overview; read Alpha's and Beta's employee gauges | Alpha = 3, Beta = 7 — each count attributed to its own `tenant_id`; no merging despite the shared employee name. |
| 2 | Confirm aggregation uses system context deliberately | The cross-tenant roll-up is the only place query filters are bypassed (system tenant / explicit `IgnoreQueryFilters`); it returns COUNTS grouped by tenant, not raw rows. |
| 3 | Inspect the overview/usage payload for row-level data | Only per-tenant aggregate counts are present — no employee rows, names, or salaries from Alpha or Beta. |
| 4 | Open Alpha's tenant detail, then Beta's | Each detail shows only that tenant's operational fields; no Beta data appears in Alpha's view and vice-versa. |
| 5 | Sum cross-check | Platform total active employees = 10 (3 + 7); the per-tenant split is exact with zero overlap. |

## 6. Postconditions
- Monitoring aggregates are correctly partitioned by tenant; no cross-tenant row leakage; collision-by-name does not corrupt counts.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
