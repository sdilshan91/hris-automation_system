# Recruitment — API-Layer QA Baseline (Execution Log)

- **Date:** 2026-06-19
- **Environment:** Running backend at `http://localhost:5000`, tenant subdomain `acme`
- **Method:** curl against live API. No source or TC files modified. No browser. No destructive ops (no applicant delete / offer rescind).
- **Auth:** `POST /api/v1/auth/login` (envelope `{success, data.accessToken}`) succeeded for all four personas: Tenant Admin, HR Officer, Manager, Employee (password `Admin@123!`, subdomain `acme`). Recruiter role is seeded with no user — not exercised.

## Scope reference (routes confirmed by grep)

| Controller | Base route | AuthZ |
|---|---|---|
| VacanciesController | `api/v1/recruitment/vacancies` | View (read) / Manage (write) |
| ApplicantsController | `api/v1/recruitment/vacancies/{vacancyId}/applicants` | View / Manage |
| ApplicantPipelineController | `api/v1/recruitment` (pipeline/move-stage) | View / Manage |
| ApplicantConversionController | `api/v1/recruitment` (convert) | Manage + Employee.Create |
| InterviewsController | `api/v1/recruitment` (interviews/scorecards) | View / Manage |
| OffersController | `api/v1/recruitment` (offers) | View / Manage |
| CareersController | `api/v1/careers/vacancies` | **AllowAnonymous** |
| PortalController | `api/v1/careers/portal` | **AllowAnonymous** (magic-link via `X-Portal-Token`) |
| RecruitmentDashboardController | `api/v1/recruitment/dashboard` | View |

> **TC↔code permission deviation (pre-existing, not a defect):** TCs (e.g. TC-REC-001-01) reference permissions `Recruitment.Create.All` / `Recruitment.Read.All`. The implemented platform uses **`Recruitment.View` / `Recruitment.Manage`**. Test intent (recruiter-class can manage, employee cannot) is honored; the named permission strings differ. Reported to caller, not "fixed."

## Results

| Endpoint / TC | Persona | Method | Verdict | HTTP | Evidence |
|---|---|---|---|---|---|
| `/recruitment/vacancies` (list) | Tenant Admin | GET | PASS | 200 | `{success:true,data:{data:[],total:0,...}}` paged envelope |
| `/recruitment/interviews` (list) | Tenant Admin | GET | PASS | 200 | `{success:true,data:[]}` |
| `/recruitment/dashboard` | Tenant Admin | GET | PASS | 200 | KPI object (openVacancies/totalApplicants/hires/avgTimeToHire...) |
| `/recruitment/scorecard-criteria` | Tenant Admin | GET | PASS | 200 | seeded criteria list (technical_skills, communication, ...) |
| `/recruitment/vacancies` (list) | HR Officer | GET | PASS | 200 | paged envelope, empty |
| `/recruitment/dashboard` | HR Officer | GET | PASS | 200 | KPI object |
| **`/recruitment/vacancies` (create)** — TC-REC-001-01 | Tenant Admin | POST | **PASS** | 201 | `id` UUID, `referenceNumber:"VAC-2026-0001"`, `status:"Draft"` |
| `/recruitment/vacancies/{id}` (read-back) | Tenant Admin | GET | PASS | 200 | same id + `VAC-2026-0001`, status Draft |
| `/recruitment/vacancies/{id}/applicants` | Tenant Admin | GET | PASS | 200 | paged empty list |
| `/recruitment/vacancies/{id}/pipeline` | Tenant Admin | GET | PASS | 200 | stages array (Applied/Screening/...) scoped to vacancy |
| `/careers/vacancies` (open list) — TC-REC-001 FR-4 | Anonymous | GET | PASS | 200 | `{success:true,data:[]}` (no open public vacancies seeded) |
| `/careers/vacancies/{slug}` (bad slug) | Anonymous | GET | PASS | 404 | `"Vacancy not found."` — correct not-found, no leak |
| `/careers/portal/dashboard` (no token) | Anonymous | GET | PASS | 401 | `code:"token_required"` |
| `/careers/portal/dashboard` (bad token) | Anonymous | GET | PASS | 401 | `code:"invalid_token"` |
| `/careers/portal/request-link` — TC-REC-008 FR-8 | Anonymous | POST | PASS | 200 | anti-enumeration generic: "If an application exists..." |
| `/careers/vacancies` (NO subdomain) | Anonymous | GET | PASS | 404 | `code:"careers_unavailable"` — fail-closed when tenant unresolved |
| `/recruitment/vacancies` (list) | Employee | GET | PASS (negative) | 403 | authZ denies View |
| `/recruitment/dashboard` | Employee | GET | PASS (negative) | 403 | denied |
| `/recruitment/vacancies` (create) | Employee | POST | PASS (negative) | 403 | denied Manage |
| `/recruitment/vacancies` (list) | Manager | GET | PASS (negative) | 403 | Manager holds no Recruitment perm |
| `/recruitment/vacancies` (create) | Manager | POST | PASS (negative) | 403 | denied |
| `/recruitment/vacancies` (no token) | none | GET | PASS (negative) | 401 | auth required |
| Cross-tenant GET acme vacancy under `globex` subdomain | Tenant Admin (acme tok) | GET | PASS (isolation) | 404 | unresolved tenant → "Workspace not found" before controller |
| Offers under nonexistent applicant (all-zero GUID) | Tenant Admin | GET | INFO | 200 | returns empty list, no 404 (see Finding F2) |
| `/recruitment/vacancies` under `admin` subdomain w/ acme token | Tenant Admin (acme tok) | GET | INFO | 200 | empty list; no acme data leaked, but token tenant claim not validated vs resolved subdomain (see Finding F1) |

## Findings

No HTTP 500s, no broken contracts, and **no anonymous endpoint leaked cross-tenant data.** Public careers/portal endpoints correctly fail-closed (404 `careers_unavailable` when the subdomain resolves to no tenant; 401 `token_required`/`invalid_token` on the portal). AuthZ is enforced as intended (Employee + Manager → 403; no token → 401). The happy-path create produced a correct tenant-scoped `VAC-2026-0001` Draft and read back cleanly. Tenant isolation held: an acme vacancy was unreachable under a non-acme subdomain.

Two non-blocking observations (neither a leak, neither blocks the baseline):

- **F1 — Token tenant claim not cross-checked against resolved subdomain (Low / hardening).** An acme-issued bearer token presented under the `admin` subdomain returned `200` with an **empty** list rather than a `401/403`. No acme data was exposed (the system/admin context is empty and EF query filters scope reads), so this is **not** a data leak. But the request succeeded with a token whose tenant differs from the resolved context — relying solely on the query filter for safety. Recommend the auth layer reject a mismatch between the JWT `tenant_id` claim and the subdomain-resolved tenant (defense-in-depth). Confidence: 80% this is by-design today; flag for review, do not block.
- **F2 — Offers/sub-resource reads return `200 []` for a nonexistent parent applicant (Low / contract nit).** `GET /recruitment/applicants/{all-zero-guid}/offers` returned `200` empty instead of `404`. Harmless (still tenant-scoped, no leak) but a stricter contract would 404 an unknown applicant. Cosmetic.

**Deviation reported (not a defect):** TC permission strings (`Recruitment.Create.All`/`Read.All`) do not match the implemented `Recruitment.View`/`Manage`. The TCs should be reconciled to the platform's actual permission names; QA agent did not edit TC files per the execution contract.

**Cleanup note:** one Draft vacancy `VAC-2026-0001` (id `019edef0-8df6-74f9-af47-53a694058b2a`) was created in acme during the happy-path test and left in place (no destructive ops performed).
