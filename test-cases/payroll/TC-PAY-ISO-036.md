---
id: TC-PAY-ISO-036
user_story: US-PAY-009
module: Payroll
priority: high
type: security
status: pass
created: 2026-06-16
---

# TC-PAY-ISO-036: Pre-aggregated dashboard cache + report/export temp-file store are tenant-scoped (no cross-tenant aggregate, chart, or file-byte leak)

## 1. Test Objective
Verify AC-5 / FR-8 / NFR-3 / NFR-4 / NFR-6: any caching of the pre-aggregated dashboard data (used to meet the <=3s chart SLA) and any temporary store for generated export/report files are keyed per tenant, so Tenant A's cached aggregates/charts and export bytes never serve to Tenant B. A finalization that refreshes/invalidates Tenant A's aggregate cache must not touch Tenant B's; export files in the temp store are isolated and individually subject to the 24h auto-delete. (CONDITIONAL: if dashboard aggregates are computed on demand without a cache layer today, this asserts no shared/global cache key is used and queries are always tenant-filtered; the file-store path-keying assertion still holds.)

## 2. Related Requirements
- User Story: US-PAY-009
- Acceptance Criteria: AC-5
- Functional Requirements: FR-5, FR-8
- Non-Functional Requirements: NFR-3, NFR-4, NFR-6

## 3. Preconditions
- Tenant A "acme" + Tenant B "globex", each with dashboard data + generated export files.
- Cache layer (if any) + export temp store available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Dashboard cache key | `tenant:{tenantId}:payroll:dashboard:{fy}:{month}` | tenant-scoped |
| Export temp path | `{tenantId}/payroll/reports/tmp/{exportId}` | tenant-scoped |
| Retention | export auto-delete @ 24h | NFR-4 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load acme's dashboard; inspect the cache key used for the pre-aggregated data. | The key includes the tenant_id (no shared/global key); if no cache exists, aggregates are computed with always-tenant-filtered queries (FR-8, NFR-6). |
| 2 | Load globex's dashboard. | globex charts reflect ONLY globex aggregates; acme's cached values are never served to globex (FR-8). |
| 3 | Finalize an acme run (refreshing acme's aggregate cache). | Only acme's dashboard cache entry is invalidated/refreshed; globex's cached aggregates are untouched (FR-5, FR-8). |
| 4 | Generate an export in acme; inspect its temp-store location. | The file lives under acme's tenant-scoped temp path; globex cannot enumerate or read it (FR-8, NFR-3). |
| 5 | Let an acme export age past 24h. | Only acme's expired file is deleted by the retention sweep; globex's files are untouched; deletion is tenant-isolated (NFR-4). |
| 6 | Confirm each tenant's dashboard cache + export store work normally. | Per-tenant aggregate caching, chart rendering, and export retention function; isolation blocks only cross-tenant leakage. |

## 6. Postconditions
- Dashboard aggregate caches + export temp-file stores are tenant-scoped; refresh/invalidation + 24h auto-delete act per tenant; no cross-tenant aggregate, chart, or file-byte leak.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
