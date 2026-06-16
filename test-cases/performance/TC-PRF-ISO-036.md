---
id: TC-PRF-ISO-036
user_story: US-PRF-009
module: Performance Management
priority: high
type: security
status: draft
created: 2026-06-16
---

# TC-PRF-ISO-036: Tenant-scoped stale-detection Hangfire job + nudge/manager/HR notifications + attachment storage + goal-list/summary caches (NFR-2)

## 1. Test Objective
Verify NFR-2 for the cross-cutting infrastructure of goal tracking: the daily stale-goal detection Hangfire job runs per tenant and only ever reads/flags that tenant's goals; nudge, update, and Blocked (manager+HR) notifications are scoped to the acting tenant's recipients; progress-update attachments are stored under tenant-scoped paths; and any goal-list / overall-completion / team-summary cache key is tenant-scoped (no shared/global key). Conditional on the cache layer (S10) -- if computed on demand, assert tenant-filtered queries with no shared key.

## 2. Related Requirements
- User Story: US-PRF-009
- Non-Functional Requirements: NFR-2, NFR-5 (job)
- Functional Requirements: FR-5 (manager notify), FR-6 (stale job), FR-8 (comments); BR-3 (Blocked -> manager+HR), BR-4 (stale interval)
- Data Requirements: S7

## 3. Preconditions
- Tenant "acme" and Tenant "globex" both have active goals (some stale), employees, managers, HR, attachments.
- Hangfire + Notification System (S25) available; a cache layer may or may not be wired (S10).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenants | acme, globex | both have stale goals |
| Job | stale-goal detection | per-tenant run |
| Notifications | nudge, update, Blocked | tenant recipients only |
| Cache key | (tenant, employee/team, cycle) | tenant-scoped |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run the stale-detection job for acme | The job processes ONLY acme goals; globex stale goals are untouched by this run (tenant-scoped job). |
| 2 | Inspect acme nudges + "Needs Attention" flags | Nudges go only to acme employees; flags appear only on acme managers' dashboards; no globex recipient is notified or flagged. |
| 3 | Trigger a Blocked status change in acme (BR-3) | The manager+HR notification targets only acme's manager/HR recipients; no globex manager/HR receives it. |
| 4 | Upload a progress-update attachment in acme | The file is stored under an acme-scoped path/key; a globex user cannot resolve it (path is not shared/global). |
| 5 | Read Sam's goal list / overall-completion / team-summary twice; inspect any cache key | Any cache entry is keyed by acme tenant_id (+ employee/team/cycle) -- no shared/global key; a globex read never returns an acme-cached aggregate. If computed on demand, the query is tenant-filtered with no shared key. |
| 6 | Run the job for globex and confirm symmetry | globex's job touches only globex; acme is untouched -- both directions isolated. |

## 6. Postconditions
- Stale-detection jobs, notifications, attachment storage, and caches are all tenant-scoped; no cross-tenant job processing, notification, file access, or cache bleed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
