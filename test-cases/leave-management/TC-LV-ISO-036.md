---
id: TC-LV-ISO-036
user_story: US-LV-009
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-036: Team-calendar cache keys are tenant- (and scope-) scoped (NFR-2; Redis DEFERRED -- partial)

## 1. Test Objective
Verify that any cache key for team-calendar results embeds the tenant id (and the viewer's scope) so two tenants -- or a manager-scope vs an employee-scope viewer -- can never collide on or read each other's cached calendar. The Redis cache layer is DEFERRED module-wide; the key design is verified by design and the DB-backed isolation is verified live, with the live-cache portion recorded as conditional.

## 2. Related Requirements
- User Story: US-LV-009
- Non-Functional Requirements: NFR-1, NFR-2
- Note: Redis caching DEFERRED per docs/vault/modules/leave-management.md (no entity uses a cache layer yet; calendar read from leave_request). Documented tenant key prefix pattern: `tenant:{tenantId}:...`.

## 3. Preconditions
- Tenants "acme" and "globex"; within acme, a manager (team scope) and an employee (department-approved scope).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Key prefix | `tenant:{tenantId}:team_calendar:{scope}:{from}:{to}` | proposed pattern |
| Collision probe | same from/to both tenants | must not collide |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify the documented/proposed cache-key pattern | The key embeds `tenantId` (and scope: managerId/department, role), so acme and globex -- and manager vs employee within a tenant -- never share a key for the same date range. |
| 2 | (DEFERRED -- cache present) Warm acme's calendar, then read globex's | globex resolves under its own tenant-scoped key; acme's cached value is never served. Mark CONDITIONAL on the Redis layer. |
| 3 | (DEFERRED -- cache present) Confirm manager-scope key != employee-scope key | A manager (full detail) and an employee (approved-only, no type) within the same tenant cannot share a cache entry -- preventing the employee from reading the manager's richer cached payload. |
| 4 | DB-fallback (live, cache absent) | With no cache layer, calendars are recomputed per-tenant/scope from leave_request via the EF global query filter and remain isolated (cross-ref TC-LV-ISO-035). |

## 6. Postconditions
- Calendar cache-key design is tenant- and scope-scoped; live cache verification deferred to Redis; DB-fallback isolation confirmed (not a silent gap).

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
