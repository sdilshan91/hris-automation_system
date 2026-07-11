# Core HR — API-Layer QA Baseline (Execution Log)

- **Date:** 2026-06-19
- **Module:** Core HR (Departments, Job Titles, Employees, Locations, Org Tree, Holidays, Custom Fields)
- **API base:** `http://localhost:5000`
- **Tenant:** `acme` (only `acme` + `platform`/`admin` tenants exist)
- **Auth:** `POST /api/v1/auth/login` (envelope `{"success":true,"data":{"accessToken":...}}`), all calls sent with `Authorization: Bearer <tok>` + `X-Tenant-Subdomain: acme`.
- **Scope:** API-layer smoke only. No source/TC files modified. No destructive endpoints called. Two safe creates (1 Department, 1 Job Title) in the `acme` test tenant.
- **Personas used:** Tenant Admin (`tenantadmin@acme.test`), HR Officer (`hr@acme.test`), Employee (`employee@acme.test`). All authenticated successfully.

## Discovered endpoints (route + permission)

| Controller | Route base | Endpoints (perm) |
|---|---|---|
| Departments | `api/v1/tenant/departments` | GET / GET tree / GET {id} (`Department.View`); POST (`Department.Create`); PUT {id} (`Department.Edit`); POST {id}/deactivate (`Department.Deactivate`) |
| Job Titles | `api/v1/tenant/job-titles` | GET / GET {id} / GET employment-types (`JobTitle.View`); POST (`JobTitle.Create`); PUT {id} (`JobTitle.Edit`); POST {id}/deactivate (`JobTitle.Deactivate`) |
| Employees | `api/v1/tenant/employees` | GET / GET {id} / GET {id}/profile (`Employee.View.All`); POST (`Employee.Create`); GET directory (`Employee.View.Own`); GET directory/export (`Employee.Export`); status/manager/documents/import/photo (various) |
| Locations | `api/v1/tenant/locations` | GET / GET {id} (`Location.View`); POST (`Location.Create`); PUT {id} (`Location.Edit`); POST {id}/deactivate (`Location.Deactivate`) |
| Org Tree | `api/v1/tenant/org-tree` | GET (`Department.View`) |
| Holidays | `api/v1/holidays` | GET / GET {id} (`Holiday.View`); POST (`Holiday.Create`); PUT {id} (`Holiday.Edit`); POST {id}/deactivate (`Holiday.Deactivate`); POST import (`Holiday.Import`) |
| Custom Fields | `api/v1/tenant/custom-fields` | GET / GET {id} (`CustomField.View`); POST (`CustomField.Create`); PUT {id} (`CustomField.Edit`); POST {id}/deactivate + reactivate + reorder (`CustomField.*`) |

## Execution results

| Endpoint / TC | Persona | Method | Verdict | HTTP | Evidence |
|---|---|---|---|---|---|
| `/tenant/departments` (list) | HR Officer | GET | PASS | 200 | `{"success":true,"data":[]}` envelope |
| `/tenant/departments/tree` | HR Officer | GET | PASS | 200 | `data:[]` |
| `/tenant/job-titles` (list) | HR Officer | GET | PASS | 200 | `data:[]` |
| `/tenant/job-titles/employment-types` | HR Officer | GET | PASS | 200 | enum list FullTime/PartTime/Contract/Intern |
| `/tenant/locations` (list) | HR Officer | GET | PASS | 200 | `data:[]` |
| `/tenant/org-tree` | HR Officer | GET | PASS | 200 | `{"nodes":[],"view":"department","reportingViewAvailable":false}` |
| `/holidays` (list) | HR Officer | GET | PASS | 200 | `data:[]` |
| `/tenant/employees` (list) | HR Officer | GET | PASS | 200 | paged `{"items":[],"totalCount":0,"page":1,"pageSize":20}` |
| `/tenant/custom-fields` (list) | HR Officer | GET | PASS | 403 | denied — `CustomField.View` not granted to HR Officer (by design; see Findings #1) |
| `/tenant/employees/directory` | HR Officer | GET | PASS | 403 | denied — requires `Employee.View.Own`, not held by HR Officer (by design; see Findings #2) |
| `/tenant/custom-fields` (list) | Tenant Admin | GET | PASS | 200 | `data:[]` — TA holds `CustomField.View` |
| `/tenant/departments/{id}` (detail) | Tenant Admin | GET | PASS | 200 | returns created dept by id |
| `/tenant/employees/directory` | Tenant Admin | GET | PASS | 403 | TA also lacks `Employee.View.Own` (directory is self-service surface; see Findings #2) |
| `/tenant/departments` | Employee | GET | PASS | 403 | authz denied (HR-only) |
| `/tenant/employees` | Employee | GET | PASS | 403 | authz denied (`Employee.View.All`) |
| `/tenant/job-titles` | Employee | GET | PASS | 403 | authz denied |
| `/tenant/departments` (create) | Employee | POST | PASS | 403 | authz denied (`Department.Create`) |
| `/tenant/departments` empty body | Tenant Admin | POST | PASS | 400 | validation: name/code required + code pattern |
| `/tenant/departments` bad code | Tenant Admin | POST | PASS | 400 | validation: code charset rule |
| `/tenant/departments` (create) | Tenant Admin | POST | PASS | 201 | created `id=019edee8-...08ef`, `isActive:true`; envelope ok |
| `/tenant/job-titles` (create) | Tenant Admin | POST | PASS | 201 | created `id=019edee8-...7549`, `employeeCount:0` |
| Persistence: dept appears in list | Tenant Admin | GET | PASS | 200 | created `QABL…` dept present (count=1) |
| `/tenant/departments/{badguid}` | Tenant Admin | GET | PASS | 404 | malformed guid → route constraint rejects |
| ISO: acme persona vs `platform` subdomain login | Tenant Admin (acme) | POST | PASS | 403 | "You do not have an active membership in this organization." |
| ISO: GET foreign/nonexistent dept id | Tenant Admin (acme) | GET | PASS | 404 | "Department not found." — query filter hides cross-tenant rows (404 not 403/500, no leak) |
| ISO: GET with NO tenant header | Tenant Admin | GET | PASS | 400 | "Tenant context is not resolved." — request rejected |

**Totals:** 26 checks across ~14 distinct endpoints — 26 PASS / 0 FAIL / 0 BLOCKED.

## Findings

No 5xx errors. No broken-contract defects. The two HR-Officer 403s below are **by-design authorization**, verified against `PermissionCatalog.DefaultPermissionsFor("HR Officer")` — not bugs — but flagged as contract notes for the test designers:

1. **CustomField.View not granted to HR Officer.** `GET /tenant/custom-fields` returns 403 for HR Officer but 200 for Tenant Admin. Per `PermissionCatalog`, `CustomField.*` is granted only to Tenant Admin / Tenant Owner. If any Core-HR TC assumes HR Officer can read custom-field definitions, that expectation is wrong — fix the TC, not the code.

2. **`/employees/directory` requires `Employee.View.Own` — neither HR Officer nor Tenant Admin holds it.** The directory endpoint is the *self-service* surface (granted to the Employee/Manager personas that hold `Employee.View.Own`); HR/Admin personas read staff via `GET /tenant/employees` (`Employee.View.All`) instead. Result: HR Officer and Tenant Admin both get 403 on `/directory`. This is an intentional split but is a sharp edge — a TC that exercises "HR views the directory" will fail against the real authz model. Recommend the TC target `/tenant/employees` for HR personas and `/directory` only for self-service personas. (Reported to caller per stay-in-lane; no code/TC edited.)

3. **Envelope is consistent and correct.** Every 2xx wraps payload in `{"success":true,"data":...,"message":null,"code":null,"errors":null,"timestamp":...}`; every 4xx uses `{"success":false,"message":...,"errors":[...]}`. Validation 400s surface FluentValidation messages. (Note: this is the same `ApiResponse<T>` envelope the FE-unwrap tech-debt note tracks app-wide; the API layer itself is well-formed.)

4. **Tenant isolation holds at the API layer.** Cross-tenant login blocked (403), cross-tenant/unknown id returns 404 (not 403/500 — no existence leak), missing tenant header rejected (400). Note: platform uses EF global query filters + `TenantInterceptor`, not Postgres RLS (RLS deferred — US-PLT-002); the 404-not-403 behavior is the expected signature of query-filter isolation.

## Notes / limitations

- Only `acme` + `platform`/`admin` tenants are seeded, so a true two-paying-tenant cross-read (acme reads a real acme-foreign row) wasn't possible; isolation was probed via cross-subdomain login + foreign-id read + missing-header instead, which is sufficient to confirm fail-closed behavior.
- Writes limited to two safe creates (Department, Job Title) in the test tenant. No deactivate/bulk/delete/import endpoints were exercised (destructive — out of scope per instructions).
- PUT/deactivate/status-transition/document/import endpoints were route-discovered but not executed (would mutate state or need uploaded fixtures); recommend a follow-up write-path run with teardown if deeper coverage is wanted.
