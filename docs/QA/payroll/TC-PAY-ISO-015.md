---
id: TC-PAY-ISO-015
user_story: US-PAY-004
module: Payroll
priority: critical
type: security
status: pass
created: 2026-06-15
---

# TC-PAY-ISO-015: Cross-tenant payslip writes blocked -- the blob path + slip pdf_* fields are derived from the resolved/job-arg tenant, never client-supplied; a body-injected tenant_id is ignored

## 1. Test Objective
Verify AC-4 / FR-5: the payslip-generation write path is tenant-isolated. The blob storage prefix `{tenantId}/payroll/{runId}/{employeeId}.pdf` is built from the tenant resolved server-side (and, in the Hangfire worker, restored from the job arg), NOT from any client-supplied tenant_id/path. A request that injects a foreign `tenant_id` in the body, or a job crafted with a mismatched tenant arg, cannot cause Tenant A's PDFs to be written under Tenant B's prefix or stamp B's `payroll_slip` rows. Writes are stamped by `TenantInterceptor`; the `GeneratePayslipsJob` restores `ITenantContext` from its tenant arg.

## 2. Related Requirements
- User Story: US-PAY-004
- Acceptance Criteria: AC-4
- Functional Requirements: FR-4 (Hangfire job), FR-5 (storage path), FR-7 (status fields)
- Data Requirements: S7 (tenant_id discriminator)

## 3. Preconditions
- Tenant "acme" (A) run Ra ReviewPending; Tenant "globex" (B) exists.
- HR Maya with `Payroll.*.All` in acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Body-injected tenant_id | globexTenantId | must be ignored |
| Job tenant arg | acme | authoritative for the worker |
| Expected write prefix | {acmeTenantId}/payroll/{Ra}/ | server-derived |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Maya, POST generate for Ra with a JSON body that adds `"tenant_id": globexTenantId`. | The injected tenant_id is ignored; the job is enqueued with acme's tenant arg; PDFs are written under `{acmeTenantId}/payroll/{Ra}/`, NOT globex's prefix. |
| 2 | Inspect the enqueued GeneratePayslipsJob args. | tenant arg = acme (session-derived), run arg = Ra; no client-supplied tenant honored. |
| 3 | Worker runs; verify slip writes. | Every updated `payroll_slip.pdf_storage_path` is under acme's prefix; `tenant_id` on the rows remains acme (TenantInterceptor); no globex slip touched. |
| 4 | Simulate a job whose tenant arg mismatches the slips' tenant (defense check). | The worker's restored ITenantContext + EF filter means it can only see/update its tenant's slips; mismatched-tenant slips are not found/updated (no cross-tenant write). |
| 5 | Verify no PDF landed under globex's prefix. | `{globexTenantId}/payroll/{Ra}/` does not exist / is empty; globex slips unchanged. |
| 6 | Confirm acme generation otherwise succeeds normally. | Ra's acme PDFs generated and stamped correctly. |

## 6. Postconditions
- Payslip writes (blob path + slip fields) are bound to the server-resolved/job-arg tenant; cross-tenant write via injected tenant_id or path is impossible.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
