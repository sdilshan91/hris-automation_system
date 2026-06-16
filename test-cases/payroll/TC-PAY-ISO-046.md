---
id: TC-PAY-ISO-046
user_story: US-PAY-012
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PAY-ISO-046: History/audit/export APIs reject missing/invalid/mismatched tenant context; no cross-tenant run-history or audit-entry IDOR via foreign run_id / audit_log_id

## 1. Test Objective
Verify AC-5 and FR-1/FR-4/FR-8: the payroll history, run-detail, audit-trail, and audit-export endpoints reject requests with missing, invalid, or mismatched tenant context, and block cross-tenant IDOR -- a user in Tenant B supplying a foreign run_id, audit_log_id, or resource_id belonging to Tenant A cannot read or export it.

## 2. Related Requirements
- User Story: US-PAY-012
- Acceptance Criteria: AC-5
- Functional Requirements: FR-1, FR-4, FR-5, FR-8

## 3. Preconditions
- Tenants "acme" (A) and "globex" (B) Active. A has run R_A and audit entry AUD_A with known ids.
- User authenticated in B; ability to forge requests (omit/alter tenant subdomain header, supply foreign ids).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Foreign run | R_A (acme) | IDOR target |
| Foreign audit id | AUD_A (acme) | IDOR target |
| Tenant context | missing / invalid / mismatched | rejection |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call history/audit APIs with NO tenant context (no subdomain/header). | Rejected (401/400) -- tenant context required; no data returned. |
| 2 | Call with an invalid/non-existent tenant subdomain. | Rejected; no fallback to a default tenant. |
| 3 | As B (valid globex context), GET run detail / timeline for R_A. | 404/403 -- the EF tenant filter excludes R_A; B cannot read A's run. |
| 4 | As B, GET / export a single audit entry AUD_A by id. | 404/403 -- no cross-tenant audit IDOR; export contains nothing from A. |
| 5 | As B, send a request whose JWT tenant != subdomain tenant (mismatch). | Rejected (401/403); the mismatch is detected; no data served. |
| 6 | Confirm error bodies leak no A data. | Responses are generic; no acme run figures, actor names, or audit fields disclosed. |

## 6. Postconditions
- All history/audit/export endpoints enforce tenant context and block cross-tenant IDOR; no A data reachable from B.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
