---
id: TC-PAY-ISO-042
user_story: US-PAY-011
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PAY-ISO-042: Distribution APIs reject missing/invalid/mismatched tenant context; no cross-tenant send / re-send / status IDOR via a foreign run_id, payslip_email_log id, or employee_id

## 1. Test Objective
Verify AC-5 and FR-1/FR-4/FR-8: every payslip-distribution endpoint (Send Payslips, Re-send / Re-send All Failed, distribution-status/summary, email-log read) requires a valid resolved tenant context and operates strictly within it. Requests with no tenant context, an invalid/unknown subdomain, or a tenant-token-vs-subdomain mismatch are rejected; and a request authenticated in Tenant B that targets a Tenant A run_id / `payslip_email_log` id / employee_id is treated as not-found (IDOR blocked), never executing a cross-tenant send or exposing cross-tenant status.

## 2. Related Requirements
- User Story: US-PAY-011
- Acceptance Criteria: AC-5
- Functional Requirements: FR-1, FR-4, FR-5, FR-8

## 3. Preconditions
- Tenant "acme" (A) Finalized run Ra with email logs; Tenant "globex" (B) with user Bob (`Payroll.*.All`).
- Known acme identifiers: runId Ra, an acme email_log_id, an acme employeeId.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| No tenant context | missing subdomain/header | reject |
| Invalid tenant | unknown subdomain | reject |
| Mismatch | acme token + globex subdomain | reject |
| IDOR target | acme Ra / email_log_id / employeeId from globex | 404 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call Send Payslips / status with NO resolvable tenant (no subdomain/header). | Rejected (401/400); no job enqueued; no data returned (FR-8). |
| 2 | Call with an invalid/unknown tenant subdomain. | Rejected; tenant resolution fails closed; no distribution executed. |
| 3 | Call with a mismatched tenant token vs subdomain (acme token, globex host). | Rejected; the mismatch is detected; no cross-tenant action. |
| 4 | As Bob (globex), POST Send Payslips / Re-send for acme's runId Ra. | 404 (run not in globex scope); NO `SendPayslipEmailsJob` enqueued for acme; no acme emails sent (IDOR blocked). |
| 5 | As Bob, GET distribution status / a specific `payslip_email_log` id belonging to acme. | 404; acme delivery status / recipient never exposed. |
| 6 | As Bob, Re-send targeting an acme employeeId within Ra. | 404; no email sent to the acme employee. |

## 6. Postconditions
- All distribution endpoints fail closed without tenant context and reject cross-tenant run/log/employee targeting; no cross-tenant send or status leak.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
