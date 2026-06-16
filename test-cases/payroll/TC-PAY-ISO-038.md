---
id: TC-PAY-ISO-038
user_story: US-PAY-010
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PAY-ISO-038: Integration/reconciliation/encashment APIs reject missing/invalid/mismatched tenant context; no cross-tenant reconciliation/encashment IDOR (AC-5, FR-8)

## 1. Test Objective
Verify AC-5 / FR-8: the reconciliation-report, attendance/leave-integration, and leave-encashment endpoints require a valid tenant context (resolved from subdomain/JWT) and reject requests with missing, invalid, or mismatched tenant context. A user authenticated in Tenant A cannot read Tenant B's reconciliation report or trigger encashment against a Tenant B employee_id by manipulating ids in the URL/body (IDOR).

## 2. Related Requirements
- User Story: US-PAY-010
- Acceptance Criteria: AC-5
- Functional Requirements: FR-5, FR-7, FR-8

## 3. Preconditions
- Tenant A "acme" + Tenant B "globex"; each with finalized attendance + at least one employee with an encashable balance.
- Valid acme JWT; known globex employee_id / reconciliation-report identifiers.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Endpoints | reconciliation report, attendance/leave summary fetch, encashment trigger | tenant-scoped |
| IDOR probes | globex employee_id, globex period/report id in acme requests | must 403/404 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call the reconciliation/integration/encashment endpoints with NO tenant context (no subdomain/header, no tenant claim). | Rejected (401/400); no data returned. |
| 2 | Call with an INVALID/unknown tenant subdomain. | Rejected; tenant resolution fails closed. |
| 3 | As acme HR, request globex's reconciliation report by passing globex's report/period id. | 403/404 -- mismatched tenant context blocked; no globex rows returned (FR-7, FR-8). |
| 4 | As acme HR, trigger leave encashment passing a globex employee_id in the body/URL. | 403/404 -- the foreign employee_id does not resolve within acme; no encashment created (FR-5, FR-8). |
| 5 | As acme HR, request the attendance/leave summary for a globex employee_id. | Empty/forbidden -- the query is tenant-filtered; no globex attendance/leave leaked (FR-1/2/8). |
| 6 | Confirm tenant context is derived server-side. | Tenant comes from the resolved subdomain/JWT, never from a client-supplied tenant_id in the body (FR-8). |

## 6. Postconditions
- All integration/reconciliation/encashment endpoints reject missing/invalid/mismatched tenant context and block cross-tenant IDOR.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
