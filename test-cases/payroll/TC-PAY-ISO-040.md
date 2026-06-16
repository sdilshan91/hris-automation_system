---
id: TC-PAY-ISO-040
user_story: US-PAY-010
module: Payroll
priority: high
type: security
status: draft
created: 2026-06-16
---

# TC-PAY-ISO-040: Attendance/leave summary cache + reconciliation report cache + advisory-lock state are tenant-scoped (no cross-tenant cache leak) (AC-5, FR-8, NFR-1)

## 1. Test Objective
Verify AC-5 / FR-8 / NFR-1: any cache used to satisfy the NFR-1 fetch SLA (attendance/leave summary cache, reconciliation report cache) and the advisory attendance/leave lock registry are keyed by tenant_id, so a cached/locked entry for Tenant A is never served to or affected by Tenant B. A write/invalidation in one tenant affects only that tenant's cache entries.

## 2. Related Requirements
- User Story: US-PAY-010
- Acceptance Criteria: AC-5
- Functional Requirements: FR-6, FR-8
- Non-Functional Requirements: NFR-1
- NOTE: CONDITIONAL -- if attendance/leave summaries are fetched on demand without a cache layer today, this TC asserts that no shared/global cache key is used and that every fetch is tenant-filtered (the no-shared-key + tenant-scoped-lock guarantees still hold).

## 3. Preconditions
- Tenant A "acme" + Tenant B "globex"; each with finalized attendance + approved leave for the same period; overlapping employee names.
- A cache layer (per S10) if present; otherwise on-demand fetch path.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Cache surfaces | attendance/leave summary cache, reconciliation report cache | tenant-keyed |
| Lock registry | advisory attendance/leave lock | tenant-scoped |
| Period | same in A and B | overlap |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme HR, fetch the attendance/leave summary + reconciliation report (populating any cache). | acme entries cached under a tenant-scoped key (e.g. `tenant:{acmeId}:payroll:attendance:{period}`); no shared/global key. |
| 2 | As globex HR, fetch the same period's summary/report. | globex receives globex data only -- never a cached acme entry (no cross-tenant cache hit). |
| 3 | Invalidate/refresh acme's attendance cache (e.g. after a regularization). | Only acme's cache entries are invalidated; globex's remain intact. |
| 4 | If no cache exists today, inspect the fetch path. | Each fetch is tenant-filtered at the query level with no shared/global key; CONDITIONAL note applies. |
| 5 | Apply the advisory lock for acme's run; inspect the lock registry. | The lock entry is tenant-scoped; globex's lock state is independent (FR-6, FR-8). |
| 6 | Verify the NFR-1 fetch SLA holds with tenant-scoped caching. | The 5,000-employee fetch meets <=2min using only acme's tenant-scoped cache/queries (NFR-1; links to TC-PAY-010-11). |

## 6. Postconditions
- Attendance/leave + reconciliation caches and the advisory-lock registry are tenant-scoped; no cross-tenant cache hit or lock interference.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
