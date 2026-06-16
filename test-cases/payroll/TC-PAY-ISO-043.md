---
id: TC-PAY-ISO-043
user_story: US-PAY-011
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PAY-ISO-043: Cross-tenant WRITE/send block -- the Hangfire SendPayslipEmailsJob runs under the tenant from its job args; payslip_email_log rows are server-tenant-stamped (TenantInterceptor); a body-injected tenant_id and a foreign run_id/payslip_id/employee_id are ignored/rejected; A's job never attaches or emails B's payslip

## 1. Test Objective
Verify AC-5 and FR-2/FR-8: the distribution job restores `ITenantContext` from its job arguments and operates strictly within that tenant. New `payslip_email_log` rows are stamped with the resolved tenant_id by the `TenantInterceptor` -- a client- or job-arg-injected `tenant_id` in the request body is ignored. A request carrying a foreign `run_id` / `payroll_slip_id` / `employee_id` (belonging to another tenant) is rejected/filtered out, so Tenant A's job can never fetch, attach, or email Tenant B's payslip PDF, and an attacker cannot enqueue a job that sends another tenant's payslips.

## 2. Related Requirements
- User Story: US-PAY-011
- Acceptance Criteria: AC-5
- Functional Requirements: FR-1, FR-2, FR-5, FR-8
- Data Requirements: S7 (tenant_id stamped on payslip_email_log)

## 3. Preconditions
- Tenant "acme" (A) Finalized run Ra; Tenant "globex" (B) Finalized run Rb with its own payslips at `{globexTenantId}/payroll/{Rb}/...`.
- HR users in each tenant; the ability to forge request bodies / job args.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Body-injected tenant_id | globexTenantId in an acme request | must be ignored |
| Foreign run_id | Rb (globex) in an acme send | rejected/filtered |
| Foreign slip/employee | globex payroll_slip_id / employee_id | rejected/filtered |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme HR, Send Payslips for Ra with a body-injected `tenant_id=globexTenantId`. | The injected tenant_id is ignored; the job runs under acme (from the resolved context/job args); all log rows stamped tenant_id=acme by TenantInterceptor (FR-8). |
| 2 | As acme HR, attempt Send/Re-send referencing globex's run_id Rb. | 404/rejected -- Rb is outside acme scope; no acme job touches globex's run; no globex payslips fetched/emailed. |
| 3 | As acme HR, Re-send referencing a globex `payroll_slip_id` / `employee_id`. | The foreign ids are filtered out (not in acme scope); no email sent containing globex's payslip; no acme log row links a globex slip. |
| 4 | Enqueue a forged `SendPayslipEmailsJob` with job args pointing at globex's run/tenant from an acme actor. | Either rejected at enqueue/authorization, or the job (running under acme context) finds no acme-scoped data for the foreign run and sends nothing -- A's actor cannot drive a send of B's payslips. |
| 5 | Inspect every `payslip_email_log` row written. | All rows carry the correct server-resolved tenant_id; none reference a cross-tenant payslip_slip_id/employee_id; no cross-tenant attachment was ever sent. |

## 6. Postconditions
- Distribution writes/sends are confined to the job's resolved tenant; injected tenant_id ignored; foreign run/slip/employee ids rejected; no cross-tenant payslip ever emailed.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
