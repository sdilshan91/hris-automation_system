---
id: TC-ADM-ISO-004
user_story: US-ADM-001
module: Admin Console
priority: high
type: security
status: blocked
created: 2026-06-16
---

# TC-ADM-ISO-004: Tenant config cache key is tenant-scoped (no cross-tenant cache bleed)

## 1. Test Objective
Verify FR-7 / AC-6: the initial tenant configuration cache written during provisioning uses a tenant-scoped key (`t:{tenantId}:config`) so a newly provisioned tenant's config can never be served to, or overwritten by, another tenant. There is no shared/global config key, and invalidating one tenant's config does not affect another's.

> Conditional note: FR-7 specifies Redis. If a distributed cache layer is not yet wired for tenant config, this test asserts the equivalent property at whatever caching/lookup layer exists today (key always includes `tenant_id`, queries always tenant-filtered) and flags the Redis-specific key as the target implementation.

## 2. Related Requirements
- User Story: US-ADM-001
- Acceptance Criteria: AC-6
- Functional Requirements: FR-7
- Cross-cutting: mandatory multi-tenant isolation (tenant-scoped cache keys)

## 3. Preconditions
- Tenant A (`alpha`) freshly provisioned (its `t:{alphaId}:config` populated per FR-7).
- Tenant B (`beta`) pre-exists with its own config cache entry.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| A config key | t:{alphaId}:config | written at provisioning |
| B config key | t:{betaId}:config | distinct key |
| Probe | request config under each context; inspect cache keys | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | After provisioning alpha, inspect the cache | A key `t:{alphaId}:config` exists carrying alpha's initial config; it contains no beta data. |
| 2 | Request tenant config under the alpha context, then under the beta context | Each receives only its own config; the keys differ by `tenant_id`; there is no shared/global `config` key serving both. |
| 3 | Update/invalidate alpha's config | Only `t:{alphaId}:config` is affected; `t:{betaId}:config` is untouched and beta continues to serve its own value. |
| 4 | Attempt to read `t:{alphaId}:config` while in the beta context via the normal config service | Beta's config service resolves its own key only; it cannot fetch alpha's key through the tenant-scoped accessor. |

## 6. Postconditions
- Tenant config caches are per-tenant keyed with no cross-tenant read, overwrite, or invalidation bleed.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
