---
id: TC-ONB-ISO-013
user_story: US-ONB-004
module: Onboarding / Offboarding
priority: critical
type: security
status: pass
created: 2026-06-17
---

# TC-ONB-ISO-013: Missing/invalid tenant context + cross-tenant asset ID injection -> 404

## 1. Test Objective
Verify AC-5 and FR-7: asset endpoints reject requests with missing/invalid tenant context, and a request that injects another tenant's asset_id (or issuance_id) returns 404 (not found) — existence is not disclosed across tenants — rather than 403.

## 2. Related Requirements
- User Story: US-ONB-004
- Acceptance Criteria: AC-5
- Functional Requirements: FR-7 (tenant_id from session)
- Cross-cutting: mandatory multi-tenant isolation

> PLATFORM NOTE: Cross-tenant ID injection asserts 404 NOT 403 — the EF global query filter makes the other tenant's row invisible, so it resolves as "not found" and does not disclose existence. Consistent with TC-ONB-ISO-002/006/009.

## 3. Preconditions
- Tenant A (`acme`) holds asset A-ID and issuance ISS-ID.
- A Tenant B (`globex`) user is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme asset id | A-ID | Tenant A row |
| acme issuance id | ISS-ID | Tenant A row |
| no-tenant request | request with no resolvable tenant context | expect rejection |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send an asset request with missing/invalid tenant context (no subdomain / unresolvable) | Rejected by tenant resolution; no asset data returned. |
| 2 | As the globex user, GET acme's asset by A-ID | 404 Not Found — not 403 (existence not disclosed, AC-5). |
| 3 | As the globex user, GET/modify acme's issuance by ISS-ID | 404 Not Found; no read or mutation of the acme record. |
| 4 | As the globex user, attempt to issue/return acme's asset A-ID | 404 Not Found; acme asset unchanged (FR-7). |
| 5 | As the acme user, GET A-ID | 200 — owner access works (positive control). |

## 6. Postconditions
- Cross-tenant ID injection yields 404; no-tenant requests rejected; acme records untouched by the globex user.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
