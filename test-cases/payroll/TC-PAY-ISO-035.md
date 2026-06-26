---
id: TC-PAY-ISO-035
user_story: US-PAY-009
module: Payroll
priority: critical
type: security
status: pass
created: 2026-06-16
---

# TC-PAY-ISO-035: Cross-tenant report generation/write blocked; tenant_id applied at query level (not client-supplied); generated export/bank-advice/tax-statement files stored under a tenant-scoped path

## 1. Test Objective
Verify AC-5 / FR-8: report generation derives tenant_id from the resolved session context and applies it as a query-level filter -- a client cannot widen scope by injecting a tenant_id/department_id/run_id from another tenant into the request body or filter params. Any generated artefact (export workbook/CSV/PDF, bank advice file, tax-statement ZIP, pre-aggregation row) is written under a tenant-scoped storage path/key derived server-side, so no report process ever writes into or reads from another tenant's storage namespace.

## 2. Related Requirements
- User Story: US-PAY-009
- Acceptance Criteria: AC-5
- Functional Requirements: FR-1, FR-2, FR-4, FR-5, FR-8

## 3. Preconditions
- Tenant A "acme" + Tenant B "globex", each with a Finalized run.
- HR user authenticated in acme; a globex run_id / department_id / employee_id known to the tester.
- Export/bank-advice files stored at a tenant-prefixed path (e.g. `{tenantId}/payroll/reports/...`); pre-aggregation table keyed by tenant_id.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Injected run_id | a globex run | scope-widen attempt |
| Injected department_id | a globex department | scope-widen attempt |
| Server path | `{acme}/payroll/reports/{exportId}` | tenant-scoped |
| Pre-agg key | (tenant_id, pay_month, pay_year, department_id) | S7 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme HR, generate a report with a body/param-injected `tenant_id=globex`. | The injected tenant_id is ignored; the report is scoped to acme via the session-derived tenant_id; only acme data returned (FR-8). |
| 2 | As acme HR, pass a globex `run_id` / `department_id` as a report filter. | The foreign id resolves to nothing within acme's tenant filter -> empty/validation result; no globex rows pulled (FR-1, FR-8). |
| 3 | As acme HR, generate a bank advice / tax-statement and inspect where the file is written. | The artefact is written under acme's tenant-scoped path/key (server-derived); no write into globex's namespace (FR-2, FR-8). |
| 4 | Inspect the pre-aggregation upsert performed after a finalization. | Rows are written with the writing tenant's tenant_id; acme's finalization never mutates globex aggregate rows (FR-5, FR-8). |
| 5 | Attempt to make acme's export job emit a file path containing globex's tenant prefix. | The path is server-derived from the resolved tenant; client-supplied path components are ignored/rejected; no traversal into another tenant's folder (FR-8). |
| 6 | Confirm each tenant's own report generation + file write works normally. | Per-tenant generation, storage, and pre-aggregation function; isolation blocks only cross-tenant write/scope-widening. |

## 6. Postconditions
- Report scope + artefact storage paths + pre-aggregation writes are tenant-derived server-side; cross-tenant scope-widening and cross-namespace writes are blocked.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
