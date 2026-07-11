# Payroll — API-layer execution results (2026-06-19)

Scope: API-layer QA baseline against the **running** backend (`http://localhost:5000`), tenant `acme`.
No source/TC files modified. No browser. One safe write performed (create salary component). No payroll
run/approval executed (side-effecting + irreversible — out of scope by instruction).

## Persona → Payroll permissions (decoded from live JWTs)

Discovered by logging in each persona and decoding the `permissions` claim. This is the load-bearing
finding for every authZ verdict below.

| Persona (login) | Role | Payroll permissions in token |
|---|---|---|
| `tenantadmin@acme.test` | Tenant Admin | `Payroll.View`, `Payroll.Run`, `Payroll.Approve`, `Payroll.Configure`, `Payroll.Export`, `Payroll.ViewSensitive` (full suite) |
| `hr@acme.test` | HR Officer | **NONE** — HR Officer role grants **zero** payroll permissions |
| `manager@acme.test` | Manager | **NONE** |
| `employee@acme.test` | Employee | `Payroll.View.Own` only |

Source of truth: `HRM.Domain/Authorization/PermissionCatalog.cs` (TenantAdmin L609; HROfficer L644-660 has
no `Payroll.*`; Employee L674-686 has only `Payroll.ViewOwn`). All payroll config/run controllers gate on
`Payroll.Configure` / `Payroll.Run`; `MyPayslips` on `Payroll.View.Own`; bank-advice **full** on
`Payroll.ViewSensitive` (sensitive PII gate, separate from `Payroll.Export`).

> **Designed-TC vs. implementation mismatch (FLAG to caller):** the designed payroll TCs
> (e.g. TC-PAY-001-01, TC-PAY-002-01, TC-PAY-ISO-001) all assert the actor is an **"HR Officer with
> `Payroll.*.All`"**. In the seeded role model the HR Officer holds **no payroll permissions** — only
> Tenant Admin (or a custom role) can configure/run payroll. The TCs are not executable as written against
> the seeded HR Officer. Either the role seed or the TC actor needs reconciling. I did **not** alter either.
> (Permission also surfaces as the dotted form `Payroll.View.Own` in the JWT, matching catalog const
> `Payroll.ViewOwn = "Payroll.View.Own"` — consistent, no bug.)

## Results

| Endpoint / TC | Persona | Method | Verdict | HTTP | Evidence |
|---|---|---|---|---|---|
| `/payroll/salary-structures` (US-PAY-001) | Tenant Admin | GET | ✅ PASS | 200 | Paginated envelope `{data:[],total:0,page:1,pageSize:25}` |
| `/payroll/salary-components` (US-PAY-001) | Tenant Admin | GET | ✅ PASS | 200 | Empty list, paginated envelope |
| `/payroll/runs` (US-PAY-003) | Tenant Admin | GET | ✅ PASS | 200 | `data:[]` — bare list (no pagination wrapper) |
| `/payroll/statutory-rules` (US-PAY) | Tenant Admin | GET | ✅ PASS | 200 | Empty, paginated envelope |
| `/payroll/statutory-rules/fiscal-years` | Tenant Admin | GET | ✅ PASS | 200 | `data:[]` bare list |
| `/payroll/reports` (US-PAY-004) | Tenant Admin | GET | ✅ PASS | 200 | Returns report catalog (PayrollSummary, …) |
| `/payroll/adjustments` (US-PAY) | Tenant Admin | GET | ✅ PASS | 200 | `{items:[],totalCount:0,...}` — third envelope shape |
| `/payroll/reports/bank-advice/preview` | Tenant Admin | GET | ✅ PASS | 404 | Clean domain 404: "No finalized payroll runs exist for this tenant." (correct — no run yet) |
| `/payroll/reports/bank-advice/full` (sensitive) | Tenant Admin | GET | ✅ PASS | 404 | Same clean 404; admin holds `Payroll.ViewSensitive` so gate passed, blocked by domain state not authz |
| `/payroll/my-payslips` (US-PAY-006 self-service) | Employee | GET | ✅ PASS | 403 | Permission gate passed; **403 `no_employee_linked`** — deliberate per controller (AC-4). Seeded employee has no linked Core HR record, so empty-list 200 path unreachable with this fixture |
| `/payroll/salary-components` | Employee | GET | ✅ PASS | 403 | AuthZ deny (no `Payroll.Configure`) |
| `/payroll/salary-structures` | Employee | GET | ✅ PASS | 403 | AuthZ deny |
| `/payroll/runs` | Employee | GET | ✅ PASS | 403 | AuthZ deny |
| `/payroll/reports` | Employee | GET | ✅ PASS | 403 | AuthZ deny |
| `/payroll/statutory-rules` | Employee | GET | ✅ PASS | 403 | AuthZ deny |
| `/payroll/reports/bank-advice/full` | Employee | GET | ✅ PASS | 403 | Sensitive gate denies (no `Payroll.ViewSensitive`) |
| `/payroll/salary-components` | HR Officer | GET | ✅ PASS | 403 | AuthZ deny — HR Officer has no payroll perms (matches seed, contradicts designed TCs) |
| `/payroll/salary-structures` | HR Officer | GET | ✅ PASS | 403 | AuthZ deny |
| `/payroll/runs` | HR Officer | GET | ✅ PASS | 403 | AuthZ deny |
| `/payroll/reports` | HR Officer | GET | ✅ PASS | 403 | AuthZ deny |
| `/payroll/statutory-rules` | HR Officer | GET | ✅ PASS | 403 | AuthZ deny |
| `/payroll/my-payslips` | HR Officer | GET | ✅ PASS | 403 | No `Payroll.View.Own` |
| `/payroll/my-payslips` | Manager | GET | ✅ PASS | 403 | No `Payroll.View.Own` |
| `/payroll/salary-components` | (unauth) | GET | ✅ PASS | 401 | No bearer → 401 (vs 403 with bad perms — correct distinction) |
| `/payroll/salary-components` (TC-PAY-001-01, safe write) | Tenant Admin | POST | ✅ PASS | 201 | Created `code=QA133514` with `id`, `typeName/calculationMethodName`, `createdAt` audit field; list `total` went 0→1 (tenant-stamped) |
| `/payroll/salary-components` (authZ negative on write) | HR Officer | POST | ✅ PASS | 403 | Write gate denies HR Officer |

## Findings (real defects / contract issues)

- **No 500s observed.** Every endpoint returned a deliberate status (200 / 201 / 401 / 403 / 404). Backend
  payroll API is stable at the smoke layer.
- **No FAILs.** All verdicts PASS against *actual* intent. (Several would be FAIL only if scored against the
  task's stated assumption that HR Officer can read payroll — that assumption is incorrect; see mismatch flag.)
- **CONTRACT (minor) — inconsistent list envelopes inside `ApiResponse<T>`.** Three shapes coexist:
  `salary-structures/-components/statutory-rules` → `{data,total,page,pageSize}`; `runs` &
  `fiscal-years` → bare `data:[]`; `adjustments` → `{items,totalCount,page,pageSize}`. Not a bug, but
  FE consumers must special-case each. Worth a normalization story.
- **AUTHZ semantics — `my-payslips` returns 403 (not 200 empty) for an unlinked user.** The Employee
  *passed* the `Payroll.View.Own` permission check but has no linked Core HR employee, yielding a
  deliberate 403 `no_employee_linked` (documented AC-4 behavior in `MyPayslipsController.cs`). This is
  correct, but it means the self-service "empty list = PASS" path could **not** be exercised with the
  seeded `employee@acme.test` fixture. To truly verify the happy 200 path, seed an employee record linked
  to that user (and, for non-empty, a finalized run + payslip).
- **`Payroll.ViewSensitive` gate confirmed working.** bank-advice **full** is reachable only with
  `ViewSensitive` (admin: passed gate, blocked by domain 404; employee: 403). Distinct from `Payroll.Export`.

## Tenant isolation (feasibility note)

Cross-tenant TCs (TC-PAY-ISO-001..010) require a **second tenant** (e.g. `globex`) with its own payroll
data + a user. Only `acme` (+ platform/system) is provisioned in this environment, so cross-tenant read
leakage could **not** be executed — BLOCKED on a second seeded tenant. Indirect evidence of isolation
working: the Admin POST created a component that appeared in acme's list with `total` 0→1 (tenant-scoped
read filter returning only acme rows). The designed ISO TCs also note RLS is deferred — platform enforces
isolation via EF Core global query filters + `TenantInterceptor` (assert 404/empty, not 403, on cross-tenant
ID injection once a second tenant exists).

## Not executed (by instruction — side-effecting / irreversible)

Payroll **run** creation, **submit-for-approval**, **approve/reject/return/finalize**, payslip
**generate/regenerate**, and salary **assignment** (needs linked employee + active structure). These are the
core US-PAY-003/005/006 happy paths and remain BLOCKED for this API smoke; they need a controlled,
recorded write session with teardown.
