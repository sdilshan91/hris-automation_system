---
id: TC-PAY-ISO-048
user_story: US-PAY-012
module: Payroll
priority: high
type: security
status: draft
created: 2026-06-16
---

# TC-PAY-ISO-048: Tenant-scoped history/audit infrastructure -- history-list + audit-query caches and audit-export temp files are tenant-keyed; per-tenant invalidation; no cross-tenant cache hit or export-file leak

## 1. Test Objective
Verify AC-5 and FR-5/FR-8/NFR-1: any caching of the payroll history list or audit-trail query results, and any audit-export temp-file store, are tenant-scoped (no shared/global key). Tenant A populating/invalidating its cache never serves or invalidates Tenant B's; an audit export generated for A is never downloadable by B. (CONDITIONAL: if no cache/temp-file layer exists today, assert no shared/global key + always-tenant-filtered queries + server-derived tenant-scoped export paths.)

## 2. Related Requirements
- User Story: US-PAY-012
- Acceptance Criteria: AC-5
- Functional Requirements: FR-5 (export), FR-8 (tenant-scoped)
- Non-Functional Requirements: NFR-1 (async/perf), NFR-7 (archival to cold storage)

## 3. Preconditions
- Tenants "acme" (A) and "globex" (B) Active, each with payroll history + audit entries; observability into cache keys + export-file storage.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Cache keys | tenant:{tenantId}:payroll:history:* / :audit:* | tenant-scoped |
| Export store | {tenantId}/payroll/audit-exports/ | server-derived |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As A, load history + an audit query (populating any cache). | Cache keys are tenant-scoped (e.g. `tenant:{A}:payroll:audit:{hash}`); no shared/global key. |
| 2 | As B, load the equivalent history + audit query. | B gets a cache MISS against A's entry and is served only B's data; A's cached result is never returned to B. |
| 3 | As A, perform a payroll write (new audit entry); re-query. | A's history/audit cache is invalidated/refreshed for A only; B's cache is untouched. |
| 4 | As A, generate an audit-trail export; note its storage path/handle. | The export file is stored under a server-derived A-scoped path (e.g. `{A}/payroll/audit-exports/...`); the handle is tenant-bound. |
| 5 | As B, attempt to download A's export via its path/handle. | Denied (403/404); B cannot retrieve A's export file; no byte leak. |
| 6 | (Archival, NFR-7) Confirm cold-storage move stays tenant-scoped. | Entries older than 90 days archived per tenant; querying cold storage still returns only the requesting tenant's rows. |
| 7 | (If no cache/export layer yet) Assert the fallback. | Queries are always tenant-filtered with no shared/global key; export paths are tenant-derived; CONDITIONAL note recorded -- the no-cross-tenant guarantee still holds. |

## 6. Postconditions
- History/audit caches + audit-export files are tenant-scoped with per-tenant invalidation; no cross-tenant cache hit or export-file leak.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
