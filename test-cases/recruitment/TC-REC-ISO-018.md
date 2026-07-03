---
id: TC-REC-ISO-018
user_story: US-REC-009
module: Recruitment
priority: critical
type: security
status: pass
exec_note: "2026-07-03 API-layer isolation probe (acme tenantadmin JWT): cross-tenant arm (X-Tenant-Subdomain: techoneglobal) => 403 cross_tenant_denied; same-tenant arm (acme) => 200. TenantAccessGuardMiddleware enforced. No leak. Recruitment dashboard aggregates tenant-scoped (same-arm KPIs reflect acme only)."
created: 2026-06-15
---

# TC-REC-ISO-018: Recruitment analytics are tenant-isolated — Tenant B's dashboard aggregates zero of Tenant A's data across every metric, cache key, and (if used) materialized view (AC-5, NFR-2/NFR-3)

## 1. Test Objective
Verify AC-5 / NFR-2 / NFR-3 for REC-009's new aggregation surface: every dashboard metric is computed ONLY over the resolved tenant's data. Because the dashboard AGGREGATES across `applicant`, `applicant_stage_history`, `vacancy`, `offer`, and `interview`, this case asserts that NONE of those aggregations cross tenants: Tenant B (globex) sees KPIs, funnel, source, time-to-hire trend, vacancy status, and recent activity computed from globex data only, with zero contribution from Tenant A (acme). It also asserts the two analytics-specific isolation seams not covered by the per-table ISO TCs: (a) the analytics cache key / materialized view is tenant-scoped (a globex dashboard never reads an acme-keyed cache row or an unscoped MV row), and (b) drill-down/export by department or vacancy id cannot reference another tenant's department/vacancy. The generic no/invalid/mismatched tenant-context rejection and the cross-tenant write/body-injection contract are reused from TC-REC-ISO-010/011 on the recruitment surface (the dashboard is read-only, so write isolation reuse covers any export-job parameters).

NOTE: AC-5/NFR-2 specify PostgreSQL RLS on the analytics tables; this platform enforces isolation via EF Core global query filters + TenantInterceptor (the same aggregation queries inherit the global filters). If RLS is later added on `applicant`/`applicant_stage_history`/`vacancy`/`offer`/`interview` (or an `mv_recruitment_analytics` view), extend Step 6 to assert it at the DB session level. Per-table read isolation is already covered by TC-REC-ISO-005 (applicant), TC-REC-ISO-009 (pipeline/stage history), TC-REC-ISO-014 (interview), TC-REC-ISO-016 (offer), and TC-REC-ISO-001 (vacancy); this case asserts that the CROSS-TABLE AGGREGATION layered on top of them stays tenant-bound.

## 2. Related Requirements
- User Story: US-REC-009
- Acceptance Criteria: AC-5 (only Tenant A's data aggregated; no cross-tenant leakage)
- Non-Functional Requirements: NFR-2 (analytics queries tenant-scoped; RLS), NFR-3 (pre-aggregation/Redis cache with tenant-scoped keys)
- Reuses: TC-REC-ISO-010 (no/invalid/mismatched tenant context), TC-REC-ISO-011 (cross-tenant write block + body-injected tenant_id) on the recruitment surface; builds on TC-REC-ISO-001/005/009/014/016 per-table read isolation.

## 3. Preconditions
- Tenant "acme" (A): rich recruitment dataset — vacancies, ~100 applicants across stages, hires, offers (sent/accepted), interviews.
- Tenant "globex" (B): a SMALLER, KNOWN dataset — e.g. 2 Open vacancies, 12 applicants, 1 hire, 3 offers (2 accepted), so any acme contamination would be immediately visible in globex's totals.
- Subdomains globex.yourhrm.com and acme.yourhrm.com active.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | large dataset (must NOT appear in B) |
| Tenant B | globex | open vacancies 2, applicants 12, hires 1, offers 3/2 |
| B expected applicants | 12 | exact, no acme spillover |
| B expected acceptance rate | 66.7% | 2/3, globex-only |
| Injected param | acme departmentId/vacancyId in a globex request | must 404/ignore |
| Cache key | tenant:{globexId}:recruitment:dashboard:* | never reads acme-keyed/unscoped row |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As a globex user, load the dashboard on globex.yourhrm.com | KPIs reflect globex ONLY: Open Vacancies 2, Total Applicants 12, Hires 1, Offer Acceptance Rate 66.7%; acme's larger numbers do NOT appear (AC-5). |
| 2 | Inspect globex's funnel, source effectiveness, time-to-hire trend, vacancy status, and recent activity | Every widget is computed from globex data only; no acme applicants/sources/events leak into any aggregation. |
| 3 | As a globex user, attempt a drill-down filter referencing an acme departmentId / vacancyId (direct API param) | 404/empty (the EF global query filter cannot resolve acme's department/vacancy under globex context); no acme data returned. |
| 4 | Trigger an export as globex with an injected `tenant_id=acme` (or acme vacancy id) in the request/job body | The injected tenant/id is ignored; the export contains globex data only and any export job is stamped globex (reuses TC-REC-ISO-011, NFR-3). |
| 5 | Call the dashboard/analytics endpoints with no/invalid/mismatched tenant context (reserved/admin subdomain, missing subdomain) | Rejected; no tenant resolved -> no aggregation served (reuses TC-REC-ISO-010). |
| 6 | Inspect the analytics cache / materialized view under globex context | The cache key is tenant-scoped to globex and never returns an acme-keyed row; if an MV is used, the MV read is filtered by tenant_id. At the DB level, the aggregation queries under a globex context read only globex rows from `applicant`/`stage_history`/`vacancy`/`offer`/`interview`. (If RLS is later added, confirm a globex session cannot read acme rows via direct SQL.) |

## 6. Postconditions
- No cross-tenant analytics were read; globex's dashboard totals equal its known dataset exactly; caches/MV/drill-down/exports stay tenant-scoped.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
