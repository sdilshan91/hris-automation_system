---
id: TC-PAY-ISO-022
user_story: US-PAY-006
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-PAY-ISO-022: Statutory configuration APIs reject missing / invalid / mismatched tenant context; no cross-tenant IDOR on rule or slab ids

## 1. Test Objective
Verify AC-4 / FR-8: every statutory configuration endpoint (list/create/update/delete rules, slabs, social-security rules, exemptions, resolve, test-calc) requires a valid resolved tenant context. Requests with no tenant subdomain, an unknown/invalid subdomain, or a tenant context that mismatches the authenticated user's membership are rejected (401/403/400 as appropriate) -- never silently served against a default or another tenant. A globex-authenticated request supplying an acme rule/slab id (IDOR attempt) returns 404, not acme's record.

## 2. Related Requirements
- User Story: US-PAY-006
- Acceptance Criteria: AC-4
- Functional Requirements: FR-1, FR-2, FR-8
- Data Requirements: S7 (tenant_id-scoped tables)

## 3. Preconditions
- Tenant A "acme" with statutory rule `ruleA` / slab `slabA`; Tenant B "globex" with user `globexUser`.
- Reserved/unknown subdomains available for negative cases.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Valid context | globex | globexUser's tenant |
| No context | (missing X-Tenant-Subdomain) | reject |
| Invalid context | notatenant.yourhrm.com | reject |
| Mismatched context | acme token + globex subdomain | reject |
| IDOR target | acme `ruleA` / `slabA` | 404 in globex scope |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call a statutory endpoint with NO tenant context (no subdomain/header). | Rejected (tenant resolution fails); no rules returned; no default-tenant fallback. |
| 2 | Call with an unknown/invalid subdomain. | Rejected (tenant not found); no rules returned. |
| 3 | As globexUser, present a token while the request resolves to acme (mismatched context). | Rejected (membership mismatch); no acme rules returned. |
| 4 | As globexUser (valid globex context), GET/PUT/DELETE acme's `ruleA` and `slabA` by id (IDOR). | 404 Not Found on every verb; no acme record read or mutated. |
| 5 | As globexUser, call resolve/test-calc; confirm only globex rules are used. | Calculation uses globex's rule set only; no acme slab leaks into the result. |
| 6 | Confirm error bodies are generic. | No stack traces, no acme identifiers/values, no cross-tenant data in any error response. |

## 6. Postconditions
- All statutory endpoints fail closed without a valid, matching tenant context; cross-tenant IDOR is blocked.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
