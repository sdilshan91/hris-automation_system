---
id: TC-ONB-ISO-023
user_story: US-ONB-006
module: Onboarding / Offboarding
priority: high
type: security
status: pass
created: 2026-06-17
---

# TC-ONB-ISO-023: Exit interview analytics cache + HR-notify outbox payload tenant-scoped

## 1. Test Objective
Verify AC-5 and NFR-2: any cached analytics aggregate (reason distribution, average ratings, trends) is keyed per tenant so Tenant A never serves Tenant B a cached result, and the self-service HR-notify outbox payload is tenant-scoped. If no distributed cache is wired yet, assert the equivalent always-tenant-filtered property and flag the target key shape.

## 2. Related Requirements
- User Story: US-ONB-006
- Acceptance Criteria: AC-5
- Non-Functional Requirement: NFR-2 (tenant isolation)
- Functional Requirement: FR-8 (HR notification on self-service submit)
- Cross-cutting: mandatory multi-tenant isolation

## 3. Preconditions
- Tenants `acme` and `globex` each have exit interview data.
- Analytics requested for both tenants; self-service submissions made in both.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| target cache key | `onboarding:exit-analytics:{tenant_id}` | tenant-scoped shape if cache wired |
| outbox payload | self-service notify intent | must carry tenant_id |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Request acme analytics (populating any cache), then request globex analytics | globex receives globex-only aggregates; the acme cache entry is never served to globex (cache keys are tenant-scoped, e.g. `onboarding:exit-analytics:{tenant_id}`). |
| 2 | Invalidate/refresh after a new acme interview | Only the acme analytics cache entry is affected; globex's entry is untouched. |
| 3 | Submit a self-service interview in acme and inspect the HR-notify outbox payload | The notification intent is tenant-stamped T-acme and addressed to acme HR only (FR-8). |
| 4 | If no distributed cache is wired | Assert analytics are always computed under the tenant query filter (equivalently isolated) and flag `onboarding:exit-analytics:{tenant_id}` as the target key shape for when caching is added. |

## 6. Postconditions
- Analytics caching (or its always-filtered equivalent) and the HR-notify outbox payload are strictly tenant-scoped.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
