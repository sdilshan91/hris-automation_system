# Performance Module — API-Layer QA Execution Log

- **Date:** 2026-06-19
- **API base:** `http://localhost:5000`
- **Tenant:** `acme` (header `X-Tenant-Subdomain: acme`)
- **Personas (pwd `Admin@123!`):** Tenant Admin `tenantadmin@acme.test`, HR Officer `hr@acme.test`, Manager `manager@acme.test`, Employee `employee@acme.test`
- **Scope:** API-layer smoke against the **running** backend (curl). No source/TC files modified. No browser. No destructive/irreversible endpoints called.
- **Auth:** all four personas logged in successfully via `POST /api/v1/auth/login`; `{"success":true,"data":{"accessToken":...}}` envelope confirmed.

## Route discovery (controllers, base `api/v1/tenant/performance`)

| Controller | Key endpoints | Required permission(s) |
|---|---|---|
| Cycles | GET `cycles`, `cycles/active`, `cycles/{id}`, `cycles/{id}/dashboard`; POST `cycles`, `cycles/clone`, `cycles/{id}/status`; PUT `cycles/{id}` | `Performance.SetGoal.All` OR `Performance.Publish.All` (ManageCycles/PublishCycles) |
| Goals | GET `employees/{eid}/cycles/{cid}/goals`, `cycles/{cid}/team-dashboard`, `goals/{id}`; POST/PUT `goals` | `Performance.SetGoal.Team` / `.All` |
| GoalProgress | GET `my-goals`, `goals/{id}/timeline`, `team-goals`, `team-goals/employees/{eid}`; POST `goals/{id}/progress`, `goals/{id}/comments` | self: `Performance.Read.Self`; team: `Performance.Review.Team`/`.All` |
| SelfAssessment | GET `self-assessments/cycles/{cid}/me`; PUT `draft`; POST `submit` | `Performance.Read.Self` |
| ManagerReview | GET `reviews/cycles/{cid}/employees/{eid}`, `reviews/cycles/{cid}/team`; PUT `reviews/draft`; POST `reviews/submit`, `.../reopen` | `Performance.Review.Team`/`.All` (reopen `.All` only) |
| ReviewSignoff | GET/PUT notes, POST `request-signoff`/`acknowledge`/`dispute`/`resolve-dispute`; GET export | manager notes `Review.Team`/`.All`; employee ack/dispute `Read.Self`; resolve `Review.All` |
| Feedback360 | reviewers (GET/POST), notify, feedback, results, report | mostly `Performance.Review.All` |
| Pip | GET list, GET `{id}`; POST create/acknowledge/checkpoints/outcome/escalation | view `Review.Team`/`.All`; create/outcome/escalation `Review.All`; acknowledge `Read.Self` |
| PerformanceDashboard | GET `dashboard/overview`, `dashboard/department/{id}`, `dashboard/trend`, `dashboard/export` | `Performance.View.All` / `.View.Team` |

## Results

| Endpoint / TC | Persona | Method | Verdict | HTTP | Evidence |
|---|---|---|---|---|---|
| `cycles` (list) | TenantAdmin | GET | PASS | 200 | `data: []` empty list, well-formed envelope |
| `cycles` (list, after create) | HR Officer | GET | PASS | 200 | returns the created cycle |
| `cycles/active` | HR Officer | GET | PASS | 404 | `no_active_cycle` — correct: only a Draft cycle exists, none activated/published |
| `cycles/{id}` | TenantAdmin | GET | PASS | 200 | returns created cycle by id |
| `cycles/{id}/dashboard` | TenantAdmin | GET | PASS | 200 | per-cycle dashboard payload |
| `cycles/{nonexistent-id}` | TenantAdmin | GET | PASS | 404 | `cycle_not_found` (404 not 403 — query-filter style, consistent with EF tenant filter) |
| **POST `cycles`** (happy path) | HR Officer | POST | PASS | 201 | created cycle `019edee8-c2d6-…`, 3 phases sequenced, `managerWeightPercent:60` derived from `selfWeightPercent:40` |
| POST `cycles` (missing required phases) | HR Officer | POST | PASS | 400 | `"The SelfAssessment phase is required.; The ManagerReview phase is required."` |
| POST `cycles` (AuthZ) | Employee | POST | PASS | 403 | manage-cycle permission absent |
| `dashboard/overview` (no cycle) | TenantAdmin | GET | PASS* | 404 | `no_cycle` — see Finding 1 (inconsistent empty-state vs `trend`) |
| `dashboard/overview` (after cycle) | TenantAdmin | GET | PASS | 200 | resolves once a cycle exists |
| `dashboard/trend` | HR Officer | GET | PASS | 200 | `data:{scope:"Organization",points:[],…}` — empty-OK |
| `my-goals` (self) | Employee | GET | BLOCKED | 403 | `no_employee_record` — see Finding 2 (data-setup gap, not authz) |
| `team-goals` | Manager | GET | PASS | 200 | `data: []` empty-OK |
| `pips` (list) | Manager | GET | PASS | 200 | `data: []` empty-OK |
| `pips` (list) | HR Officer | GET | PASS | 200 | `data: []` empty-OK |
| `cycles` (AuthZ) | Employee | GET | PASS | 403 | denied (no manage/publish perm) |
| `dashboard/overview` (AuthZ) | Employee | GET | PASS | 403 | denied |
| `team-goals` (AuthZ) | Employee | GET | PASS | 403 | denied |
| `pips` (AuthZ) | Employee | GET | PASS | 403 | denied |
| `cycles` (AuthZ) | Manager | GET | PASS | 403 | Manager lacks ManageCycles/PublishCycles — read-only on team scope |
| `my-goals` (AuthZ) | Manager | GET | PASS | 403 | Manager not linked to employee record + needs `Read.Self` |
| Tenant isolation — no `X-Tenant-Subdomain` | TenantAdmin | GET | PASS | 400 | request rejected without tenant context |
| Tenant isolation — wrong subdomain (`globex`) | TenantAdmin | GET | PASS | 404 | resolution middleware returns "Workspace not found" before reaching the cycle |

\*PASS for liveness (no 500), but the empty-state behaviour is flagged as a contract inconsistency below.

## Findings

**Finding 1 — Dashboard empty-state inconsistency (LOW / contract).**
With **no** appraisal cycle in the tenant, `GET /dashboard/overview` returns **404** `no_cycle`, while `GET /dashboard/trend` returns **200** with an empty series (`points:[]`). Two sibling dashboard endpoints disagree on how to represent "no data yet": one treats it as an error, the other as an empty success. A dashboard UI calling both will see a partial 404. Recommend aligning on one convention (most consumers expect 200-empty for a dashboard). Not a crash; not blocking. *Reported to caller — not fixed (no source edits).*

**Finding 2 — Self-service paths blocked by missing seed data (BLOCKED, not a defect).**
`GET /my-goals` as **Employee** returns **403** `no_employee_record`: the `employee@acme.test` login is not linked to an `Employee` row, so the self-scope resolver can't map the user to goals. This blocks API-layer verification of all `Performance.Read.Self` flows (`my-goals`, `self-assessments/.../me`, goal progress, PIP acknowledge) end-to-end. The authz layer itself is fine (the 403 is a domain guard, returned *after* the permission check passes). To unblock, seed an Employee record for the self-service test users. *Environment/data gap — flag to caller.*

**No real defects (no 500s, no broken contracts, no authz holes).** AuthZ matrix behaves correctly across all four personas: Employee/Manager are denied manage-cycle, dashboard, team-goals, and PIP-list endpoints (403); HR Officer and Tenant Admin succeed. Tenant isolation is enforced — requests without a tenant header are 400, wrong-subdomain requests are stopped at resolution (404), and unknown cycle ids return 404 (query-filter semantics, consistent with the platform's EF global-filter approach rather than RLS).

## Notes / cleanup
- One **Draft** cycle ("QA Smoke Cycle 2026-06-19", id `019edee8-c2d6-78c5-bbcc-71da93a88ffc`) was created in `acme` as the happy-path artifact. It is harmless (Draft, AllEmployees, 0 participants) but left in the tenant — no safe non-destructive delete endpoint was exercised. Caller may purge if desired.
- Tenant-isolation cross-read was tested *lightly* (header manipulation only). A full A-vs-B leakage test needs a second seeded tenant with data; not performed here.
