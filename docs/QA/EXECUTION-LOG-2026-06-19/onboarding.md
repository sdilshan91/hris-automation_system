# API-Layer QA Baseline — Onboarding / Offboarding Module

- **Date:** 2026-06-19
- **Environment:** Running backend at `http://localhost:5000`, tenant `acme` (only seeded tenant)
- **Auth:** `POST /api/v1/auth/login` + `X-Tenant-Subdomain: acme`, password `Admin@123!`
- **Personas:** Tenant Admin `tenantadmin@acme.test`, HR Officer `hr@acme.test`, Manager `manager@acme.test`, Employee `employee@acme.test` (linked to Core HR `EMP-001`)
- **Scope:** Non-destructive API smoke against the 5 onboarding controllers. One safe create (template). No offboarding initiation / termination / clearance.

## Controllers / Routes Under Test (from source)

| Controller | Base route | Endpoints sampled |
|---|---|---|
| OnboardingTemplatesController | `api/v1/onboarding/templates` | GET (list), GET `{id}`, POST (create) — `Onboarding.View` / `Onboarding.Manage` |
| OnboardingChecklistsController | `api/v1/onboarding/checklists` | GET `applicable-templates`, GET `me`, GET `me/progress` (self-service) |
| OnboardingAssetsController | `api/v1/onboarding/assets` | GET (list), GET `available`, GET `me` (self-service), POST `issue` |
| OffboardingController | `api/v1/offboarding` | GET `?employeeId=` (lookup, NOT a dashboard list), GET `{id}` |
| ExitInterviewsController | `api/v1/exit-interviews` | GET `template`, GET `?offboardingId=` (lookup), GET `analytics` |

## Results

| Endpoint / TC | Persona | Method | Verdict | HTTP | Evidence |
|---|---|---|---|---|---|
| `/onboarding/templates` (list) | HR Officer | GET | PASS | 200 | `{data:{data:[],total:0,...}}` paged envelope |
| `/onboarding/templates` (list) | Tenant Admin | GET | PASS | 200 | `total:0` empty list |
| `/onboarding/assets` (list) | HR Officer | GET | PASS | 200 | `data:[]` |
| `/onboarding/assets/available` | HR Officer | GET | PASS | 200 | `data:[]` |
| `/exit-interviews/analytics` | HR Officer | GET | PASS | 200 | `{reasonDistribution:[],averageRatingsPerCategory:[],trend:[]}` anonymized aggregate |
| `/exit-interviews/analytics` | Tenant Admin | GET | PASS | 200 | same aggregate shape |
| `/exit-interviews/template` | HR Officer | GET | PASS | 200 | seeded template with categories + questions |
| `/offboarding?employeeId=` (lookup) | HR Officer | GET | PASS | 404 | `offboarding_not_found` — correct: lookup-by-employee with no instance, **not** a dashboard list (see Finding 1) |
| `/offboarding?employeeId=` (lookup) | Tenant Admin | GET | PASS | 404 | `offboarding_not_found` (same) |
| `/exit-interviews?offboardingId=` (lookup) | HR Officer | GET | PASS | 404 | `exit_interview_not_found` — correct: lookup-by-offboarding, **not** a list (see Finding 1) |
| `/onboarding/assets/me` (my-assets) | Employee EMP-001 | GET | PASS | 200 | `data:[]` self-service works |
| `/onboarding/checklists/me` (my-checklist) | Employee EMP-001 | GET | BLOCKED | 404 | `checklist_not_found` — graceful, but no onboarding checklist assigned to EMP-001 in seed (dependency missing, not a defect) |
| `/onboarding/checklists/me/progress` | Employee EMP-001 | GET | BLOCKED | 404 | `checklist_not_found` (same missing dependency) |
| `/onboarding/checklists/applicable-templates` | HR Officer | GET | PASS | 404 | `employee_not_found` — requires `?employeeId=`; clean validation 404, no 500 |
| `/onboarding/templates` (create) — TC-ONB-001-01 happy path | HR Officer | POST | PASS | 201 | created id `019edef1-…`, 2 tasks persisted, `responsibleRole` serialized as name `"HR"`, `isMandatory` honored |
| `/onboarding/templates/{id}` (GET back) | HR Officer | GET | PASS | 200 | full template returned, `total` now 1 |
| `/onboarding/templates` (authZ) | Employee | GET | PASS | 403 | `Onboarding.View` enforced |
| `/onboarding/assets` (authZ) | Employee | GET | PASS | 403 | `Onboarding.Manage` enforced |
| `/offboarding?employeeId=` (authZ) | Employee | GET | PASS | 403 | manage-gated, denied |
| `/exit-interviews/analytics` (authZ) | Employee | GET | PASS | 403 | manage-gated, denied |
| `/onboarding/assets/issue` (authZ) | Employee | POST | PASS | 403 | manage-gated, denied |
| `/onboarding/templates` (no token) | — | GET | PASS | 401 | unauthenticated rejected |
| Tenant isolation — wrong subdomain `globex` + acme token/ID | HR Officer | GET | PASS | 404 | resolution rejects unknown tenant ("workspace does not exist") **before** controller — no data leak |
| Tenant isolation — unknown GUID under acme | HR Officer | GET | PASS | 404 | `Template not found` (EF query-filter read isolation; 404-not-403 per platform model) — no leakage |

**Tally:** 24 checks — PASS 22, BLOCKED 2, FAIL 0.

## Findings

1. **Naming/expectation mismatch (not a defect): no "offboarding dashboard" or "exit-interview list" endpoint exists.**
   The task brief expected `GET /offboarding` and `GET /exit-interviews` to be dashboard/list endpoints returning 2xx. In source they are **lookup-by-query-param** actions: `GET /offboarding?employeeId={id}` → the single offboarding instance for that employee (404 if none), and `GET /exit-interviews?offboardingId={id}` → the single exit interview for that offboarding (404 if none). With no/empty query param they correctly 404 (`offboarding_not_found` / `exit_interview_not_found`). Aggregate visibility for HR is provided by `GET /exit-interviews/analytics` (returns 200). If a paged HR-facing offboarding list is a requirement, **it is not implemented** — flag to caller as a possible coverage gap in US-ONB offboarding stories, not an app bug.

2. **Self-service checklist is BLOCKED on missing seed data, not broken.**
   `GET /onboarding/checklists/me` and `/me/progress` return 404 `checklist_not_found` for Employee EMP-001 because no onboarding instance/checklist has been assigned to that employee in the seeded data. The endpoint, auth, and contract are correct (graceful 404, no 500). To turn these into a true PASS, a prerequisite is needed: assign EMP-001 an active onboarding checklist (which requires an onboarding-initiation flow / seed). Marked BLOCKED per the missing-dependency rule. `my-assets` (`/assets/me`) does return 200, so the self-service surface itself is wired.

3. **AuthZ is consistently enforced (no gaps found).** Employee persona received 403 on every `Onboarding.View` / `Onboarding.Manage`-gated endpoint (templates list, assets list/issue, offboarding lookup, exit analytics); unauthenticated request got 401. No over-permissive endpoint observed.

4. **Tenant isolation holds (light check).** Only `acme` is seeded, so a true cross-tenant A→B leak test wasn't possible. Substitute checks passed: (a) a valid acme token with an unknown tenant subdomain (`globex`) is rejected at `TenantResolutionMiddleware` ("This workspace does not exist") before reaching the controller; (b) an unknown GUID under valid acme context returns a clean 404 `Template not found` via EF query-filter read isolation (404-not-403, consistent with the platform's EF-filter model rather than Postgres RLS). No cross-tenant data surfaced.

5. **Contract sanity on create path is clean.** `POST /onboarding/templates` returned 201 with `CreatedAtAction` linking to `GetById`; enums serialize as names (`responsibleRole:"HR"`), `isMandatory`/`sortOrder` round-trip, and the list `total` reflected the insert. No 500s, no envelope inconsistencies observed across any endpoint hit.

## Test Artifact Left Behind

One template `"QA Smoke Template 2026-06-19"` (id `019edef1-0c3d-7208-ab9e-6cb11c807a36`) was created in tenant `acme` by the happy-path test. It is inert/non-destructive; delete if a clean template list is desired (no delete endpoint exercised — only activate/deactivate exist).
