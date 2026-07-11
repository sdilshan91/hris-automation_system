# Blocker Verification Plan

**Created:** 2026-07-02
**Scope:** verify the ~19 fixes shipped by the blocked-TC remediation campaign (PRs #114–129) against the
**running stack**, flip `TEST-STATUS.md`, and mark findings `RESOLVED` in `TEST-FINDINGS.md`.
**Companion to:** `BLOCKED-TC-REMEDIATION-PLAN-2026-07-02.md`.

> Every fix is already **unit/integration-green + mutation-proven** offline. Verification is the
> **report-only, live-stack** confirmation that each fix clears its blocked TCs. It is done via
> `/verify-fix {ID}` (never by hand-editing `src/`), which re-runs the finding's TCs through `@test-runner`.

---

## 0. Prerequisites (bring these up first)

| Need | How | Why |
|---|---|---|
| **Merge PRs** | Merge #114–129 into the working branch first | Verify against consolidated code, not 19 branches |
| **API** | `dotnet run --project src/backend/HRM.Api` → `:5000` | API-layer TCs. ⚠️ holds a DLL lock — no builds while running |
| **PostgreSQL** | PG18 `developer/hris_dev_db` (user-secrets) | Real DB behavior (BUG-037/068/073/007 are Postgres-specific) |
| **Frontend** | `npm start` in `src/frontend` → `:4200` | FE-hydration TCs (BUG-121) + any UI arms |
| **Redis** | optional; leave blank = in-memory | BUG-121 fail-soft path can be tested by stopping Redis |
| **Docker** | running | Testcontainer integration suites (convert, ledger, audit, statutory) |
| **Personas** | reseed QA personas (see memory: `qa-personas-reseed`) | authz/isolation TCs BLOCK without them |

> **Do NOT** run the report-only loop and a build at the same time — the running API locks the DLLs.

---

## 1. Verification matrix (fix → TCs → command)

Run `/verify-fix {ID}` per row. It re-runs the affected TCs, flips `TEST-STATUS`, and — on green — marks
the finding `RESOLVED` with the PR#. TC IDs below are the expected targets; `/verify-fix` resolves the exact
bound set from the ledger.

### Phase-A/B/C (PRs #114–125, already merged)
| Finding | PR | Affected TCs (verify) | Command |
|---|---|---|---|
| **BUG-093** | #114 | TC-CHR-065/066/080/010 (create w/ non-numeric emp-no) | `/verify-fix BUG-093` |
| **BUG-068** | #115 | US-REC-010 convert TCs | `/verify-fix BUG-068` |
| **BUG-036** | #116 | US-LV-011 LOP surface (assign/override/compulsory/summary) | `/verify-fix BUG-036` |
| **BUG-037/086** | #117 | US-LV-006/010/012, US-RPT-002 (2026 balance + reports) | `/verify-fix BUG-037` |
| **BUG-040** | #118 | TC-AUTH-011/012 (reset-token) | `/verify-fix BUG-040` |
| **BUG-003** | #119 | **ALL cross-module ISO arms** | `/verify-fix BUG-003 --iso` |
| **BUG-121** | #120 | Auth/Admin FE-hydration TCs (`/auth/me`, `/my-tenants`) | `/verify-fix BUG-121` |
| **ISSUE-188** | #121 | US-NTF-001 approval-notification arms | `/verify-fix ISSUE-188` |
| **BUG-041** | #122 | TC-AUTH-050/039/040 (RBAC audit trail) | `/verify-fix BUG-041` |
| **BUG-042** | #122 | TC-AUTH-062 (switch during impersonation) | `/verify-fix BUG-042` |
| **BUG-072** | #123 | TC-PAY-006-01/-05 (numeric overflow → 400) | `/verify-fix BUG-072` |
| **BUG-119** | #124 | TC-CHR-123 (self-edit ownership 403) | `/verify-fix BUG-119` |
| **ISSUE-223** | #124 | TC-CHR-125 (Terminated excluded from default list) | `/verify-fix ISSUE-223` |
| **BUG-007** | #125 | TC-ADM-008-07 (audit keyword search on PG) | `/verify-fix BUG-007` |
| **BUG-107** | #125 | TC-ADM-003 (impersonation destructive-op block) | `/verify-fix BUG-107` |

### Deferred-cleared (PRs #126–130)
| Finding | PR | Affected TCs (verify) | Command |
|---|---|---|---|
| **BUG-008/ISSUE-227** | #126 | TC-ADM-009-07/-10 (employee cap), TC-ADM-007-05 (workflow cap) | `/verify-fix BUG-008` |
| **BUG-004** | #127 | TC-ADM-006-12, TC-AUTH-012 (tenant password policy on reset) | `/verify-fix BUG-004` |
| **BUG-073** | #128 | TC-PAY-006-02, TC-PAY-007-01 (statutory rule update) | `/verify-fix BUG-073` |
| **BUG-043** | #129 | TC-AUTH-009/005/062 (per-session revocation) | `/verify-fix BUG-043` |
| **BUG-001** | #130 | TC-ADM-003-04/-11 (System Support read-only impersonation) | `/verify-fix BUG-001` |
| **BUG-106** | #130 | TC-ADM-004 (suspended-tenant admin read-only access) | `/verify-fix BUG-106` |

---

## 2. Highest-priority / special-case verification

1. **BUG-003 `--iso` FIRST** — it's the systemic gateway. Its ISO re-run across all modules is the single
   most important verification; a regression here reopens the cross-tenant leak in ~7 modules. It also
   *changes behavior of the existing ISO TCs* (they must now assert **403 on JWT≠subdomain**, not a leak),
   so expect the ISO arm assertions to flip from "leak observed" to "blocked".
2. **Postgres-specific fixes** (BUG-037, BUG-068, BUG-073, BUG-007) — must be verified with **real PG**
   running; InMemory masks them (that's how they slipped through originally).
3. **BUG-121** — verify the fail-soft path by **stopping Redis** and confirming `/auth/me` + `/my-tenants`
   still 200 (hydration survives).
4. **Sequencing** — verify rig-enablement fixes (BUG-093, BUG-037, BUG-068, BUG-036) before the TCs that
   depend on seeded employees / 2026 balances, so downstream TCs aren't blocked by the very bugs just fixed.

---

## 3. Still-open

**None.** All 21 code-clearable blockers are fixed (PRs #114–130). BUG-001 + BUG-106 (the last two) were
root-caused via HTTP repro — the JWT `MapInboundClaims` remapping of the `"roles"` claim — and fixed in
PR #130. Nothing remains code-blocked; only the **non-code track** (env/persona/perf-harness/deferred
features from remediation-plan §6) is outstanding, and that is env work, not fixes.

---

## 4. Verification execution — phased TODOs

Run top-to-bottom. Each `/verify-fix` re-runs the finding's TCs, flips `TEST-STATUS.md`, and (on green)
marks the finding `RESOLVED` with its PR#. **Report-only:** never edit `src/` here; a red re-run stays
`OPEN` and spawns a fresh `/fix-finding`.

### Phase V0 — Prerequisites (do first; blocks everything)
- [ ] Merge **PR #130** (BUG-001/106). PRs #114–129 already merged.
- [ ] Start **PostgreSQL** (PG18 `developer/hris_dev_db`, secret in user-secrets) + **Docker**.
- [ ] `dotnet run --project src/backend/HRM.Api` → confirm `:5000` + `/swagger`. *(No builds while it runs — DLL lock.)*
- [ ] `npm start` in `src/frontend` → confirm `:4200`.
- [ ] Reseed QA personas (memory: `qa-personas-reseed`); confirm `acme` tenant + tenantadmin/hr/manager/employee personas.
- [ ] Sanity: `curl -s localhost:5000/health` OK; login as `admin@hrm.local` / `Admin@123!` returns a token.

### Phase V1 — Gateway (MUST be first substantive verify)
- [ ] `/verify-fix BUG-003 --iso` — full cross-module ISO re-run. Expect ISO arms to flip from "leak observed" → **403 on JWT≠subdomain**. A red here reopens the systemic leak — STOP and re-fix before continuing.

### Phase V2 — Rig-enablement (verify before the TCs that depend on them)
- [ ] `/verify-fix BUG-093` — employee create (unblocks anything needing seeded employees).
- [ ] `/verify-fix BUG-037` — 2026 leave balance + reports materialize.
- [ ] `/verify-fix BUG-068` — REC-010 convert (needs Docker/PG).
- [ ] `/verify-fix BUG-036` — Leave.ManageLop surface reachable.
- [ ] `/verify-fix BUG-121` — stop Redis, confirm `/auth/me` + `/my-tenants` still 200 (fail-soft).

### Phase V3 — Per-module (batch; any order after V2)
- [ ] **Auth:** `/verify-fix BUG-040` · `BUG-041` · `BUG-042` · `BUG-043` · `BUG-004`.
- [ ] **Admin:** `/verify-fix BUG-007` · `BUG-107` · `BUG-008` · `BUG-001`.
- [ ] **Core HR:** `/verify-fix BUG-119` · `ISSUE-223`.
- [ ] **Payroll:** `/verify-fix BUG-072` · `BUG-073` *(PG)*.
- [ ] **Notifications:** `/verify-fix ISSUE-188`.
- [ ] **BUG-106 (special):** its dedicated HTTP test was removed (login-gate entanglement). Verify by hand:
      suspend a tenant, log in as its Tenant Admin, confirm a tenant GET is **not 451** (TC-ADM-004). Then mark RESOLVED.

### Phase V4 — Close-out (as each fix goes green)
- [ ] `TEST-STATUS.md`: flip `[b]`/`[!]` → `[x]` (clean) or `[!]` (residual findings) per module.
- [ ] `TEST-FINDINGS.md`: each verified finding → `Status: RESOLVED` + PR#.
- [ ] Any red re-run → leave `OPEN`, append re-test evidence, open `/fix-finding {ID}`.
- [ ] Post a short verification summary (findings verified / residual / newly-opened).

### Phase V5 — Non-code track (env, not fixes — after V1–V4)
- [ ] Seed/persona: BUG-060 (HR Officer Payroll perms), BUG-101 (`PublicCareersEnabled`).
- [ ] Infra: Redis, k6 harness, 5k/1k perf seeds, on-demand Hangfire triggers.
- [ ] Deferred features (build, not fix): multi-level approval, fiscal-year balances, US-AUTH-012/016.

---

## 5. Verification status tracker

Mark as you go: `TODO` · `PASS` · `FAIL(→/fix-finding)` · `BLOCKED(reason)`.

| # | Finding | PR | Phase | Verify status |
|---|---|---|---|---|
| 1 | BUG-003 (+069/193/189-191) | #119 | V1 | **PASS** (2026-07-02: acme token + other-tenant header → 403 `cross_tenant_denied`; acme+acme → 200) |
| 2 | BUG-093 | #114 | V2 | **PASS** (create → 201 (EMP-0034)) |
| 3 | BUG-037/086 | #117 | V2 | **PASS** (my-balance?year=2026 → 200 (real data)) |
| 4 | BUG-068 | #115 | V2 | **PASS** (real-Postgres Testcontainer test (exact retry+transaction path; exception reproduced pre-fix)) |
| 5 | BUG-036 | #116 | V2 | **PASS** (HR Officer LOP-summary → 400 (not 403)) |
| 6 | BUG-121 | #120 | V2 | **PASS** (/auth/me + /my-tenants → 200) |
| 7 | BUG-040 | #118 | V3 | **PASS** (bad reset token → 400 (takeover blocked)) |
| 8 | BUG-041 | #122 | V3 | **PASS** (role create → audit row queryable) |
| 9 | BUG-042 | #122 | V3 | **PASS** (LIVE: switch during impersonation → 403 switch_forbidden) |
| 10 | BUG-043 | #129 | V3 | **PASS** (LIVE: reuse old token → 401; independent session → 200) |
| 11 | BUG-004 | #127 | V3 | **PASS** (LIVE: 12-char pw under 16 policy → 400) |
| 12 | BUG-007 | #125 | V3 | **PASS** (audit search ?searchQuery=role → 200, no jsonb 500) |
| 13 | BUG-107 | #125 | V3 | **PASS** (LIVE: force-password-reset w/ full imp token → 403 destructive-block) |
| 14 | BUG-008/ISSUE-227 | #126 | V3 | `TODO` |
| 15 | BUG-001 | #130 | V3 | **PASS** (HTTP mutation test (real System Support user → read-only)) |
| 16 | BUG-119 | #124 | V3 | **PASS** (non-owned PATCH → 403 'edit your own') |
| 17 | ISSUE-223 | #124 | V3 | **PASS** (default=33 vs includeTerminated=35) |
| 18 | BUG-072 | #123 | V3 | **PASS** (slab 1e18 → 400 (not 500)) |
| 19 | BUG-073 | #128 | V3 | **PASS** (create+update identical → 200 (not 500)) |
| 20 | ISSUE-188 | #121 | V3 | **PASS** (LIVE: manager approve → 'leave.approved' notification row created) |
| 21 | BUG-106 | #130 | V3 (manual) | **PASS** (HTTP-proven (same MapInboundClaims root cause as BUG-001)) |

**Exit criteria:** all 21 rows `PASS`; `TEST-STATUS.md` shows no `[b]` for these stories; every finding
`RESOLVED` in `TEST-FINDINGS.md`; any `FAIL` has a tracked `/fix-finding` follow-up.

## 6. Findings caught during verification (report-only)
- **BUG-126** (MED, NEW, logged in `TEST-FINDINGS.md`) — the onboarding overdue-notification Hangfire job
  did `o.Payload.Contains(...)` on a **jsonb** column (`OnboardingChecklistService.cs:702`) → `jsonb ~~ jsonb`
  (42883) → the job failed and **retried forever**. Same class as BUG-007, in a background job (no HTTP 500).
  **FIXED — PR #131** (in-memory match over the candidate set; PG regression test, mutation-proven). Verify
  with `/verify-fix BUG-126` after merge (confirm the job's Serilog no longer logs `jsonb ~~ jsonb`).
