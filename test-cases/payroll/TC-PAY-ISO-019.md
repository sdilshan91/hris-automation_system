---
id: TC-PAY-ISO-019
user_story: US-PAY-005
module: Payroll
priority: high
type: security
status: draft
created: 2026-06-15
---

# TC-PAY-ISO-019: The employee payslip surface is read-only -- no mutation endpoints exist, and any forged cross-tenant/cross-employee write is blocked (write isolation)

## 1. Test Objective
Verify FR-5 / FR-8 / BR-4: US-PAY-005 introduces only read endpoints (list/detail/PDF) for the employee self-service surface; there is no employee-facing payslip mutation. This TC asserts that (a) no write path is exposed to employees, and (b) if a forged write is attempted (PUT/PATCH/DELETE, or a write with a body-injected `tenant_id`/`employee_id` aimed at another tenant's or employee's slip), it is rejected and no `payroll_slip` row is created, modified, or deleted -- the TenantInterceptor stamps tenant from context and the global query filter prevents resolving foreign rows.

## 2. Related Requirements
- User Story: US-PAY-005
- Acceptance Criteria: AC-4
- Functional Requirements: FR-5, FR-8
- Business Rules: BR-4 (read-only)
- Note: US-PAY-005 is read-only; this ISO TC reuses the write-isolation pattern from TC-PAY-ISO-015 to confirm the read surface introduces no new mutation seam.

## 3. Preconditions
- Tenant A "acme": EMP-A01 with a Finalized payslip `payslipA`.
- Tenant B "globex": EMP-B01 (attacker) authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Attacker | EMP-B01 (globex) | forged writes |
| Target | acme `payslipA` | foreign slip |
| Injected fields | body tenant_id=acme / employee_id=EMP-A01 | must be ignored/blocked |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Enumerate the employee payslip API; confirm allowed verbs. | Only GET (list/detail/PDF) is exposed to employees; no POST/PUT/PATCH/DELETE on my-payslips. |
| 2 | As EMP-B01 (globex), PUT/PATCH/DELETE my-payslips/{payslipA}. | 404/405/403; no acme slip modified or deleted (filter hides the row; method not allowed). |
| 3 | As EMP-B01, attempt any write with body `{ "tenantId": "acme", "employeeId": "EMP-A01", ... }`. | Body-supplied tenant/employee ignored; TenantInterceptor stamps globex; no acme write occurs. |
| 4 | Direct DB check after the attempts. | `payslipA` (acme) is byte-for-byte unchanged; no new globex-owned row references acme data. |
| 5 | As EMP-A01 (acme, legitimate), confirm there is still no self-write capability. | Even the owning employee cannot mutate their payslip via the self-service surface (BR-4). |

## 6. Postconditions
- No employee-facing write path exists; forged cross-tenant/cross-employee writes are blocked with zero data mutation.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
