# QA Coverage Plan — Full Testing (prioritized)

> Created 2026-06-27. Companion to [TEST-STATUS.md](../TEST-STATUS.md) (per-story state) and
> [TEST-FINDINGS.md](../TEST-FINDINGS.md) (defect ledger). **Report-only** — executing test cases and
> logging findings; never fixes code. Tracks the remaining full-testing campaign by phase.

## Reality check
- **Modules 1–8** (auth, core-hr, leave, attendance, recruitment, payroll, performance, admin):
  all executed at least once — `[!]` (findings logged) / a few `[x]` clean. ~70 stories.
- **Zero coverage (16 stories):** Onboarding (6), Notifications & Audit (5), Reports & Analytics (5).
- **SSO epic (US-AUTH-011..016):** shipped in PR #112, **not yet in the tracker**; only AC-1/2/5/7 of
  US-AUTH-011 live-verified (2026-06-26). See [authentication/SSO-EPIC-STATUS-AND-TODO.md](../../BA/authentication/SSO-EPIC-STATUS-AND-TODO.md).
- Findings backlog: **BUG-003 (CRIT, cross-tenant write) still OPEN**; ~149 CRIT/HIGH mentions.

## Phases & priority

### P0 — SSO epic (security-critical, just shipped)
Tenant-isolation code; isolation defects = cross-tenant breach. TCs `TC-AUTH-011..016` exist.
- US-AUTH-013 — fail-closed isolation (foreign tid/domain rejected; empty allow-list denies all) — **Must Have**
- US-AUTH-014 — oid match / email-bootstrap link / JIT / non-member rejection
- US-AUTH-011 — happy path + id_token negatives *(happy path needs interactive Microsoft login)*
- US-AUTH-015 — button render / sso_only UX / callback token storage (Playwright)
- US-AUTH-012 / US-AUTH-016 — **not built yet** (see SSO TODO); test once implemented
- **Component gaps (recommendation, not report-only):** no xUnit for `EntraSsoService`; no `sso-callback.component.spec` — hand to a dev to add.

### P1 — Zero-coverage modules (run in this order)
1. **Notifications & Audit** (US-NTF-001..005, 80 TCs) — audit is cross-cutting/compliance-critical.
2. **Reports & Analytics** (US-RPT-001..005, 80 TCs) — aggregates across modules; re-check BUG-003 doesn't leak cross-tenant rows into reports.
3. **Onboarding/Offboarding** (US-ONB-001..006, 95 TCs) — feature-contained, largest, lowest cross-cut risk.

### P2 — Regression of modules 1–8 `[!]` — **GATED on fixes landing**
Don't run now (would re-confirm ~70 known findings). Trigger after a fix cycle; BUG-003 gets a focused
isolation regression sweep the moment its fix lands.

### P3 — Remaining blocked-TC clearance (added 2026-06-30; **recommended order**)
After standing up the **FE** (`acme.myhrm.org:4200`), the **nginx HTTPS rig**, **platform-admin login**
(`?tenant=platform`), the **throwaway-tenant isolation fixture**, **Docker** integration (Track A — 518 ITs
green), and the **k6 perf harness** (Track B — see [INTEGRATION-PERF-TEST-PLAN.md](INTEGRATION-PERF-TEST-PLAN.md)),
**~315 of the 431 still-blocked TCs are now testable.** Run highest-signal first:

- **P3a — Cross-tenant isolation (throwaway-tenant fixture)** — 42 `security` TCs.
  - `[!]` **admin-console (14) DONE 2026-06-30** → 6 pass / 8 fail; **NEW BUG-106, BUG-107 (impersonation destructive-op bypass), ISSUE-217**.
  - `[ ]` remaining by module: **core-hr 15** (fixture-ready) · reports 8 · payroll 6 · leave 4 · performance 3 · auth 3 · notifications 2 · attendance 1.
  - Method: reuse the `iso*` fixture (`scratchpad/iso-fixture-seed.sql`), seed per-module data as needed; run each module's write-isolation / IDOR / lifecycle arms; **writes ONLY between throwaway `iso*` tenants; teardown by exact id**. Highest hit-rate for net-new security findings.
- **P3b — Performance / scale (k6 harness + per-module seed)** — `[!]` **DONE 2026-06-30: 21 pass / 1 fail / 90 blocked / 2 draft, 0 net-new.** Ran k6 (hot-reads, module-lists, scale-reads, auth-login) on the 5k `perf` tenant. **All read/list/aggregate endpoints meet SLA** at 5k+50VU (employee-list p95 212ms, dashboard 106ms, reports 18ms, dept/title/loc/custom-fields/notif 57–70ms, audit-log 96ms, scale list100 93ms — 0 errors). **2 perf defects re-confirmed (already filed): ISSUE-203** (login p95 2.43s @20VU > 800ms — BCrypt-bound) + **BUG-095** (export 500s under concurrent same-second exports). The **90 still-blocked need a DIFFERENT tool than k6 read-load**: FE-render/FPS arms → Chrome DevTools perf traces; write-load arms → write k6 scripts + data; module-volume arms → seed leave/attendance/payroll records; extreme-scale → 50k-employee / 100+-tenant seeds; Redis-caching arms → Redis not wired (in-memory fallback). New k6 script: `perf/scripts/05-module-lists.js`.
- **P3c — Functional / a11y / integration with seeded data** — `[~]` STARTED 2026-06-30, **FE-blocks first** (~97 TCs: 55 a11y deep-arms + 42 FE-render-perf).
  - **P3c-a11y (55, testable now)** — Playwright keyboard (Tab/focus/Enter/Space/Arrow) + responsive (resize 360px, no h-scroll) + ARIA/SR (a11y snapshot, live regions). ~10 need a data state. By module: payroll 10, attendance 8, recruitment 7, performance 7, leave 7, onboarding 6, core-hr 4, notif/auth 2, reports/admin 1.
  - **P3c-FE-perf (42, partial)** — Chrome DevTools traces. **LIMITATION:** Lighthouse *navigation* mode (FCP/TTI/LCP) reloads → logs out (BUG-097) on authenticated pages, so cold-load FCP/TTI on feature pages stays blocked-on-BUG-097; soft-nav render timing + interaction fps ARE measurable. By module: core-hr 18, admin 5, payroll/recruitment 4, leave 3, perf/notif/auth 2, attendance/reports 1.
  - **P3c-a11y progress 2026-06-30/07-01:** done core-hr, payroll, attendance, leave, notifications, reports (+partial recruitment/perf/onboarding). suite a11y-blocked **55→38**. **6 net-new = systemic CLASSES: BUG-108** (aria-hidden focusable upload), **BUG-109** (hand-rolled overlays missing focus-trap/inert/escape — recurs on EVERY module's drawers/modals), **BUG-110** (role-misuse tablist), **BUG-111** (missing aria-live counter), **BUG-112** (non-focusable scroll region) + systemic **BUG-096** (contrast). All 6 → fixes in [STATUS.md](../../BA/STATUS.md) QA-Surfaced Dev Backlog. **Last 2 batches = 0 net-new** → classes fully characterized; remaining ~31 a11y TCs are systemic re-confirmation + data-gap-blocked (recruitment 0-applicants / performance sparse). **STOPPED the per-TC grind** — actionable output (6 systemic a11y fixes) captured; re-test the remainder after the fixes land.
  - **FE-render-perf (42): blocked-on-BUG-097** — cold-load FCP/TTI on authenticated pages needs a full navigation that logs you out; not cleanly measurable until BUG-097 is fixed.
  - **P3c-functional STARTED 2026-07-01 — Core HR (59 blocked worked): 130P/36F/49B/9draft. NET-NEW: BUG-113 HIGH** (employee Create/Edit API has no `LocationId` → employee↔location linking impossible, per-location count always 0, deactivation-guard dead code), **BUG-114 MED** (tenant storage quota never enforced), **ISSUE-218 MED** (reporting-manager not on employee GET). Higher yield than a11y (real business-logic gaps). **⚠ DATA-SAFETY:** functional write-flows mutate REAL acme rows (BUG-093 blocks throwaway-employee creation, so status/profile tests use real employees); truncation left EMP-0030 mutated twice → manually restored (acme back to 34 total / 27 active baseline). **Continued on a THROWAWAY `fntest` tenant** (clean EMP-NNNN → creates work, dropped after — zero acme risk, verified contained): **Leave 108P/22F/35B · Auth 33P/7F/9B/6draft · Admin 60P/8F/15B — all 0 NET-NEW** (real fails map to known BUG-037/086/040/004/104 etc.; business rules mostly hold). **Pattern: functional net-new was front-loaded in Core HR (BUG-113/114/218); 3 subsequent modules = 0 net-new.** fntest dropped clean. **STOPPED the functional/integration grind** — remaining ~27 functional + ~31 integration TCs would predictably be 0 net-new confirmation; best re-run after the fixes land. Integration also largely overlaps the Track A suite (518 ITs green).

**Hard-blocked — NOT testable, need DEV (tracked in [docs/BA/STATUS.md](../../BA/STATUS.md)):**
- **97** `[DEFERRED]` features (monitoring error-rate/SLA KPIs, PDF exports, magic-link email, async/Hangfire paths…).
- **19** PostgreSQL RLS / at-rest-encryption → **US-PLT-002** (env precondition now met; needs the migration).

## Recommended sequence
**P0 SSO → P1 (Notifications → Reports → Onboarding) → P2 regression → P3a isolation → P3b perf → P3c functional/a11y → [dev: US-PLT-002 + deferred features] → re-test the newly unblocked.**

## Progress log
| Date | Phase | Story/Module | Result | Findings |
|---|---|---|---|---|
| 2026-06-26 | P0 | US-AUTH-011 (partial) | AC-1/2/5/7 PASS (live) | none new; happy-path + id_token negatives pending |
| 2026-06-27 | P1a | Notifications & Audit | 45P/20F/15B — DONE | BUG-081..085, ISSUE-188..192 (BUG-082/084 HIGH; ISSUE-191 audit leak) |
| 2026-06-27 | P1b | Reports & Analytics | 39P/6F/15B — DONE | BUG-086 HIGH, ISSUE-193..199 (ISSUE-193 module-wide BUG-003 leak) |
| 2026-06-27 | P1c | Onboarding/Offboarding | all 6 executed — DONE | BUG-087..092, ISSUE-200 (⚠ DB cleanup incident — no data lost; policy updated) |
| 2026-06-27 | P2 | Authentication | all prior present, 0 new — DONE | BUG-040/BUG-003 CRIT re-confirmed |
| 2026-06-27 | P2 | Core HR | DONE | **NEW BUG-093 HIGH** (employee create broken) + ISSUE-201 |
| 2026-06-27 | P2 | Leave Management | 0 deltas — DONE | BUG-086 == leave BUG-037 confirmed |
| 2026-06-27 | P2 | Attendance | 0 new — DONE | — |
| 2026-06-27 | P2 | Recruitment | 0 deltas — DONE | BUG-068 CRIT re-verified still broken |
| 2026-06-27 | P2 | Payroll | 0 new — DONE | self-protected held |
| 2026-06-27 | P2 | Performance | 0 deltas — DONE | flagged BUG-068 ID collision + orphan EMP-0001 |
| 2026-06-27 | P2 | Admin Console | 0 new — DONE | BUG-004 NOT fixed (hardcoded, not policy); BUG-008 still broken |
| _pending_ | P0 | US-AUTH-013/014/015 | — | 015 FE testable; 013/014 need interactive Microsoft login |
| 2026-06-30 | (infra) | FE :4200 + nginx + Tracks A/B | DONE | 518 ITs green; k6 5k perf (BUG-095, ISSUE-203); FE sweep 21 finds (BUG-096..104, ISSUE-204..215; 6 HIGH) |
| 2026-06-30 | P3a | Admin-console isolation (14 TCs) | 6P/8F — DONE | **BUG-106, BUG-107** (impersonation destructive-op bypass), **ISSUE-217**; iso fixture torn down clean |
| 2026-06-30 | P3a | Core HR isolation (security TCs) | 69P/19F/14draft/9B — DONE | **0 net-new** — all 19 fails = BUG-003 cross-tenant-leak confirmations on Core HR surfaces (referenced). IDOR→404 + write-stamping arms PASS |
| 2026-06-30 | P3a | reports/payroll/leave/perf/auth/notif/attendance isolation | DONE | **0 net-new** — all fails = BUG-003 read+WRITE-leak confirmations (live write-leak into isob reproduced on notif templates). Self-protected/IDOR arms PASS. iso fixture torn down clean |

## P3a COMPLETE (2026-06-30) — cross-tenant isolation across all 11 modules
**Net-new: BUG-106, BUG-107 (impersonation destructive-op bypass), ISSUE-217** — all from admin-console's unique lifecycle/impersonation arms. Every other module = **BUG-003 surface confirmation, 0 net-new** (as predicted). P3a's lasting value: BUG-003 is now documented as breaching read+write isolation on essentially every `.All`-gated surface app-wide → strengthens the case that **fixing BUG-003 (validate JWT tenant_id vs subdomain) is the single highest-leverage security fix.** Next: **P3b perf** (different signal class).

## Status: P1 + P2 COMPLETE; P3 STARTED (2026-06-30)
P1+P2 done 2026-06-27. P3a (isolation) started — admin-console 14 done (BUG-106/107). **Next: Core HR
isolation, then the remaining P3a modules → P3b perf → P3c.** Top fixes feeding the dev backlog:
**BUG-003** (systemic cross-tenant), **BUG-107** (impersonation destructive-op block bypassed),
**BUG-104/ISSUE-217** (`/exports` vs `/data-exports` route mismatch), **US-PLT-002** (RLS, unblocks 19 TCs).
