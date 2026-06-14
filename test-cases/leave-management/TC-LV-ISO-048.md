---
id: TC-LV-ISO-048
user_story: US-LV-012
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-048: Report export/cache artifacts are tenant-scoped (blob path + cache keys; Blob/Redis DEFERRED — partial) (NFR-3)

## 1. Test Objective
Verify that any persisted/cached report artifact is tenant-scoped: background-export files use the tenant-scoped blob path `{tenantId}/reports/leave/{reportId}.xlsx` and any cached report/balance value embeds the tenant id, so Tenant A can never download or read Tenant B's generated report. Blob storage and Redis are DEFERRED dependencies, so the path/key design is verified by design and the DB-fallback isolation is verified live, with the live blob/cache portion recorded CONDITIONAL.

## 2. Related Requirements
- User Story: US-LV-012
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-5
- Data Requirements: §7 export path
- Note: Blob storage (FR-5) and Redis cache (BR-3) DEFERRED per docs/vault/modules/leave-management.md; documented cache key `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}`.

## 3. Preconditions
- Tenants "acme" and "globex", each able to generate a (large) export; a reportId issued per request.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Blob path | `{tenantId}/reports/leave/{reportId}.xlsx` | tenant-scoped, DEFERRED |
| Cache key | `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}` | documented |
| Collision probe | same reportId/leave-type across tenants | must not collide |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify the export blob-path design | The path is prefixed by `{tenantId}`, so acme and globex artifacts live under disjoint prefixes and cannot collide or be cross-read. |
| 2 | (CONDITIONAL on Blob Storage) Generate a background export in acme, then attempt to fetch it from a globex session | acme's file is retrievable only within acme's tenant scope; globex cannot enumerate or download it (download authorized by tenant + report ownership). Mark CONDITIONAL until blob storage is wired. |
| 3 | (CONDITIONAL on Redis) Verify cached balance/report keys | Keys embed `tenantId` (and `employeeId`), so acme and globex never share a cache entry; invalidation in one tenant does not touch the other. DB-fallback path (no cache) verified live. |
| 4 | Confirm DB-fallback isolation (live) | With no cache/blob, the report is recomputed per request under the tenant filter; acme's recompute never reads globex data (cross-ref TC-LV-ISO-047). |

## 6. Postconditions
- Export blob paths and cache keys are tenant-scoped by design; live blob/cache isolation CONDITIONAL on Blob/Redis; DB-fallback isolation verified (not a silent gap).

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
