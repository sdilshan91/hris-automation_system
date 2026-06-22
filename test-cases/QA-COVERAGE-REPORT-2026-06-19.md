# QA Coverage & Completion Report — HRM SaaS Platform

**Date:** 2026-06-19
**Author:** QA Engineer (analysis-only; no test files modified)
**Scope:** All 11 test-case modules + the running automated suites (FE Karma, BE xUnit)
**Standards:** IEEE 829, ISO/IEC/IEEE 29119

---

## 1. Executive Summary

**The stakeholder's worry — "most test cases are completed happy-path only" — is REFUTED for the IEEE-829 markdown design, but the word "completed" is the real problem: 0% of the 1,941 designed test cases have been executed.**

Two facts must be held apart, because they tell opposite stories:

- **Test DESIGN coverage (the markdown specs) is thorough and NOT happy-path-skewed.** Across 1,941 TC files, **Negative-test** tags (940) *outnumber* **Happy-path** tags (601), and **Boundary** (507), **Security** (800), and **Multi-tenant isolation** (392) are all heavily represented. Sample-reading real TCs (e.g. `TC-AUTH-026` steps through 5 sequential failed-login attempts to a lockout; `TC-PAY-ISO-001` asserts 404-not-403 on cross-tenant ID injection plus a DB-level check) confirms the tags reflect genuine negative/edge steps, not checkbox theatre. On paper the suite is strong.

- **Test EXECUTION / AUTOMATION completion is effectively zero.** Every one of the 1,941 specs carries `status: draft` (1,900) or `status: blocked` (41) — **none are `pass`/`fail`/`executed`**. None of the markdown TCs are wired to a runner. The only things that actually RUN are **296 frontend Karma unit specs** and **201 backend xUnit test files** — and those are a *separate* artifact from the IEEE-829 design, skew toward verifying logic in isolation, and **mock the cross-layer seams** (auth, roles, router, HTTP) where the highest-risk defects live.

So the perception "happy-path only" is most likely formed by looking at **what runs** (the FE unit specs), not **what is designed**. The designed suite is broad; the *executed* suite is narrow and mock-heavy; and the bridge between them does not exist.

**Headline numbers:**

| Dimension | Value |
|---|---|
| Designed TC specs | 1,941 (11 modules) |
| Designed TCs executed | **0 (0%)** — all `draft`/`blocked` |
| Negative-tagged vs Happy-path-tagged | **940 vs 601** (design is negative-heavy) |
| Running automated tests | 296 FE Karma unit + 201 BE xUnit files |
| Markdown TCs automated end-to-end | **≈ 0%** |
| E2E / browser tests | **0** (no Playwright/Cypress config, no `e2e/` dir) |

---

## 2. Completion %, defined three ways

"Completion" is ambiguous and the three readings diverge sharply. Reporting only one is how a project convinces itself it is done when it is not.

**(a) Design / AC coverage — ~100% claimed.**
Per `test-cases/TRACEABILITY-MATRIX.md`, every module claims full acceptance-criterion coverage (e.g. Authentication **61/61 AC**, every per-story "Acceptance Criteria Coverage" row reads `100% PASS`). This measures *"is every requirement linked to at least one written test?"* — and by that measure the team is essentially complete. It says nothing about whether those tests pass.

**(b) Execution completion — 0%.**
`status` frontmatter across all 1,941 files: `draft` 1,900, `blocked` 41, **executed/passed/failed/approved 0**. No test-run records exist. By IEEE-829's own lifecycle (a test case is not "complete" until executed with a recorded verdict), **completion is 0%**.

**(c) Automation completion — ≈ 0% of the markdown TCs.**
None of the 1,941 specs are bound to an automated runner. The 296 FE Karma specs and 201 BE xUnit files exist and run, but they are independently authored unit tests, not automations of the IEEE-829 cases — there is no ID mapping (`TC-*` → spec) and no E2E layer to carry the integration/e2e-typed TCs (85 `e2e`, 123 `integration`).

> **Bottom line:** "100% designed" is true; "100% complete" is false. The honest status is *fully designed, zero executed.*

---

## 3. Coverage by module

All counts verified by direct grep over `test-cases/*/TC-*.md`. AC-coverage column from `TRACEABILITY-MATRIX.md` (Coverage `100% PASS` claimed everywhere).

| Module | TC count | AC coverage (matrix claim) | Has traceability section | Dominant type / notable tags |
|---|---|---|---|---|
| core-hr | 372 | 61/61 AC | Yes | functional 224, security 111 |
| leave-management | 303 | 52/52 AC | Yes | functional-heavy, strong ISO |
| admin-console | 217 | (per-story 100%) | Yes | **all 41 `blocked` TCs are here** |
| payroll | 192 | 63/63 AC | Yes | functional 72, security 71 (critical module) |
| performance | 183 | 44/44 AC | Yes | functional-heavy |
| attendance | 154 | 50/50 AC | Yes | functional + ISO |
| recruitment | 149 | 48/48 AC | Yes | mixed |
| authentication | 116 | **61/61 AC** | Yes (deepest) | functional 55, security 49 |
| onboarding | 95 | 39/39 AC | Yes | mixed |
| reports | 80 | 25/25 AC | Yes | functional |
| notifications | 80 | 20/20 AC | Yes | functional/integration |
| **Total** | **1,941** | — | 11/11 modules + root | — |

**Notes / discrepancies found:**
- All 11 modules have both a per-module `TEST-MATRIX.md` and a section in the root `TRACEABILITY-MATRIX.md` — traceability hygiene is good.
- Minor count drift vs. the matrix's own `**TOTAL**` rows (e.g. matrix shows leave "275" and performance "153" in older summary rows, vs. 303 and 183 TC files on disk today). The matrix totals lag the file counts — they were not all updated as TCs were added. Flag for matrix maintenance; the file counts are authoritative.
- **`CLAUDE.md` is stale:** it states "There is currently no backend test project." This is now wrong — `src/backend/HRM.Tests/HRM.Tests.csproj` exists with **201** `*Tests.cs` files. Recommend correcting that line.

---

## 4. Happy-path vs negative/edge analysis

### 4a. The designed specs are NOT happy-path-skewed

Checked `- [x]` category tags across all 1,941 files:

| Tag | Checked count |
|---|---|
| Negative test | **940** |
| Security test | 800 |
| Happy path | 601 |
| Boundary test | 507 |
| Multi-tenant isolation | 392 |
| Performance test | 144 |
| Cross-browser test | 105 |
| Accessibility test | 91 |

Negative+Boundary together (1,447) dwarf Happy-path (601). By `type`: functional 904, **security 622**, integration 123, performance 120, accessibility 87, e2e 85.

**Sample-read confirms the tags are real, not decorative** (8 TCs read across modules):
- `TC-AUTH-026` (security, US-AUTH-010): 5 sequential wrong-password steps → lockout + audit/notification assertions. Genuine negative flow.
- `TC-PAY-ISO-001` (security, US-PAY-001): cross-tenant list returns only own rows; fetch by other tenant's UUID → **404 (not 403, not 200)**; DB-level `WHERE tenant_id` check; correctly notes RLS is deferred to EF global query filters on this platform.
- `TC-RPT-001-02` (functional, US-RPT-001): titled an "alternative path" (department filter) — proves alternative flows are designed, not just the primary one.
- `TC-AUTH-010` (US-AUTH-004): forgot-password email flow with proper preconditions/test-data tables.

**Verdict on the design:** the IEEE-829 markdown is broad, negative-heavy, and substantive. The "happy-path only" claim does not hold against the specs.

### 4b. The 296 FE unit specs DO skew toward logic-in-isolation — this is the likely source of the perception

Spot-checked guard/service/interceptor specs:

| Spec | `it()` | negative-ish assertions | Reads as |
|---|---|---|---|
| `core/auth/auth.guard.spec.ts` | 4 | 4 | balanced (allow + deny) |
| `core/auth/auth.service.spec.ts` | 15 | 1 | **happy-path heavy** |
| `core/interceptors/error.interceptor.spec.ts` | 2 | 12 | error-focused (good) |
| `core/interceptors/api-envelope.interceptor.spec.ts` | 10 | 5 | mixed |
| `core/interceptors/tenant.interceptor.spec.ts` | 2 | 0 | happy-path only |

The guard spec *does* test denial — but **with mocked role strings it invents**: `roleGuard` is tested with `hasRole` faked to match `'Tenant Admin'`. It proves the guard's branching logic; it cannot prove the *role strings agree across layers*. That gap is realized as a live defect (Section 5). The service spec (`15 it / 1 negative`) is the kind of artifact a reviewer skims and concludes "happy-path only" — and for the *running* suite, that's a fair read.

---

## 5. The execution / automation gap (critical finding)

Three structural holes, in order of risk:

**(i) Zero E2E / integration-through-the-stack layer.** No Playwright/Cypress/Protractor in `src/frontend/package.json`; no `e2e/` directory; no config. The 85 `e2e`-typed and 123 `integration`-typed TCs have **no runner that could execute them**. Every test that actually runs is a unit test that mocks its neighbours.

**(ii) The 1,941 markdown TCs are not wired to anything.** They are documentation. There is no `TC-* → automated test` mapping, so the rich negative/ISO/boundary design in Section 4a contributes **zero** runtime protection today.

**(iii) Mocked seams let real cross-layer defects through a green suite — confirmed live example:**

The backend seeds the platform role as **`SystemAdmin`** (no space — `HRM.Infrastructure/Persistence/DbInitializer.cs:16`, `SystemAdminRoleName = "SystemAdmin"`), so the JWT carries `SystemAdmin`. But the frontend `roleGuard` on the admin-console routes checks for **`'System Admin'`** (with a space):

- `src/frontend/src/app/app.routes.ts:127` → `roleGuard(['System Admin'])`
- `:139` → `roleGuard(['System Admin', 'System Support'])`
- `:152` → `roleGuard(['System Admin'])`
- `tenant-monitoring-detail.component.ts:116` → `hasRole('System Admin')`

`'SystemAdmin' !== 'System Admin'`, so at runtime a real System Admin is **redirected to /forbidden** on the entire admin console. The unit suite stays green because:
- `auth.guard.spec.ts` fakes `hasRole` to match `'Tenant Admin'` — a string of its own choosing, never the seeded value.
- `auth.service.spec.ts:294` mints a token with `roles: ['System Admin']` — the **wrong (spaced) form**, matching the guard's bug rather than the seeder's truth.

Both layers were tested against the *same wrong assumption in isolation*, so the contradiction is invisible to the suite. (Note: the no-space `SystemAdmin` literal also appears in `app.routes.ts` and `tenants.routes.ts` comments/identifiers, so the codebase itself is internally inconsistent about the form.) **This is the canonical defect class that 0 E2E + mocked-seam unit tests cannot catch**, and it is sitting in the highest-privilege path in the product. I am reporting it, not fixing it (outside QA's lane).
**Confidence: 90%** that a System Admin login hits /forbidden on these routes at runtime; a 10-minute browser repro via `@browser-debugger` would settle it to 100%.

---

## 6. Risks & gaps

- **0% execution = unknown real quality.** 1,941 well-designed tests with no verdicts means we genuinely do not know what passes. "Designed" has been mistaken for "done."
- **No E2E safety net** for auth, tenant routing, or any multi-layer flow — exactly where multi-tenant SaaS fails most expensively. The `SystemAdmin` bug is proof the net is needed.
- **41 blocked TCs, all in `admin-console`.** Every blocked spec is in one module — worth a targeted review of *why* (likely deferred dependencies, e.g. multi-level org or RLS-phase work referenced in the matrix as "CONDITIONAL").
- **Mock-heavy unit suite gives false confidence.** Green Karma + green xUnit does not imply the app works end-to-end; the role-string defect demonstrates a green suite over a broken runtime path.
- **Traceability-matrix totals drift** from on-disk file counts — the matrix is a maintained artifact that has fallen behind; don't trust its `**TOTAL**` rows for current counts.
- **Stale docs:** `CLAUDE.md` still claims no backend test project (there are 201 test files).

---

## 7. Recommendations (leads into a separate automation plan — not written here)

1. **Stand up an E2E layer (Playwright recommended; the MCP server is already configured).** Start with one smoke test per persona login through to a landing page — that single test would have caught the `SystemAdmin` 403.
2. **Fix the role-string contract first, then guard it.** Pick one canonical form, align seeder + guards + specs, and add a test that asserts FE guard strings match the BE-seeded role names (a contract test over mocked strings).
3. **Bind a high-value slice of markdown TCs to real runners** — prioritize the 392 multi-tenant ISO and 85 e2e TCs (highest risk, currently unrunnable), mapping `TC-*` IDs to automated tests so traceability becomes executable.
4. **Introduce an execution-status lifecycle.** Move TCs off blanket `draft`; record `pass`/`fail` per run so "completion %" reflects execution, not authorship.
5. **Reduce mocking at the seams.** Add integration tests (real DB via Testcontainers on BE; real router/HTTP on FE) for auth, tenant resolution, and RBAC so cross-layer mismatches surface in CI.
6. **Housekeeping:** reconcile `TRACEABILITY-MATRIX.md` totals with on-disk counts, triage the 41 admin-console `blocked` TCs, and correct the stale `CLAUDE.md` backend-test-project line.

---

*Analysis-only deliverable. No test cases, source files, or PRs were modified. All TC IDs, file paths, and line numbers cited were read directly during this review.*
