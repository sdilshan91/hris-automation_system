---
title: QA Status Sheet & Testing Frameworks
created: 2026-06-23
stack: native localhost (API :5000, FE :4200) — UP; Docker — DOWN
sources: STATUS/BUG-STATUS, EXECUTION-LOG-2026-06-19, admin-console pilot 2026-06-23, repo scan
---

# QA Status Sheet — HRM Automation System (2026-06-23)

## 1. Testing frameworks & tools used

Two distinct layers: **automated suites committed in the repo** and **agent-driven manual QA execution** used for the baseline/pilot runs.

### A. Automated test suites (in repo)

| Layer | Framework / tools | Location | Tests | Run command |
|---|---|---|---:|---|
| Backend unit + integration | **xUnit 2.9** + **FluentAssertions 8** + **Testcontainers.PostgreSQL 4** (real Postgres) + `WebApplicationFactory` for HTTP integration | `src/backend/HRM.Tests` | **2,333** `[Fact]`/`[Theory]` (11 files = HTTP integration, `[Collection("HttpApi")]`) | `dotnet test HRM.sln` |
| Frontend unit/component | **Karma 6.4** + **Jasmine 5.4** (headful Chrome), karma-coverage | `src/frontend/src/**/*.spec.ts` (296 files) | **3,624** `it()` | `npm test` |
| Frontend end-to-end | **Playwright 1.50** (`@playwright/test`) | `src/frontend/e2e` (3 specs) | **7** `test()` | `npm run e2e` |

Config: `karma.conf.js`, `playwright.config.ts`. Backend integration needs **Docker** (Testcontainers) — currently **down**, so those 11 files can't run locally right now.

### B. Agent-driven QA execution tooling (baseline + pilot runs)

| Layer | Tooling | Used for |
|---|---|---|
| API-layer | **curl + real JWT** (scripted HTTP, SystemAdmin holds all perms) | 2026-06-19 baseline (~262 checks) + admin-console pilot (per-US smoke) |
| UI-layer | **Playwright MCP** (real Chrome, via `@browser-debugger`) | UI flows, console/network inspection, auth/tenant diagnosis |

> IEEE alignment: user stories = IEEE 830, test cases = IEEE 829. Designed TCs live in `test-cases/` as markdown, independent of the automated suites above.

---

## 2. Designed test-case coverage (1,941 TCs)

| Test type | TCs | % |
|---|---:|---:|
| Negative | 940 | 48.4% |
| Security | 800 | 41.2% |
| Happy path | 601 | 31.0% |
| Boundary | 507 | 26.1% |
| Multi-tenant isolation | 392 | 20.2% |
| Performance | 144 | 7.4% |
| Cross-browser | 105 | 5.4% |
| Accessibility | 91 | 4.7% |

Untagged TCs: **32 → 18 remaining** (14 admin-console fixed in the pilot; performance/attendance/recruitment/core-hr still pending). Non-functional (perf/a11y/x-browser) is the structural gap.

---

## 3. Execution status

| Run | Date | Method | Scope | Result |
|---|---|---|---|---|
| QA baseline | 2026-06-19 | curl+JWT / Playwright MCP | ~262 critical+smoke checks, 11 modules | Backend ~245/262 PASS, 0 isolation breaches; 6 defects |
| Admin-console pilot | 2026-06-23 | curl+JWT per-US smoke | US-ADM-001…010 | **10/10 US PASS**, 0 5xx |
| Backend xUnit (2,333) | — | dotnet test | full suite | **Not freshly run** (integration needs Docker, down) |
| FE Karma (3,624) | — | ng test | full suite | **Not freshly run** this session |
| Playwright E2E (7) | — | npm run e2e | smoke | **Not freshly run** this session |

**Formal designed-TC execution: ~0%** — the 1,941 markdown TCs have no recorded per-TC verdict; execution to date is the ~262-check smoke subset + the admin-console pilot.

---

## 4. Defect status (from BUG-STATUS.md)

| Severity | Open | Fixed | Verified | Total |
|---|---:|---:|---:|---:|
| CRIT | 0 | 0 | 2 | 2 |
| HIGH | 1 | 0 | 4 | 5 |
| MED | 0 | 2 | 0 | 2 |
| LOW | 1 | 0 | 0 | 1 |
| **Total** | **2** | **2** | **6** | **10** |

Open: **BUG-5** (no E2E harness — partially addressed; Playwright e2e now exists but only 7 tests, CI wiring TODO), **BUG-8** (JWT tenant claim not cross-checked — deferred security task). Plus 4 contract nits, 5 TC/permission-drift items, 2 coverage gaps. New this session: **CT-ADM-1** (impersonation/targets 404→should be 400 on missing param).

---

## 5. Module status snapshot

| Module | Designed TCs | Baseline exec | Pilot (US-wise + tag + exec) |
|---|---:|---|---|
| admin-console | 217 | 28 checks PASS | ✅ DONE — 10/10 US PASS, 0 untagged |
| core-hr | 372 | 26 checks PASS | ⬜ pending (next — weakest boundary cov) |
| leave-management | 303 | 27 checks PASS | ⬜ pending |
| payroll | 192 | 25 checks PASS | ⬜ pending |
| performance | 183 | 22 checks PASS | ⬜ pending (8 untagged TCs) |
| attendance | 154 | 29 (3×500 fixed) | ⬜ pending (5 untagged TCs) |
| recruitment | 149 | 24 checks PASS | ⬜ pending (4 untagged TCs) |
| authentication | 116 | ~5 + UI login | ⬜ pending |
| onboarding | 95 | 24 checks PASS | ⬜ pending |
| notifications | 80 | 28 checks PASS | ⬜ pending |
| reports | 80 | 24 (1×500 fixed) | ⬜ pending |
| **TOTAL** | **1,941** | **~262 checks** | **1 / 11 modules** |

---

## 6. Honest headline
- **Backend is solid** at the API layer (smoke); tenant isolation holds; authz correct.
- **Automated suite inventory is large** (2,333 backend + 3,624 FE unit) but its *current* pass-rate is unverified this session, and backend integration is blocked on Docker being down.
- **Real gaps:** near-zero non-functional design coverage (perf/a11y/x-browser); ~0% formal per-TC execution; thin E2E (7 tests); 2 open defects deferred by decision.
