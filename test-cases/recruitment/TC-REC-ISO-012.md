---
id: TC-REC-ISO-012
user_story: US-REC-003
module: Recruitment
priority: high
type: security
status: pass
created: 2026-06-15
---

# TC-REC-ISO-012: Pipeline board cache, signed resume URLs and stage-config are tenant-scoped (no collision or cross-tenant leak)

## 1. Test Objective
Verify AC-5 / NFR-3 / NFR-5: any caching of pipeline board data is keyed by tenant (and vacancy), so identical vacancy ids across tenants never collide or serve another tenant's board; signed resume URLs opened from a detail panel are short-lived and scoped so they cannot be used to fetch another tenant's resume; and pipeline stage configuration is read per-tenant.

## 2. Related Requirements
- User Story: US-REC-003
- Acceptance Criteria: AC-5
- Non-Functional Requirements: NFR-3, NFR-5
- Data Requirements: S7 (tenant-scoped board data; signed short-lived resume URLs)

## 3. Preconditions
- Tenant "acme" and tenant "globex" each have a vacancy and applicants; (optionally) both have a vacancy with the same UUID-shaped id only by contrivance, or distinct ids with overlapping cache namespaces.
- A board cache may exist (key like `tenant:{tenantId}:vacancy:{vacancyId}:pipeline`). NOTE: If the board cache is DEFERRED in the delivered increment, Steps 1-2 assert the cache-KEY contract as a CONDITIONAL design check (no Redis required to pass); the DB path is already isolated by EF query filters.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Vacancy + applicants + resumes |
| Tenant B | globex | Vacancy + applicants + resumes |
| Cache key shape | `tenant:{tenantId}:vacancy:{vacancyId}:pipeline` | Must include tenant id |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load acme's pipeline (populates any cache), then load globex's pipeline for a same-shaped vacancy id | globex receives only globex's board; no acme cards/counts appear -- cache keys are tenant-scoped (no collision). |
| 2 | Inspect the cache key (if a cache is used) | The key includes the tenant id; reusing a vacancy id under a different tenant yields a distinct key -> no cross-tenant cache hit. |
| 3 | In acme, open an applicant detail and capture the signed resume URL | The URL is short-lived and scoped; no raw/permanent blob URL is exposed (NFR-5). |
| 4 | As globex (or anonymously), attempt to reuse acme's captured signed resume URL | Denied/expired; the URL cannot be used to fetch acme's resume from a different tenant context. |
| 5 | Verify pipeline stage configuration read | Stage config (names/order) is read per-tenant; globex's custom stages do not appear on acme's board and vice versa. |

## 6. Postconditions
- No cross-tenant board, cache entry, resume, or stage config was leaked; cache keys and signed URLs are tenant-scoped.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
