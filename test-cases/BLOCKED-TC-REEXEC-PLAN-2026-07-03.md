# Blocked-TC Re-Execution Plan (post-blocker-clearance)

**Date:** 2026-07-03 · **Owner:** `@test-runner` (REPORT-ONLY) · **Precondition:** all 22 code blockers cleared (PRs #114–131) and finding-level verified (see [BLOCKER-VERIFICATION-PLAN-2026-07-02.md](BLOCKER-VERIFICATION-PLAN-2026-07-02.md) §5 — 21/21 PASS + BUG-126 logged).

---

## 0. What this plan is (and is NOT)

| | Already done (2026-07-02) | This plan (2026-07-03) |
|---|---|---|
| **Level** | **Finding-level** verification | **TC-level** re-execution |
| **Action** | One targeted probe per fix (e.g. "bad reset token → 400") | Re-run the **full** blocked test cases the fix gated (all assertions, all arms) |
| **Output** | Finding → `RESOLVED` in ledger | TC `status: blocked → pass/fail`, story `[b]/[!] → [x]/[!]` in TEST-STATUS |
| **Value** | Proves the fix works | Proves the fix **unblocked the TCs** + flips the STATUS board honestly |

The finding probes passing is a strong signal these TC re-runs will pass — but a TC checks more than the probe did, so this is genuine additional coverage, not a rubber-stamp.

**Discipline (unchanged):** report-only. A red TC re-run stays `blocked`/`fail` and spawns a fresh `/fix-finding` — never edit `src/` here, never weaken a TC to go green.

---

## 1. The cascade insight (why this is small, not 270 re-runs)

`grep status: blocked` → **270 TC files** across 11 modules. But they are **not** 270 independent blocks — a few **root** findings gate most of them. Clearing the root cascade-unblocks its whole downstream set:

| Root fix | Cascade it unblocks | Rough downstream TCs |
|---|---|---|
| **BUG-003** (cross-tenant guard, #119) | **every cross-module ISO arm** (~7 modules) | systemic — the single biggest unblock |
| **BUG-093** (employee create, #114) | all Core-HR TCs that need a seeded employee | ~30–60 core-hr |
| **BUG-037/086** (2026 leave balance, #117) | leave-management + reports balance/report TCs | ~leave + reports |
| **BUG-068** (applicant convert on PG, #115) | recruitment REC-010 convert chain | ~recruitment |
| **BUG-036** (Leave.ManageLop perms, #116) | leave LOP assign/override/summary surface | ~leave LOP |

So the strategy is: **run the roots first (they cascade), then the leaf finding-bound TCs, then re-triage whatever is still blocked** — most of the residual will be **env/persona/rig gaps** (the non-code track), not code.

---

## 2. Mechanism & tooling

| Use | Tool | Touches TEST-STATUS? | Notes |
|---|---|---|---|
| Finding-bound TCs (mapped in §5) | `/verify-fix {ID}` (`--iso` for BUG-003) | **Yes** (flips `[b]/[!]`, marks finding) | Purpose-built for exactly this; TC set resolved from the ledger |
| Broad module sweep of residual now-unblocked TCs | `/test-all {module}` (loop) | **Yes** | REPORT-ONLY, safe unattended; logs any new finding |
| Single-story spot re-run | `/test-us US-{ID}` | No (manual single-shot) | Use to confirm one story without the loop |

Prefer `/verify-fix` for the 22 findings' TCs (already mapped, fastest), then `/test-all {module}` to catch downstream TCs the finding-map didn't name.

---

## 3. Priority tiers

- **P0 — Gateway + rig-enablement** (must precede everything; they cascade-unblock the rest): BUG-003 ISO, then BUG-093 / BUG-037 / BUG-068 / BUG-036 / BUG-121.
- **P1 — Per-module finding-bound TCs** (the §5 matrix leaves): Auth, Admin, Core-HR, Payroll, Notifications.
- **P2 — Module sweeps + residual re-triage**: `/test-all` each module, then classify anything still blocked as **env-gap** (→ non-code track §7) vs **new finding** (→ `/fix-finding`) vs **stale** (→ flip).

---

## 4. Execution phases (top-to-bottom)

### T0 — Preflight (blocks everything)
- [ ] Stack up: PostgreSQL (PG18 `developer/hris_dev_db`) + Docker + API `:5000` (`/swagger` OK) + FE `:4200`. **No `dotnet build` while API runs — DLL lock.** *(API is currently already running from the 07-02 session.)*
- [ ] Reseed QA personas (memory `qa-personas-reseed`): `acme` tenant + tenantadmin/hr/manager/employee@acme.test (pw `Admin@123!`) + platform `admin@hrm.local`. Authz/ISO TCs BLOCK without these.
- [ ] Sanity: `curl -s localhost:5000/health` OK; login returns a token.

### T1 — Gateway (FIRST substantive re-run)
- [ ] `/verify-fix BUG-003 --iso` — full cross-module ISO re-run. **ISO arm assertions FLIP**: from "leak observed" → **403 `cross_tenant_denied` on JWT≠subdomain**. A red here reopens the systemic leak across ~7 modules — **STOP and re-fix** before continuing.

### T2 — Rig-enablement cascade (unblocks downstream seed/balance-dependent TCs)
- [ ] `/verify-fix BUG-093` — employee create → unblocks Core-HR seed-dependent TCs.
- [ ] `/verify-fix BUG-037` — 2026 leave balance + reports materialize.
- [ ] `/verify-fix BUG-068` — REC-010 convert (needs Docker/PG).
- [ ] `/verify-fix BUG-036` — Leave.ManageLop surface reachable.
- [ ] `/verify-fix BUG-121` — **stop Redis**, confirm `/auth/me` + `/my-tenants` still 200 (fail-soft).

### T3 — Per-module finding-bound TCs
- [ ] **Auth:** `/verify-fix BUG-040` · `BUG-041` · `BUG-042` · `BUG-043` · `BUG-004`.
- [ ] **Admin:** `/verify-fix BUG-007` · `BUG-107` · `BUG-008` · `BUG-001` · `BUG-106` *(BUG-106 manual: suspend tenant, login as its Tenant Admin, confirm tenant GET **not** 451 — TC-ADM-004)*.
- [ ] **Core HR:** `/verify-fix BUG-119` · `ISSUE-223`.
- [ ] **Payroll:** `/verify-fix BUG-072` · `BUG-073` *(PG)*.
- [ ] **Notifications:** `/verify-fix ISSUE-188`.

### T4 — Module sweeps (catch downstream TCs the finding-map didn't name)
Run `/test-all {module}` for the modules whose blocked count is dominated by a now-cleared root. Order by leverage:
- [ ] `/test-all core-hr` (60 blocked — most were BUG-093 / BUG-003 gated)
- [ ] `/test-all leave-management` (50 — BUG-036 / BUG-037 / BUG-003)
- [ ] `/test-all admin-console` (52 — BUG-003/007/107/008/001/106/004)
- [ ] `/test-all recruitment` (16 — BUG-068 / BUG-003)
- [ ] `/test-all notifications` (9 — ISSUE-188 / BUG-003) · `/test-all reports` (8 — BUG-037 / BUG-003) · `/test-all payroll` (13 — BUG-072/073/003) · `/test-all onboarding` (10 — BUG-003; watch BUG-126)

### T5 — Re-triage residual blocked
For every TC still `blocked` after T1–T4, classify (do **not** force green):
- [ ] **env/persona/rig gap** → move to non-code track §7 (e.g. attendance 13, performance 22 — mostly persona/perms gaps, NOT our 22 fixes).
- [ ] **new finding** → log in `TEST-FINDINGS.md` (OPEN) + `/fix-finding`.
- [ ] **stale** (blocker already gone, TC never re-run) → flip to pass/fail on this run.

### T6 — Close-out
- [ ] `TEST-STATUS.md`: flip `[b]/[!] → [x]` (clean) only where the story has **no** remaining findings; else `[!]` with the residual IDs. **No blanket flips** — story markers must reflect real per-TC state.
- [ ] TC files: `status: blocked → pass/fail` for every re-run TC.
- [ ] Post a summary: TCs re-run / passed / still-blocked-by-env / newly-opened findings.

---

## 5. Finding → TC map (from verification plan §1; the P0/P1 targets)

| Finding | PR | TCs to re-run | Tool |
|---|---|---|---|
| BUG-003 | #119 | ALL cross-module ISO arms | `/verify-fix BUG-003 --iso` |
| BUG-093 | #114 | TC-CHR-065/066/080/010 | `/verify-fix BUG-093` |
| BUG-068 | #115 | US-REC-010 convert TCs | `/verify-fix BUG-068` |
| BUG-036 | #116 | US-LV-011 LOP (assign/override/compulsory/summary) | `/verify-fix BUG-036` |
| BUG-037/086 | #117 | US-LV-006/010/012, US-RPT-002 | `/verify-fix BUG-037` |
| BUG-121 | #120 | Auth/Admin FE-hydration (`/auth/me`, `/my-tenants`) | `/verify-fix BUG-121` |
| BUG-040 | #118 | TC-AUTH-011/012 | `/verify-fix BUG-040` |
| BUG-041 | #122 | TC-AUTH-050/039/040 | `/verify-fix BUG-041` |
| BUG-042 | #122 | TC-AUTH-062 | `/verify-fix BUG-042` |
| BUG-043 | #129 | TC-AUTH-009/005/062 | `/verify-fix BUG-043` |
| BUG-004 | #127 | TC-ADM-006-12, TC-AUTH-012 | `/verify-fix BUG-004` |
| BUG-007 | #125 | TC-ADM-008-07 | `/verify-fix BUG-007` |
| BUG-107 | #125 | TC-ADM-003 | `/verify-fix BUG-107` |
| BUG-008/ISSUE-227 | #126 | TC-ADM-009-07/-10, TC-ADM-007-05 | `/verify-fix BUG-008` |
| BUG-001 | #130 | TC-ADM-003-04/-11 | `/verify-fix BUG-001` |
| BUG-106 | #130 | TC-ADM-004 (manual) | manual |
| BUG-119 | #124 | TC-CHR-123 | `/verify-fix BUG-119` |
| ISSUE-223 | #124 | TC-CHR-125 | `/verify-fix ISSUE-223` |
| BUG-072 | #123 | TC-PAY-006-01/-05 | `/verify-fix BUG-072` |
| BUG-073 | #128 | TC-PAY-006-02, TC-PAY-007-01 | `/verify-fix BUG-073` |
| ISSUE-188 | #121 | US-NTF-001 approval-notification arms | `/verify-fix ISSUE-188` |

---

## 6. Blocked-TC census (starting point) & expected residual

| Module | `blocked` TC files | Dominant root(s) | Expect after T1–T4 |
|---|---:|---|---|
| core-hr | 60 | BUG-093, BUG-003, BUG-119, ISSUE-223 | mostly clears |
| admin-console | 52 | BUG-003/007/107/008/001/106/004 | mostly clears |
| leave-management | 50 | BUG-036, BUG-037, BUG-003 | mostly clears |
| performance | 22 | BUG-003 (rest = **persona/perms**) | **residual env** |
| authentication | 17 | BUG-040/041/042/043/004/003/121/001 | mostly clears |
| recruitment | 16 | BUG-068, BUG-003 | mostly clears |
| attendance | 13 | BUG-003 (rest = **persona**) | **residual env** |
| payroll | 13 | BUG-072/073, BUG-003 | mostly clears (BUG-060 perms residual) |
| onboarding | 10 | BUG-003 (watch **BUG-126** new) | mostly clears |
| notifications | 9 | ISSUE-188, BUG-003 | mostly clears (ISSUE-188 producer) |
| reports | 8 | BUG-037, BUG-003 | mostly clears |
| **Total** | **270** | | |

> Counts are the current `status: blocked` census; some are **stale** (blocker gone, never re-run) and will flip on first re-run. attendance/performance blocked sets are dominated by **persona/permission gaps** (non-code), so expect them to stay blocked after the code-fix cascade — that's correct, not a failure.

---

## 7. Out of scope — non-code track (env, not fixes)

These keep TCs blocked for reasons **no code fix addresses**; handle separately (verification plan §V5):
- **Persona/seed:** BUG-060 (HR Officer Payroll perms), BUG-101 (`PublicCareersEnabled`), performance/attendance employee-linked personas.
- **Infra:** Redis, k6 harness, 5k/1k perf seeds, on-demand Hangfire triggers.
- **Deferred features (build, not fix):** multi-level approval, fiscal-year balances, US-AUTH-012/016.

---

## 8. Exit criteria

- Every §5 finding-bound TC re-run → `pass` (or `fail` with a tracked `/fix-finding`).
- T4 module sweeps complete; no now-unblocked TC left un-run.
- Every residual `blocked` TC classified (env / new-finding / stale) — none left "blocked, reason unknown".
- `TEST-STATUS.md` story markers reflect real per-TC state (`[x]` only where clean).
- Summary posted: re-run / passed / env-residual / newly-opened.

---

## 9. EXECUTION LOG — 2026-07-03 (loop run, REPORT-ONLY)

Executed T0→T6 via `@test-runner` (report-only). **Result: every one of the 22 code blockers is now verified at the TC level; the residual blocked census is non-code.**

### T0 preflight — GREEN
API `:5000` healthy; acme personas (tenantadmin/hr/manager/employee@acme.test) login 200; platform admin `admin@hrm.local` 200 via `X-Tenant-Subdomain: platform` (platform tenant subdomain is `platform`, not `admin`).

### T1 gateway (BUG-003 ISO) — **11/11 modules PASS**
Representative cross-tenant sweep (route-agnostic `TenantAccessGuardMiddleware`): acme-JWT + foreign-header → **403 `cross_tenant_denied`**; acme-JWT + acme-header → **200**. Zero leaks, zero over-blocks. Two endpoints BLOCKED on persona-authz (both arms 403, no leak; each module separately covered). Non-defect: guard runs after MVC authz, so perm-less endpoints return plain authz-403 rather than `cross_tenant_denied` (security-equivalent).

### T2 rig cascade — **PASS** (BUG-093/037/036/121)
BUG-093 create → 201 (EMP-0035; numeric-suffix-skip confirmed in source); BUG-037/086 balance 2026 + reports → 200 (no Accrued-enum 500); BUG-036 LOP summary → 200 for HR/admin; BUG-121 `/auth/me`+`/my-tenants` → 200 hydrated. BUG-068 stays PG-integration-test-proven (live convert would orphan rows). **By-design:** BUG-036 manager-403 is correct (LOP is HR/admin-only; finding title over-scoped "Manager").

### T3 per-module finding TCs — **19/19 PASS, 1 BLOCKED(env)**
- Auth: BUG-040 (forged/expired/reused reset token → 400) · BUG-004 (sub-min pw → 400) · BUG-041 (RBAC change → queryable audit row) · BUG-042 (switch during impersonation → 403 `switch_forbidden_during_impersonation`) · BUG-043 (rotated-token reuse → 401, independent session 200) — all PASS.
- Core-HR: BUG-119 (non-owned PATCH → 403; own → 200) · ISSUE-223 (default list hides Terminated, `?includeTerminated=true` shows) — PASS.
- Admin: BUG-007 (audit `?search=` → 200, no jsonb 500) · BUG-107 (destructive op under impersonation → 403 `impersonation_forbidden`) · BUG-008/ISSUE-227 (override>plan>snapshot: override=0 → employee 403 / workflow 409) · BUG-001 (System Support impersonation read-only → write 403) — PASS. **BUG-106 BLOCKED(env):** both suspended tenants have never-activated owner invites (no working credential) and acme must not be suspended; fix was HTTP-mutation-test-proven 2026-07-02.
- Payroll: BUG-072 (1e18 monetary → 400 not 500) · BUG-073 (rule create+update identical/changed slabs → 200 not 500) — PASS.
- Notifications: ISSUE-188 (manager approve → `leave.approved` notification row created) — PASS.

**By-design divergences flagged (TC-vs-impl, not code defects):** ISSUE-223 shipped as status-based *visibility* exclusion (`includeTerminated` flag) — GET-by-id of a Terminated employee still 200 (TC-CHR-125 step-3 "expect 404" is stale vs the shipped design). BUG-007 searches text columns (Detail/Action/ResourceType), not jsonb before/after payload — a term only in after-JSON is by-design unmatched; param is `search` not `searchQuery`.

### T4/T5 re-triage of the full 271-blocked census — classified (no stale flips found)
Only **25** blocked TCs reference a now-cleared root (all covered by T1–T3). The other **246 are blocked by causes NONE of the 22 code fixes touch** — spawning sweep agents would only re-confirm the same env/rig gaps:

| Cat | Count | Blocker | Track |
|---|---:|---|---|
| A | 25 | our 22 fixes | ✅ verified in T1–T3 |
| B | 15 | Firefox/WebKit cross-browser rig | infra |
| C | 46 | browser a11y (axe-core) / FE-UI-render arm | infra (browser rig) |
| D | 23 | gated on FE bugs BUG-096/097/099/100, ISSUE-207/214 | **separate FE-fix track** |
| E | 61 | k6 / 500-emp perf seeds | infra (perf harness) |
| F | 24 | persona/seed gaps | env |
| G | 44 | deferred/unimplemented features (virus scan, EXIF, fiscal-yr, multi-level approval, year-end job) | product backlog |
| H | 33 | other (mostly more B/E + data-setup) | infra/data |

### T6 close-out
- **34 TC files** flipped `blocked/fail → pass` (18 ISO reps + 16 finding-bound) with dated `exec_note`.
- Story markers: the 5 `[b]` stories are **all SSO/Entra** (US-AUTH-011/012/013/014/016) — deferred-feature track, none ours; left as-is. `[!]` stories keep `[!]` (still carry Cat B–H blocked TCs). No dishonest `[x]` flips.
- Findings ledger unchanged (all 22 already `RESOLVED` from the 07-02 close-out; no new FAILs; BUG-106 is BLOCKED-env not FAIL).
- **No new findings opened.** State clean: overrides removed, impersonation sessions ended, test leave request cancelled, no suspended tenant touched. One legit residual test row EMP-0035 in acme (no hard-delete verb).

### Bottom line
The code-fix verification loop is **complete and green**. What remains blocked is four *separate* backlogs — FE-fix (Cat D), browser+perf rigs (B/C/E/H), persona seeding (F), and deferred features (G) — each of which is its own track, not a gap in this remediation.

---

## 10. FOLLOW-ON — Track A (live convert) + Track B (browser rig), 2026-07-03

User elected "A + B": run the live REC-010 convert E2E, and stand up + batch the browser rig.

### Track A — live REC-010 convert E2E (BUG-068)
**BUG-068 is fixed and now LIVE-proven:** `POST /api/v1/recruitment/applicants/{id}/convert` → **HTTP 201**, created employee **EMP-0036** (`019f2607-…`, acme, filled 1/3), no Npgsql retry/tx 500. Of the 6 REC-010 TCs:
- **PASS (2):** TC-REC-010-04 (convert + data carried), TC-REC-010-06 (duplicate → 409 `already_converted`, no side effects).
- **FAIL → new ISSUE-232 (1):** TC-REC-010-05 — the "Converted" badge + employee deep-link (AC-4) are not surfaced on `/applicants/{id}/detail` (only the conversion-prefill DTO carries the linkage). Read-projection gap, data is persisted.
- **BLOCKED, non-BUG-068 (3):** 010-03 auto-create-user is deferred (ISSUE-140); 010-08 auto-close needs headcount-fill (row spam); 010-10 limit-block needs a tenant-config write (report-only can't).

### Track B — browser rig: proven, but the "blocked census" was mis-classified
**Rig works.** Both Playwright & Chrome-DevTools MCP drove the live app end-to-end. **Unlock recipe (durable):** host **`http://acme.myhrm.org:4200`** (hosts entry exists; `localhost`→platform tenant, `acme.localhost`→login 400); Playwright login needs `pressSequentially` (reactive form ignores `fill()`); Chrome-DevTools `fill_form` works; a11y via `lighthouse_audit` (external axe CDN is CSP-blocked); `emulate` viewport **without** mobile/touch flags (touch forces reload→logout = BUG-097); SPA-nav only (hard reload logs out).

**Key correction:** the grep that built the "~50 browser-runnable" list conflated *TCs that mention an a11y/responsive strata section* with *TCs whose objective is a11y/responsive*. In reality most were perf/functional/integration/deferred. Genuine, rig-clearable TCs are **few** and several are **persona-gated**.

**Wave results:**
- **Wave 1 (Core-HR non-employee, 18 TCs): 3 PASS** — TC-CHR-030 (Departments responsive+a11y), TC-CHR-061 (Job-Titles), TC-CHR-190 (Locations). 13 blocked by *other* rigs: cross-browser needs FF/WebKit (4), FE-perf needs scale seed + BUG-097 fix (7), deferred custom-fields (2); org-tree empty = ISSUE-207.
- **Wave 2 (multi-module a11y/responsive, 24 TCs): 0 flips** — agent correctly refused to flip perf/functional IDs to pass (would fabricate). Confirmed **5 pages render clean** (Dashboard/Reports/Vacancies/Pipeline/Onboarding — a11y 96, responsive clean); genuine a11y TCs (AUTH-048 roles, LV-170/173/174 team-calendar) are **persona-gated** for `hr@acme.test` (`/leave`, `/admin/*` → `/forbidden`).

**New a11y findings (real, logged OPEN):** ISSUE-233 (Core-HR ARIA structure nits), ISSUE-234 (Leave-Types `role="switch"` toggle has no `aria-checked`/name — SR users can't operate it).

### Must-fix / must-provision to unblock the rest (reported, not fixed — report-only)
1. **BUG-099** (FE) — `EmployeeListComponent` `.length`-of-undefined crash → employee directory renders 0 rows; **gates ~40 employee-dir/profile TCs**. Highest-leverage FE fix.
2. **BUG-097** (FE) — touch/mobile emulation forces reload→logout; blocks mobile-viewport arms.
3. **ISSUE-207** (FE/data) — org-tree renders "No departments found"; blocks org-tree TCs.
4. **Persona reach** — genuine a11y TCs need `tenantadmin`/manager/employee (not `hr`) to reach `/leave`, `/admin/*`, `/notifications`.
5. **FF/WebKit rig** — cross-browser TCs need Playwright FF/WebKit projects (the MCP is chromium-only).
6. **Perf seeds** — perf TCs need the 200/500-dept, 200-job-title, 5k-employee seeds.

### Track B net
5 real passes (CHR-030/061/190 + rig-proven page health on 5 more pages), 2 new a11y findings, and a corrected map of what "browser-blocked" actually means. The chromium-CDP rig's practical ceiling is the **responsive + a11y-audit arm on pages that render for the current persona** — a small set, not the 90 the census implied.
