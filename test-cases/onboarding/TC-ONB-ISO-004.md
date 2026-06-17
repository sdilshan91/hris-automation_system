---
id: TC-ONB-ISO-004
user_story: US-ONB-001
module: Onboarding / Offboarding
priority: high
type: security
status: draft
created: 2026-06-17
---

# TC-ONB-ISO-004: Onboarding cache/lookup keys are tenant-scoped

## 1. Test Objective
Verify that any caching of onboarding templates (e.g. an assignable-template list or per-tenant template lookup) is keyed by `tenant_id`, so a cached entry populated by Tenant A can never be served to Tenant B. If no distributed cache layer is wired yet, assert the equivalent always-tenant-filtered property and flag the cache key as the target for when caching lands.

## 2. Related Requirements
- User Story: US-ONB-001
- Acceptance Criteria: AC-5
- Non-Functional Requirements: NFR-2
- Cross-cutting: mandatory multi-tenant isolation (cache-key scoping)

## 3. Preconditions
- Tenants `acme` and `globex` exist, each with at least one template.
- If a cache (e.g. Redis/in-memory) backs the template list, it is enabled; otherwise note "no cache layer wired" and assert the fallback property.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Cache key shape | `onboarding:templates:{tenant_id}` (target) | must embed tenant_id |
| Tenant A | acme | populates cache first |
| Tenant B | globex | must not read A's entry |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme, list templates twice (prime then hit cache) | Second response is a cache hit (if caching is enabled) returning only acme templates. |
| 2 | Inspect the cache key used | The key embeds `tenant_id` (e.g. `onboarding:templates:{acmeId}`); no global/un-keyed entry exists. |
| 3 | As globex, list templates | Returns ONLY globex templates — never acme's cached entry; a distinct tenant-scoped key (or DB read) is used. |
| 4 | (CONDITIONAL) If no cache layer is wired | Record "no distributed cache today"; assert that the list endpoint is always tenant-filtered at query time (TC-ONB-ISO-001/-003) and flag `onboarding:templates:{tenant_id}` as the required key shape when caching is introduced. |

## 6. Postconditions
- No cross-tenant cache leakage; cache keys are tenant-scoped (or the gap is explicitly recorded with the target key shape).

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
