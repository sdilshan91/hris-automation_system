---
id: TC-ONB-ISO-021
user_story: US-ONB-006
module: Onboarding / Offboarding
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ONB-ISO-021: Missing/invalid tenant context + cross-tenant exit-interview ID injection -> 404

## 1. Test Objective
Verify AC-5 and FR-6: API requests without a resolvable tenant context are rejected, and a request that injects another tenant's exit interview id (or offboarding_id) returns 404 (existence not disclosed) rather than 403 — consistent with the platform's existence-non-disclosure stance.

## 2. Related Requirements
- User Story: US-ONB-006
- Acceptance Criteria: AC-5
- Functional Requirement: FR-6 (tenant_id from session)
- Cross-cutting: mandatory multi-tenant isolation

## 3. Preconditions
- Exit interview EI-B900 exists in tenant `globex` (T-globex).
- An HR Officer authenticated in tenant `acme` (T-acme).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| foreign interview id | EI-B900 | belongs to T-globex |
| caller | acme HR Officer | T-acme |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call an exit interview endpoint with no/invalid tenant context (no subdomain, no X-Tenant-Subdomain) | Request rejected (tenant cannot be resolved); no data returned. |
| 2 | As the acme HR Officer, GET exit interview EI-B900 by id | 404 Not Found — the foreign record is invisible under the acme query filter; existence not disclosed (not 403). |
| 3 | Attempt to edit/version EI-B900 from the acme context | 404; no write occurs against the globex record. |
| 4 | Attempt to view analytics scoped to a foreign offboarding id | 404 / empty; no foreign data surfaced. |

## 6. Postconditions
- No cross-tenant exit interview is readable or writable via id injection; missing tenant context is rejected.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
