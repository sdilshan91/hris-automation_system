---
id: TC-PAY-ISO-025
user_story: US-PAY-007
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-PAY-ISO-025: Tenant B can never see, list, retrieve, or download Tenant A's payroll adjustments or their supporting documents (cross-tenant read isolation)

## 1. Test Objective
Verify AC-5 / FR-8: the payroll-adjustment read surface (`payroll_adjustment` rows + supporting-document blobs at `{tenantId}/payroll/adjustments/{id}/`) is tenant-scoped by EF Core global query filters + TenantInterceptor and a tenant-prefixed blob path. A user authenticated in Tenant B can NEVER list, read, or download Tenant A's adjustments or documents -- not via the list/detail/download APIs and not by guessing a Tenant A payroll_adjustment_id or blob path. (US-PAY-007 AC-5/FR-8 say "RLS"; this platform enforces via EF query filters -- if Postgres RLS is later added on `payroll_adjustment`, extend Step 5 to assert session-level isolation as defense-in-depth.)

## 2. Related Requirements
- User Story: US-PAY-007
- Acceptance Criteria: AC-5
- Functional Requirements: FR-8
- Data Requirements: S7 (tenant_id on payroll_adjustment; supporting_document_path tenant-prefixed)

## 3. Preconditions
- Tenant A "acme": payroll adjustments (Bonus/Deduction/Reimbursement) incl. one reimbursement with a stored document at `acme/payroll/adjustments/{adjA}/receipt.pdf`.
- Tenant B "globex": its own adjustments; user authenticated in globex with `Payroll.*.All`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | owns adjustments |
| Tenant B | globex | attacker context |
| Target | acme payroll_adjustment_id `adjA`; doc `acme/payroll/adjustments/{adjA}/receipt.pdf` | A's data |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As a globex user (`X-Tenant-Subdomain: globex`), list payroll adjustments. | Only globex's adjustments returned; zero acme rows in the result, counts, or filters. |
| 2 | As globex, GET the adjustment detail for acme's `adjA`. | 404 Not Found (filtered out of globex scope); no acme data leaked. |
| 3 | As globex, request the supporting-document download for acme's `adjA` (by id and by guessed blob path `acme/payroll/adjustments/{adjA}/receipt.pdf`). | Denied -- 404/403; no bytes of acme's document served; the tenant prefix is server-derived, not client-trusted. |
| 4 | As globex, filter the adjustments list by an acme employee_id / period. | Returns only globex's matching rows (or empty); never acme's, even when an acme employee_id is supplied. |
| 5 | Direct DB cross-check: query `payroll_adjustment` for `adjA` with tenant context = globex. | EF global query filter returns zero rows (tenant_id=acme != globex). (If RLS is added, a globex DB session also returns zero.) |
| 6 | Confirm globex's own adjustments + documents remain fully accessible. | globex list/detail/download work normally -- isolation blocks only cross-tenant access. |

## 6. Postconditions
- Tenant B cannot read, enumerate, or download Tenant A's adjustments or supporting documents by any path.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
