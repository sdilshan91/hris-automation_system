# Test-Environment Setup Plan — unblock the "needs-setup" TCs (B + C)

> Goal: stand up the **test harness / data / infra** needed to execute the blocked TCs that do
> **not** require application code changes. Companion to [QA-COVERAGE-PLAN.md](QA-COVERAGE-PLAN.md)
> (P3) and [INTEGRATION-PERF-TEST-PLAN.md](INTEGRATION-PERF-TEST-PLAN.md) (Tracks A/B).
> **Report-only** for execution; **no app `src/` changes** — every item here is data/infra/harness.
> Update the **Status Tracker** + **Progress Log** as each phase runs.

## Status legend
`[ ]` not-started · `[~]` in-progress · `[x]` done-clean · `[!]` done-with-findings · `[b]` blocked

## Scope (from the 348 currently-blocked TCs, 2026-07-01)
- **A — needs CODE (out of scope here): ~56** — `[DEFERRED]` features + RLS (US-PLT-002) + BUG-097-gated FE-perf. Tracked in [user-stories/STATUS.md](../user-stories/STATUS.md) QA-Surfaced Dev Backlog.
- **B — needs TEST SETUP (this plan): ~78** — scale seeds, 2nd instance, cross-browser, WS client.
- **C — old/un-triaged: ~214** — reclassified by S1; the testable ones then flow into S2–S6.
- **Caveat:** "unblock" = executable. Several will land **fail** on existing bugs (esp. BUG-003) — passing those still needs the code fixes (bucket A).

## Prerequisites (verify before S1)
- Backend `:5000` up (`ASPNETCORE_ENVIRONMENT=Development` so user-secrets load), FE `:4200` up, nginx rig optional.
- PostgreSQL 18 `hris_dev_db` reachable via psql; Docker Desktop up; k6 installed.
- Personas seeded (`Admin@123!`); `acme`/`techoneglobal` intact.

---

## Phases

| Phase | Goal | Tasks | Success criteria | Effort |
|---|---|---|---|---|
| **S0 — Pre-flight** | Baseline healthy | Confirm backend/FE/Docker/psql/k6 all up; snapshot acme/techoneglobal counts | All green; baseline recorded | XS |
| **S1 — Re-triage C (read-only)** | Turn 214 unknowns into real buckets | Read each un-triaged blocked TC; reclassify → `now-testable` (no setup) · `needs-B-setup` (which) · `needs-code` (→A) · `already-covered`. Write a precise `exec_note` per TC. NO execution, NO data, NO code | Every C TC has a bucket + reason; a count of "free re-runs" produced | S |
| **S2 — Scale data seeds** | Realistic volume for scale/perf/list TCs | (a) extend `perf/seed/seed-perf-tenant.sql` → **50k employees** in the `perf` tenant; (b) new script → **100+ throwaway tenants** (reuse `iso`/`fntest` pattern); (c) per-module volume (leave requests, attendance rows, payroll run) into a throwaway tenant. All idempotent + teardown-by-exact-id | 50k emp in `perf`; 100 `scale*` tenants; module rows present; acme/TG untouched | M |
| **S3 — 2nd backend instance (Redis multi-instance)** | Prove cross-instance SignalR backplane | Start Redis container + `ConnectionStrings:Redis`; run a **2nd `HRM.Api` on :5001** (same DB+Redis); connect a **SignalR/WS client** to each; fire a notification on A → assert it fans out to the client on B via Redis pub/sub | Cross-instance fan-out observed; backplane channel active | M |
| **S4 — Cross-browser rig** | Firefox/WebKit render + a11y arms | `npx playwright install firefox webkit`; author a small multi-engine harness (or `@browser-debugger` per engine) that re-runs the render/a11y TCs on Firefox + WebKit | Key pages render + axe-clean (modulo systemic a11y) on 2 more engines | M |
| **S5 — Live WS notification client** | Real-time delivery / reconnect TCs | Small WS client on `/hubs/notifications` + a producer to trigger an event; test deliver / drop-reconnect / redelivery | Live notification delivered + reconnect verified | S–M |
| **S6 — Execute + reconcile + teardown** | Run the unblocked TCs; record verdicts | Execute S1-confirmed-testable + S2–S5-enabled TCs (via `@test-runner`, REPORT-ONLY); flip `pass`/`fail`(ref finding)/keep-`blocked`; log findings; **drop all throwaway tenants + stop containers + revert config**; verify zero residue | Each executed TC has a verdict; all fixtures dropped; acme/TG intact | L |

---

## Safety (non-negotiable — per 2026-06-27 policy + the git-OOM incident)
- **Throwaway tenants only** for writes (`scale*`, `perf`, `iso*`, `fntest`); teardown by **exact tenant_id / `subdomain LIKE`** prefix; verify no real tenant matches the prefix first. Never mass-delete by `tenant_id` on a shared tenant.
- **No app `src/` changes.** Redis/SMTP/2nd-instance are config + containers; **revert user-secrets + remove containers** in S6.
- **Commit findings promptly** (small commits) — do NOT accumulate large uncommitted working trees (git-stash under memory pressure discarded a tree on 2026-07-01; recovered from context). Avoid `reset --hard` with uncommitted work.
- Re-set the **Gmail SMTP app password** in user-secrets only if SMTP is re-tested (it was removed 2026-07-01).

## Recommended sequence (cheapest / highest-yield first)
**S0 → S1 (re-triage C, read-only) → S2 (scale seeds: 50k + 100-tenant) → S6-partial (execute the S1-free + S2 TCs) → S3 (2nd instance) → S5 (WS client) → S4 (cross-browser) → S6 teardown.**
Rationale: S1 is free and de-risks everything; S2 unblocks the largest concrete bucket (admin/perf scale) with a pattern we already have; S3/S4/S5 are the heavier rigs, done last.

## What this does NOT unblock
- The **~56 code-gated TCs** (feature-not-built + RLS + BUG-097) — need the **dev fix cycle**, not setup.
- TCs that **execute but fail** on existing bugs (BUG-003 etc.) — unblocked here, but only go green after the code fixes.

---

## STATUS TRACKER
| Phase | Status | Started | Finished | Result / notes |
|---|---|---|---|---|
| S0 Pre-flight | `[x]` | 2026-07-01 | 2026-07-01 | backend/FE 200, Docker OK, k6 v2.0.0, acme login OK; baseline acme=34/TG=1, 23 tenants, no stale throwaway |
| S1 Re-triage C | `[x]` | 2026-07-01 | 2026-07-01 | **251 un-triaged reclassified** (title+type): **152 now-testable no-setup** (136 re-run + 16 iso) · 52 needs-B-setup (32 S2-scale, 15 S4-crossbrowser, 3 S5-WS, 2 S3-2nd-instance) · 47 needs-code (deferred/RLS/BUG-097). exec_notes written per TC |
| S2 Scale seeds | `[~]` | 2026-07-01 | | Seeding 50k + 100 tenants + module volume |
| S3 2nd instance | `[ ]` | | | |
| S4 Cross-browser | `[ ]` | | | |
| S5 WS client | `[ ]` | | | |
| S6 Execute + teardown | `[ ]` | | | |

## PROGRESS LOG
| Date | Phase(s) | What happened | Outcome |
|---|---|---|---|
| _2026-07-01_ | — | Plan created | Ready to start (recommend S0→S1→S2 first) |

---

## Estimated unblock (rough, pre-S1)
| Bucket | ~TCs | Unblocked by |
|---|--:|---|
| Scale/perf/list-at-volume | ~45 | S2 |
| Redis multi-instance backplane | ~4 | S3 |
| Cross-browser render/a11y | ~10–20 | S4 |
| Live WS notification | ~5 | S5 |
| C "free re-runs" (no setup) | **?** (S1 reveals) | S1 → S6 |
> Firm numbers land after **S1**; today's split is 56 code / 78 setup / 214 un-triaged.
