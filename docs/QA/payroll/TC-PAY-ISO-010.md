---
id: TC-PAY-ISO-010
user_story: US-PAY-003
module: Payroll
priority: critical
type: security
status: pass
created: 2026-06-15
---

# TC-PAY-ISO-010: Payroll run / slip APIs reject missing, invalid, or mismatched tenant context; no cross-tenant run/slip read

## 1. Test Objective
Verify AC-7 / FR-8: payroll run and payslip read endpoints require a valid, resolvable tenant context and reject requests that lack one or whose JWT tenant claim does not match the subdomain. A user in Tenant B cannot retrieve Tenant A's run status, summary, progress, or payslips by guessing a runId or employeeId (IDOR). Enforced via TenantResolutionMiddleware + EF Core global query filters.

## 2. Related Requirements
- User Story: US-PAY-003
- Acceptance Criteria: AC-7
- Functional Requirements: FR-1, FR-3, FR-8
- Data Requirements: S7 (tenant_id discriminator)

## 3. Preconditions
- Tenant "acme" (A) has a ReviewPending run R-acme with slips.
- Tenant "globex" (B) user authenticated; reserved/unknown subdomains available for negative cases.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Run under test | R-acme | belongs to acme |
| Attacker context | globex | Tenant B |
| Bad contexts | none / unknown-subdomain / mismatched-claim | rejection cases |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `GET /api/v1/payroll/runs/{R-acme}` with NO tenant context (no subdomain / no header). | Rejected -- 400/401 (tenant cannot be resolved); never 200 with acme run data. |
| 2 | `GET /api/v1/payroll/runs/{R-acme}` authenticated in globex (mismatched tenant). | 404 Not Found (query filter excludes acme); never 200; no run status/summary leaked. |
| 3 | `GET /api/v1/payroll/runs/{R-acme}/slips` and `.../slips/{slipId}` from globex (IDOR). | 404; no acme payslip / amounts / employee PII returned. |
| 4 | Subscribe to the run's SignalR progress group from globex using R-acme's id. | Subscription denied / yields no acme progress events (group is tenant-scoped, see TC-PAY-ISO-012). |
| 5 | Request with a JWT whose tenant claim != resolved subdomain (forged/mismatched). | Rejected; mismatch not honored; no cross-tenant access. |
| 6 | Repeat step 2 from acme context (control). | acme HR sees R-acme normally -- confirms the 404s are isolation, not a broken endpoint. |

## 6. Postconditions
- Run/slip endpoints fail closed without a matching tenant context; no cross-tenant run or payslip data is exposed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
