---
id: TC-PRF-ISO-020
user_story: US-PRF-005
module: Performance Management
priority: high
type: security
status: pass
created: 2026-06-16
---

# TC-PRF-ISO-020: Hangfire reviewer-reminder jobs, results/aggregation caches, and 360 notifications are tenant-scoped (NFR-2)

## 1. Test Objective
Verify NFR-2 across the asynchronous + cached surfaces of 360: the Hangfire reviewer-reminder job runs per-tenant and only over that tenant's non-submitters; any results/aggregate/completion-tracker cache key is tenant-scoped (no shared/global key); and assignment/reminder/results-available notifications are delivered only to recipients within the owning tenant.

## 2. Related Requirements
- User Story: US-PRF-005
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-6, FR-7, FR-8
- Acceptance Criteria: AC-2, AC-5

## 3. Preconditions
- Tenants "acme" and "globex" each have an active 360 review with non-submitting reviewers.
- Reviewer-reminder Hangfire job configured for both tenants.
- A cache layer (S10) may or may not be present (assert conditionally).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenants | acme, globex | both with pending reviewers |
| Surfaces | Hangfire reminders, results cache, notifications | tenant-scoped |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run the reviewer-reminder job | Each run executes in a single tenant's context and reminds only that tenant's non-submitters; acme's job never reminds globex reviewers and vice versa (NFR-2). |
| 2 | Inspect any results/aggregate/completion-tracker cache keys | Keys are namespaced by tenant_id (e.g. `360:results:{tenantId}:{revieweeId}`); no shared/global key. If results are computed on demand (no cache today), assert the query is tenant-filtered with no shared key — CONDITIONAL on S10. |
| 3 | Populate acme's results cache, then read as globex | globex never receives acme's cached results; cache is not shared across tenants. |
| 4 | Trigger assignment + results-available notifications in acme | Notifications target only acme recipients; no globex user is notified about acme 360 activity (delivery CONDITIONAL on Notification System S25 — assert the enqueue scoping). |
| 5 | Verify Hangfire job args carry tenant context | Each scheduled 360 reminder job carries its tenant_id and resolves the correct tenant on execution. |

## 6. Postconditions
- 360 reminders, caches, and notifications are strictly tenant-scoped; no cross-tenant leakage on async/cached paths.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
