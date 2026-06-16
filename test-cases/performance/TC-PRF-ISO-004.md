---
id: TC-PRF-ISO-004
user_story: US-PRF-001
module: Performance Management
priority: high
type: security
status: draft
created: 2026-06-16
---

# TC-PRF-ISO-004: Goal list / team-dashboard caches and notifications are tenant-scoped (NFR-1, NFR-2)

## 1. Test Objective
Verify NFR-2 at the caching/notification layer: any cache backing the team goal list / dashboard (per NFR-1) is keyed per tenant, so a write or read in Tenant A never serves or invalidates Tenant B's cached data, and goal-assignment notifications are delivered only within the owning tenant.

## 2. Related Requirements
- User Story: US-PRF-001
- Non-Functional Requirements: NFR-1, NFR-2
- Functional Requirements: FR-7 (notification)

## 3. Preconditions
- acme and globex each have a manager with direct reports and goals in their active cycles.
- (If a cache layer exists per S10) team goal lists are cacheable.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Cache key pattern | `tenant:{tenantId}:performance:goals:{cycleId}:...` | must include tenant id |
| Notification | goal-assignment in-app message | delivered to the employee in the same tenant only |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load acme's team goal list (populates cache), then globex's | Each tenant's cached entry uses a distinct tenant-scoped key; no shared/global key. CONDITIONAL — if no cache today, assert each read issues tenant-filtered queries and returns only its tenant's rows. |
| 2 | Write a goal in acme (cache invalidation) | Only acme's cache entry is invalidated/refreshed; globex's cached list is untouched and still correct. |
| 3 | Read globex's list immediately after the acme write | Returns globex data only; no acme goal appears via a stale/shared cache. |
| 4 | Assign goals to an acme employee and confirm the notification target | The in-app (and enqueued email) goal-assignment notification reaches only the acme employee; no globex user receives it (FR-7). |

## 6. Postconditions
- Caches and notifications are strictly tenant-scoped; no cross-tenant cache hit, invalidation bleed, or notification leak.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
