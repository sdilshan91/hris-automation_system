---
id: TC-PAY-ISO-027
user_story: US-PAY-007
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-PAY-ISO-027: Cross-tenant adjustment writes blocked -- tenant_id session-derived; body-injected tenant_id ignored; foreign employee_id / reference_payroll_slip_id rejected; bulk-CSV employee_no resolves within tenant only; document path server-derived

## 1. Test Objective
Verify AC-5 / FR-1 / FR-2 / FR-8 / NFR-5: adjustment writes (single create, cancel, recurring series generation, bulk CSV import, document upload) are always stamped with the session/resolved tenant_id by the TenantInterceptor. A client-supplied `tenant_id` in the body is ignored; an adjustment cannot link to another tenant's `employee_id` or `reference_payroll_slip_id`; bulk-CSV `employee_no` values resolve only within the caller's tenant; and the supporting-document blob path is server-derived as `{resolvedTenantId}/payroll/adjustments/{id}/`, not client-controlled.

## 2. Related Requirements
- User Story: US-PAY-007
- Acceptance Criteria: AC-5
- Functional Requirements: FR-1, FR-2, FR-8
- Non-Functional Requirements: NFR-5
- Business Rules: BR-1, BR-5

## 3. Preconditions
- Tenant A "acme": Employee A1, a finalized payslip `slipA`. Tenant B "globex": user authenticated with `Payroll.*.All`, Employee B1.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Body-injected | `{ tenant_id: "acme", employee_id: B1, amount: 1000 }` from globex | tenant_id must be ignored |
| Foreign employee | globex create referencing acme Employee A1 | reject |
| Foreign slip | globex Correction with reference_payroll_slip_id=`slipA` (acme) | reject |
| Bulk CSV | globex CSV containing an acme employee_no | row rejected/unresolved |
| Path injection | document upload with path `../acme/payroll/adjustments/x/` | sanitized/rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As globex, POST an adjustment with body `tenant_id="acme"`. | The row is stamped tenant_id=globex (session-derived); the injected `acme` value is ignored; no acme row created (FR-8). |
| 2 | As globex, create an adjustment whose employee_id is acme's Employee A1. | Rejected -- the foreign employee_id resolves to no in-scope employee (BR-1 active-structure check runs within globex scope); no cross-tenant link. |
| 3 | As globex, create a Correction with reference_payroll_slip_id=`slipA` (an acme payslip). | Rejected -- the referenced payslip is outside globex scope; arrears reference cannot cross tenants (BR-5, FR-8). |
| 4 | As globex, upload a bulk CSV containing an acme employee_no. | That row fails resolution within globex (unknown employee) and is rejected in the preview; never creates an adjustment against acme's employee (FR-2, FR-8). |
| 5 | As globex, upload a supporting document and attempt to control the blob path (`../acme/...`). | The stored path is server-derived `globex/payroll/adjustments/{id}/...`; path traversal is sanitized/rejected; nothing written under acme's prefix (NFR-5, FR-8). |
| 6 | Verify acme's adjustments/documents are completely unaffected by all attempts. | Zero acme rows created/modified; acme blob prefix untouched. |

## 6. Postconditions
- All adjustment/document writes are tenant-stamped server-side; no cross-tenant write, link, or path leak.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
