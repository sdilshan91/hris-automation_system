---
id: TC-PAY-ISO-014
user_story: US-PAY-004
module: Payroll
priority: critical
type: security
status: pass
created: 2026-06-15
---

# TC-PAY-ISO-014: Payslip generate/download/preview APIs reject missing/invalid/mismatched tenant context; no cross-tenant IDOR via runId/employeeId

## 1. Test Objective
Verify AC-4: every payslip endpoint (generate, regenerate, retry, single-download, download-all, list, preview) resolves the tenant from the authenticated session/subdomain and rejects requests with NO tenant context (401/400), an INVALID/unknown subdomain (400/404), or a MISMATCH between the bearer's tenant and the `X-Tenant-Subdomain` header. A user authenticated in Tenant A cannot perform an IDOR by passing Tenant B's runId/employeeId in the path/body to act on B's payslips -- the tenant filter scopes the lookup so B's run is simply not found.

## 2. Related Requirements
- User Story: US-PAY-004
- Acceptance Criteria: AC-4
- Functional Requirements: FR-5, FR-6
- Security: tenant context resolution + IDOR prevention

## 3. Preconditions
- Tenant "acme" (A) user Maya with `Payroll.*.All`; Tenant "globex" (B) with its own run Rb + employees.
- Maya knows globex's runId Rb and a globex employeeId.

## 4. Test Data
| Scenario | Request | Expected |
|----------|---------|----------|
| No tenant context | header omitted, no resolvable subdomain | 401/400 |
| Invalid subdomain | X-Tenant-Subdomain: doesnotexist | 400/404 |
| Mismatch | bearer=acme, header=globex | 403/400 (no cross-tenant action) |
| IDOR | acme session targets globex Rb | 404 (not found in acme scope) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call generate/download with NO resolvable tenant (no subdomain, no header). | 401/400; request rejected before any payslip lookup. |
| 2 | Call with an invalid/unknown subdomain. | 400/404; no tenant resolved; no data exposed. |
| 3 | As Maya (bearer=acme) send `X-Tenant-Subdomain: globex` (mismatch). | Rejected (403/400); the server uses the authenticated tenant, never the spoofed header, to act. |
| 4 | As Maya, POST generate / GET download / GET preview targeting globex's runId Rb + globex employeeId (IDOR). | 404 Not Found -- Rb is outside acme's tenant scope; no globex payslip generated, downloaded, or previewed. |
| 5 | As Maya, POST retry on a globex slip id. | 404; no cross-tenant retry. |
| 6 | Confirm Maya's legitimate acme operations still succeed. | acme generate/download/preview work normally with the correct context. |

## 6. Postconditions
- Every payslip endpoint is tenant-context-gated; spoofed headers and cross-tenant IDs cannot reach another tenant's payslips.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
