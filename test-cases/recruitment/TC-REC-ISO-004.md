---
id: TC-REC-ISO-004
user_story: US-REC-001
module: Recruitment
priority: high
type: security
status: draft
created: 2026-06-15
---

# TC-REC-ISO-004: Vacancy caches, slugs, and public-page URLs are tenant-scoped (no cross-tenant collision or leakage)

## 1. Test Objective
Verify that any caching of vacancy lists/details and the SEO slug / public careers URL namespace are tenant-scoped, so two tenants can hold vacancies with identical titles/slugs without collision and one tenant's cached or public data is never served under another tenant's context.

## 2. Related Requirements
- User Story: US-REC-001
- Acceptance Criteria: AC-4
- Functional Requirements: FR-4, FR-5 (public page + slug)
- Non-Functional Requirements: NFR-1 (cached list), NFR-2 (tenant isolation)
- Business Rules: BR-5 (tenant-level public toggle)

## 3. Preconditions
- Tenant "acme" and Tenant "globex" each have an `Open`, publicly-listed vacancy titled "Software Engineer" (so slugs would collide if not tenant-scoped).
- Both tenants have the public careers page enabled.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme vacancy | "Software Engineer" (Open, public) | slug `software-engineer-...` |
| globex vacancy | "Software Engineer" (Open, public) | slug `software-engineer-...` |
| Expected cache key | `tenant:{tenantId}:vacancies:...` | Tenant-scoped key design |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Request the acme vacancy list, then the globex vacancy list under their respective contexts | Each returns only its own tenant's "Software Engineer"; no cross-tenant entry appears regardless of cache warmth. (If a Redis cache layer is wired, verify keys are tenant-prefixed, e.g. `tenant:{tenantId}:vacancies:list`; if not yet wired, verify the DB-backed read path returns correctly per tenant -- record which path was exercised.) |
| 2 | Open `https://acme.yourhrm.com/careers/{acme-slug}` and `https://globex.yourhrm.com/careers/{globex-slug}` anonymously | Each resolves to its own tenant's vacancy even though the slug strings collide; the slug is unique within a tenant, scoped under the tenant's subdomain. |
| 3 | Anonymously request the acme slug under the globex subdomain (and vice versa) | 404 -- a slug from one tenant does not resolve under another tenant's public careers page. |
| 4 | Edit the acme vacancy and re-request both tenants' lists/details | The acme change is reflected only under acme; globex's cached/served data is unaffected (cache invalidation is tenant-scoped). |

## 6. Postconditions
- Vacancy cache keys and public slugs/URLs are tenant-scoped; identical titles across tenants cause no collision or leakage.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
