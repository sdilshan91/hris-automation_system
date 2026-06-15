---
id: TC-PAY-ISO-018
user_story: US-PAY-005
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-PAY-ISO-018: My-payslips list / detail / PDF endpoints reject missing, invalid, or mismatched tenant context; no cross-employee or cross-tenant IDOR

## 1. Test Objective
Verify AC-4 / FR-5 / FR-8: the employee payslip endpoints require a resolved tenant context AND derive the employee from the authenticated principal, never from client-supplied identifiers. Requests with no tenant context, an invalid/unknown subdomain, or a tenant context that mismatches the bearer token's tenant are rejected. A valid request cannot be turned into an IDOR by swapping the payslip id (another employee, same or other tenant) or by injecting an `employee_id` / `tenant_id` parameter.

## 2. Related Requirements
- User Story: US-PAY-005
- Acceptance Criteria: AC-4
- Functional Requirements: FR-5, FR-8
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Tenant A "acme": EMP-A01 (`Payroll.Read.Self`), payslip `payslipA`.
- Tenant B "globex": EMP-B01, payslip `payslipB`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Endpoints | GET my-payslips, my-payslips/{id}, my-payslips/{id}/pdf | FR-1/FR-4 |
| No-context request | omit X-Tenant-Subdomain / unknown subdomain | reject |
| Mismatch | acme token + globex subdomain | reject |
| IDOR target | EMP-A01 token + `payslipB` (globex) | 404 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call the endpoints with NO tenant context (no subdomain / unresolved). | Rejected (400/401); no payslip data returned. |
| 2 | Call with an invalid/unknown subdomain. | Tenant resolution fails; request rejected; no data. |
| 3 | As EMP-A01 (acme token) but with `X-Tenant-Subdomain: globex` (mismatch). | Rejected (401/403); the token's tenant must match the resolved tenant; no globex data served. |
| 4 | As EMP-A01 (acme), GET my-payslips/{payslipB} (globex id). | 404 Not Found (filtered); no cross-tenant IDOR. |
| 5 | As EMP-A01 (acme), GET my-payslips/{EMP-A02 own-tenant slip id}. | 403/404 (cross-employee, Self scope); no other employee's data. |
| 6 | As EMP-A01, add `?employeeId={EMP-A02}` / `?tenantId={globex}` query/body params. | Ignored; employee + tenant resolved from the authenticated context only; still scoped to EMP-A01/acme. |

## 6. Postconditions
- All payslip endpoints fail closed without a valid matching tenant context and resist cross-employee / cross-tenant IDOR.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
