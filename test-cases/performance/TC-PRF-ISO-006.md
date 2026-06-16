---
id: TC-PRF-ISO-006
user_story: US-PRF-002
module: Performance Management
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PRF-ISO-006: Self-assessment APIs reject missing/invalid/mismatched tenant context + cross-tenant IDOR (NFR-2)

## 1. Test Objective
Verify NFR-2 at the request-context boundary: self-assessment endpoints reject requests with no resolvable tenant context, an invalid/unknown tenant, or a tenant context that mismatches the JWT's tenant; and a valid Tenant B user cannot reach a Tenant A self-assessment by supplying its ID (cross-tenant IDOR).

## 2. Related Requirements
- User Story: US-PRF-002
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-1

## 3. Preconditions
- acme has Asha's self-assessment (id known); globex has a valid authenticated user.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme self-assessment id | known UUID | target of IDOR |
| globex JWT | valid | Tenant B caller |
| Tenant header | absent / `unknown` / mismatched vs JWT | rejection cases |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call a self-assessment endpoint with NO resolvable tenant (no subdomain / no `X-Tenant-Subdomain`) | Rejected -- 400/401; no data returned (tenant context required). |
| 2 | Call with an invalid/unknown tenant subdomain | Rejected -- tenant not resolved; no data. |
| 3 | Authenticate as globex but send `X-Tenant-Subdomain: acme` (mismatch vs JWT tenant) | Rejected -- the mismatch is detected; the request is not served acme data. |
| 4 | As a valid globex user, `GET .../self-assessments/{acme_assessment_id}` (IDOR) | 404/403 -- the acme record is never returned cross-tenant. |
| 5 | As a valid globex user, `PUT`/submit against the acme assessment id | Rejected; no write to acme's record. |

## 6. Postconditions
- Requests without valid, matching tenant context are rejected; cross-tenant IDOR by ID is blocked.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
