---
id: TC-PRF-ISO-038
user_story: US-PRF-010
module: Performance Management
priority: critical
type: security
status: fail
created: 2026-06-16
---

# TC-PRF-ISO-038: Recommendation APIs reject missing/invalid/mismatched tenant context + cross-tenant IDOR (view/create/override/submit/approve/auto-generate/export) (NFR-2)

## 1. Test Objective
Verify NFR-2 at the request boundary: every recommendation endpoint (workspace list, get-by-id, create, override, submit, approve/reject, auto-generate, budget, summary, export) rejects a request with NO tenant context, an INVALID tenant, or a tenant that MISMATCHES the JWT/subdomain; and an authenticated Tenant B user cannot act on a Tenant A recommendation by supplying its id (IDOR) -- on either a read or a state-changing action (override, submit, approve, export).

## 2. Related Requirements
- User Story: US-PRF-010
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-2 (auto-generate), FR-3 (override), FR-4 (approval), FR-6 (export), FR-8 (budget)

## 3. Preconditions
- Tenant "acme" (Tenant A) has a recommendation `acme_recId` Pending Approval + an approval task; Tenant "globex" (Tenant B) has its own HR Officer + manager.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme rec | acme_recId | target of the IDOR probe |
| globex caller | HR Officer | Tenant B |
| Tenant contexts | none / invalid / mismatched | rejection probes |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call recommendation endpoints with NO tenant context (no subdomain/header) | Rejected -- tenant resolution fails; 400/401, no recommendation data returned or mutated. |
| 2 | Call with an INVALID/unknown tenant subdomain | Rejected -- tenant does not resolve; no data leaked. |
| 3 | Authenticate as globex but send `X-Tenant-Subdomain: acme` (mismatch vs the globex JWT) | Rejected -- the mismatch is detected; the request does not execute in acme's context. |
| 4 | As globex, `GET .../recommendations/{acme_recId}` (IDOR read) | 404/403 -- the global query filter excludes acme; never 200 with acme data. |
| 5 | As globex, attempt to override / submit / approve / export `acme_recId` (IDOR write) | 404/403 -- no acme recommendation is modified, submitted, approved, or exported by a globex caller. |
| 6 | As globex, run auto-generate or set a budget targeting acme's cycleId | 404/403 -- the cycle resolves only within the caller's tenant; no acme suggestions/budget created. |

## 6. Postconditions
- All recommendation endpoints fail closed on missing/invalid/mismatched tenant context; cross-tenant IDOR is blocked on both reads and state-changing actions. No acme data exposed or mutated by globex.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
