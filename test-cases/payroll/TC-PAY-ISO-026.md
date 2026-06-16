---
id: TC-PAY-ISO-026
user_story: US-PAY-007
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-PAY-ISO-026: Adjustment create/list/detail/cancel/upload/download APIs reject missing, invalid, or mismatched tenant context; no adjustment or document IDOR

## 1. Test Objective
Verify AC-5 / FR-1 / FR-2 / FR-8: every payroll-adjustment endpoint (create, list, detail, cancel, bulk-CSV upload, document upload/download) requires a valid resolved tenant context. Requests with no tenant context, an invalid/unknown subdomain, or a tenant context mismatched to the authenticated user's membership are rejected; an authenticated user cannot reach another tenant's adjustment/document by id (no IDOR).

## 2. Related Requirements
- User Story: US-PAY-007
- Acceptance Criteria: AC-5
- Functional Requirements: FR-1, FR-2, FR-8

## 3. Preconditions
- Tenant A "acme" with adjustment `adjA` + document; Tenant B "globex" user authenticated with `Payroll.*.All`.
- TenantResolutionMiddleware + auth active.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| No tenant ctx | (header/subdomain omitted) | reject |
| Invalid tenant | `X-Tenant-Subdomain: nope-not-a-tenant` | reject |
| Mismatch | globex token + `X-Tenant-Subdomain: acme` | reject |
| IDOR target | acme `adjA`, acme document path | 404 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call create / list / detail / cancel / bulk-upload / document-download with NO tenant context resolvable. | Each rejected (400/401) -- no adjustment operation proceeds without a tenant scope. |
| 2 | Call the same endpoints with an invalid/unknown subdomain. | Rejected; tenant resolution fails closed; no data returned. |
| 3 | As a globex-authenticated user, send `X-Tenant-Subdomain: acme` (mismatch). | Rejected -- the server binds tenant scope from the authenticated membership/resolution, not a client-asserted mismatched header; no acme data. |
| 4 | As globex (valid globex context), GET acme's `adjA` by id and POST a cancel to acme's `adjA`. | 404 Not Found on both -- adjustment is outside globex scope; no cross-tenant IDOR. |
| 5 | As globex, request the document download for acme's adjustment id / blob path. | 404/403; no acme document bytes; no path-based IDOR. |
| 6 | As globex with a valid globex adjustment id, repeat all calls. | Succeed normally -- rejection is specific to missing/invalid/mismatched/cross-tenant access. |

## 6. Postconditions
- All adjustment/document endpoints fail closed without a valid matching tenant context; no cross-tenant IDOR.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
