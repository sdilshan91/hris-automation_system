---
id: TC-LV-ISO-028
user_story: US-LV-007
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-028: Holiday cache keys are tenant-scoped (Redis DEFERRED -- partial)

## 1. Test Objective
Verify that the holiday-list cache-key design is tenant-scoped so no two tenants can collide on or read each other's cached holiday data. The Redis cache for holidays (NFR-1) is DEFERRED module-wide; this test verifies the tenant-scoped key pattern by design and the DB-fallback isolation, marking the live-cache portion conditional (NFR-1, NFR-2).

## 2. Related Requirements
- User Story: US-LV-007
- Non-Functional Requirements: NFR-1, NFR-2
- Note: Redis caching DEFERRED (per docs/vault/modules/leave-management.md). A tenant-scoped key pattern (e.g. `hrm:{tenantId}:holidays:{year}`) is verified by design; the DB-fallback path is isolated and verified live.

## 3. Preconditions
- Tenant "acme" and Tenant "globex" each have holidays for 2026.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Key pattern | `hrm:{tenantId}:holidays:{year}` (proposed) | tenant-scoped |
| Same year both tenants | 2026 | collision probe |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify the documented/proposed cache-key pattern | The key embeds `tenantId` (and year), so acme and globex never share a key even for the same year. |
| 2 | (DEFERRED -- cache present) Populate acme's holiday cache, then read globex's holidays | globex resolves under its own tenant-scoped key; acme's cached value is never served to globex. Mark CONDITIONAL on the Redis layer. |
| 3 | DB-fallback (live, cache absent) | With no cache layer, holiday lists are read per-tenant via the EF global query filter and remain isolated (cross-ref TC-LV-ISO-025/027). |
| 4 | Record deferral honestly | Live cache-key isolation is DEFERRED pending Redis; the tenant-scoped key pattern and DB-fallback isolation are verified now (not a silent gap). |

## 6. Postconditions
- Holiday cache-key design is tenant-scoped; live verification deferred to the Redis layer; DB-fallback isolation confirmed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
