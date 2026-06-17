---
id: TC-NTF-ISO-012
user_story: US-NTF-003
module: Notifications & Audit
priority: high
type: security
status: draft
created: 2026-06-17
---

# TC-NTF-ISO-012: Dispatch-time preference lookup + cache keys are tenant+user scoped

## 1. Test Objective
Verify that when the Notification Dispatcher resolves a user's preferences at send time, it reads only
within the recipient's tenant, and that any preference cache key is scoped by both tenant and user --
so Tenant A's cached preference can never satisfy a Tenant B lookup, even for the same user_id value.

## 2. Related Requirements
- User Story: US-NTF-003
- Acceptance Criteria: AC-5 (per-tenant-membership isolation)
- Functional Requirements: FR-6 (preferences applied at dispatch), FR-8 (tenant_id + user_id)
- Non-Functional: NFR-2 (tenant isolation), NFR-3 (Redis cache for dispatch lookup -- deferred/CONDITIONAL)
- Business Rules: BR-4 (per tenant membership)

## 3. Preconditions
- The same user_id value exists in both Tenant A and Tenant B (cross-tenant user or coincidental id).
- In Tenant A "Leave Updates" Email = OFF; in Tenant B the same category Email = ON.
- NOTE: Redis preference caching (NFR-3) is deferred on the dev box; cache-key assertions are
  CONDITIONAL on Redis being wired -- otherwise assert the equivalent always-tenant-scoped DB lookup.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| cache key (if wired) | notif:prefs:{tenant_id}:{user_id} | tenant + user scoped |
| Tenant A: Leave Updates Email | false | |
| Tenant B: Leave Updates Email | true | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Dispatch a Tenant A leave event for the user | Dispatcher loads Tenant A preference; NO email sent (Email OFF in A); if Redis wired, entry cached under a key containing tenant_id=Tenant A |
| 2 | Dispatch a Tenant B leave event for the same user_id | Dispatcher loads Tenant B preference; email IS sent (Email ON in B) -- the Tenant A cached entry does NOT satisfy the Tenant B lookup |
| 3 | (If Redis wired) inspect cache keys | Two distinct keys exist, each containing its tenant_id; no shared key across tenants for the same user_id |
| 4 | (If Redis NOT provisioned) confirm the lookup path | Each dispatch reads fresh, always filtered by the recipient's tenant; CONDITIONAL cache items recorded as deferred |
| 5 | Verify no cross-tenant decision leakage | A change in Tenant A's preference never alters Tenant B dispatch behavior and vice versa |

## 6. Postconditions
- Dispatch-time preference resolution (and any cache) is strictly tenant+user scoped.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
