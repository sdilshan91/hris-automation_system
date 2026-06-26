---
id: TC-PAY-ISO-034
user_story: US-PAY-009
module: Payroll
priority: critical
type: security
status: pass
created: 2026-06-16
---

# TC-PAY-ISO-034: Report / export / bank-advice / tax-statement APIs reject missing/invalid/mismatched tenant context; no cross-tenant IDOR on report, file, or job handle

## 1. Test Objective
Verify AC-5 / FR-8: all report-generation, export-download, bank-advice, and year-end-tax-statement endpoints require a valid resolved tenant context and reject requests with missing, invalid, or mismatched tenant context. A user authenticated in Tenant A cannot retrieve Tenant B's report data, download Tenant B's export/bank-advice file, or poll/fetch Tenant B's async report job handle by guessing/altering an id (IDOR) -- such attempts return 404/403, never B's bytes.

## 2. Related Requirements
- User Story: US-PAY-009
- Acceptance Criteria: AC-5
- Functional Requirements: FR-1, FR-2, FR-4, FR-8

## 3. Preconditions
- Tenant A "acme" + Tenant B "globex", each with a Finalized run, a generated export file, a bank advice file, and an async report job.
- HR user authenticated in acme; ids of globex's export file / bank advice / tax-statement ZIP / report job known to the tester.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| globex export id | EXP-globex-001 | IDOR target |
| globex bank-advice id | BA-globex-001 | IDOR target |
| globex report job id | JOB-globex-001 | async handle IDOR |
| Tenant context | none / invalid / mismatched (acme token + globex subdomain) | rejection |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call each report/export/bank-advice/tax-statement endpoint with NO tenant context (no subdomain/header). | Rejected -- 400/401; no report generated or file served (FR-8). |
| 2 | Call with an invalid/unknown tenant subdomain. | Rejected; tenant resolution fails; no data (FR-8). |
| 3 | As acme HR, present a mismatched context (acme token + globex subdomain). | Rejected -- the resolved tenant must match the authenticated user's tenant; no globex data (FR-8). |
| 4 | As acme HR, request `GET /reports/exports/EXP-globex-001` (and the bank-advice / tax-statement ZIP ids). | 404/403; acme never receives globex's file bytes (IDOR blocked, FR-8). |
| 5 | As acme HR, poll the async report job handle JOB-globex-001. | 404/403; acme cannot read globex's job status or download its result (FR-4, FR-8). |
| 6 | Generate a report with a `?tenantId=globex` (or body) override while authenticated as acme. | The override is ignored; tenant_id is taken from the resolved session context; only acme data is returned (FR-8). |

## 6. Postconditions
- Report/export/bank-advice/tax-statement/job-handle endpoints enforce tenant context and block cross-tenant IDOR; no Tenant B bytes or status reach a Tenant A user.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
