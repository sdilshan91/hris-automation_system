---
name: us-chr-009-status-findings
description: US-CHR-009 employee-status REPRESENTATIVE test pass (2026-06-25) — BUG-021 (BUG-003 on status read surface), ISSUE-025 (status audit is write-only), state-machine + enum-binding facts
metadata:
  type: project
---

REPRESENTATIVE API pass of **US-CHR-009 employee status management**, 2026-06-25, debugger-free. 26 TCs: 14 PASS / 2 FAIL / 10 BLOCKED. Two new findings; BUG-003 extended.

**Contract facts (verify before re-testing — may drift):**
- `POST /api/v1/tenant/employees/{id}/status` needs perm `Employee.ChangeStatus`; `GET .../{id}/status/transitions` needs `Employee.View.All` (manager/employee lack it → 403 even on the read).
- `NewStatus` is the `EmployeeStatus` **enum** (Active=0,Probation=1,Inactive=2,Terminated=3,Suspended=4). JSON binding accepts the int OR the string name, **case-insensitively** (`"active"`==`Active`, System.Text.Json default) — this is acceptable, NOT a bug.
- State machine (`EmployeeStatusStateMachine.cs`, hardcoded): probation→{active,terminated}; active→{suspended,terminated,inactive}; suspended→{active,terminated}; inactive→{active,terminated}; **terminated=terminal** (empty). Invalid → 400 "{From} employees cannot be moved to {To}".
- Validation (fast 4xx = PASS): missing/empty reason→400, missing effectiveDate→400 (default DateTime caught by NotEmpty), invalid enum value 99→400. Idempotency-Key header dedupes (cached result, one history row).
- Status change writes: employee.Status + employee.IsActive (Active/Probation→true), an `EmploymentHistory` row (readable via `GET .../{id}/profile` → `employmentHistory[]`), and an `EmployeeFieldAuditLog` before/after row. Terminate/Suspend disables linked User login + revokes refresh tokens.

**BUG-021 (HIGH, BUG-003 class — DO NOT re-file as new root):** the status READ surface honors a spoofed `X-Tenant-Subdomain`. acme token + `X-Tenant-Subdomain: techoneglobal` → `GET .../{techo-emp}/status/transitions` 200 + `/profile` returns the techoneglobal employee's status/history/PII; honest-header control 404s. Same unvalidated-header root as [[us-adm-006-settings-findings]] BUG-003. Extends the affected-surface list (now: settings/workflows/audit/data-export/documents/org-tree/locations/**status+profile reads**).

**ISSUE-025 (MED, missing-audit/forensic-gap class):** status change DOES write a before/after snapshot to `employee_field_audit_logs` (`EmployeeStatusService.cs:174-184`), so NFR-5 is met at the WRITE layer — but that row is **write-only**: absent from queryable `GET /api/v1/tenant/audit-logs` AND no read endpoint over `EmployeeFieldAuditLog` exists. Only `/profile` employment-history is retrievable (actor/reason/prev/new value, NOT the structured snapshot). TC-230 FAIL. Same audit-gap class as ISSUE-024.

**Throwaway acme employees used (operate on these, NOT personas' linked records):** EMP-0004 `019efcf5-49bc-7cd0-81da-76d5a5dd05e7`, EMP-0005 `019efcf5-4d69-7752-a321-037bfef1be2d`, EMP-0006..0008. Run left EMP-0004 Active, EMP-0006 Terminated, EMP-0008 Suspended. **0 writes to techoneglobal** (cross-tenant probe was read-only GET). See [[qa-personas-reseed-2026-06-25]].
