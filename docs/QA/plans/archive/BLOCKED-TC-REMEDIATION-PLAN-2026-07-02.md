# Blocked-TC Remediation Plan — Clear by Code Change

**Created:** 2026-07-02
**Owner:** human-decided fix cycle (testing loop is REPORT-ONLY — this plan is executed via the new `/fix-finding` + `/verify-fix` skills, see §4b; NOT via `/test-all`)
**Sources:** `docs/QA/TEST-STATUS.md` + `docs/QA/TEST-FINDINGS.md` (OPEN findings only)
**Confidence on TC counts:** ~70% — ledger cross-refs are uneven; treat counts as order-of-magnitude until verified.

---

## 0. Purpose & scope

Map every **code-clearable** blocked test case to the code change that unblocks it, prioritized by
blast radius + severity, and sequenced into phases with dependencies.

> ⚠️ **Not everything blocked is code-blocked.** A large share of `[b]` TCs are gated by env / persona /
> perf-harness / deferred-feature gaps. No `src/` change clears those — see [§6 Non-code track](#6-non-code-track-will-not-clear-via-code).

**Status legend (this doc):**

| Mark | Meaning |
|------|---------|
| `TODO` | not started |
| `WIP` | in progress (branch cut) |
| `PR` | PR open, awaiting merge |
| `DONE` | merged + affected TCs re-run green |
| `BLOCKED` | fix itself blocked (dependency/decision) |

---

## 1. Priority model

- **P0 — Gateway:** one fix cascades across many TCs and/or is security-critical. Do first.
- **P1 — Module-gating:** unblocks a whole story / surface.
- **P2 — Localized:** clears a handful of TCs in one place.

---

## 2. P0 — Gateway fixes

| # | Finding(s) | Sev | Area | File / locus | Code change | TCs cleared (approx) | Status |
|---|---|---|---|---|---|---|---|
| P0-1 | **BUG-003** + BUG-069, ISSUE-193, ISSUE-189/190/191 | CRIT | Cross-cutting (Reports, Notif, Perf, Payroll, Leave, Admin, CHR) | `TenantResolutionMiddleware.cs:56-146` | After auth, assert `_currentUser.TenantId == _tenantContext.TenantId`; reject 403 on mismatch. **Keep** the `X-Tenant-Subdomain` dev fallback (pre-auth); only add the post-auth invariant. | ~30–40 (all ISO arms + report/dashboard leaks) | `WIP` — new `TenantAccessGuardMiddleware` (post-auth, resolution untouched) on `fix/BUG-003`; unit regression test, mutation-proven. Dev header fallback preserved. **Full ISO re-run = `/verify-fix --iso` (deferred, needs live stack)**. PR pending. |
| P0-2 | **BUG-093** | HIGH | Core HR | `EmployeeService.cs:770-788` | Employee-no generator sorts lexicographically → parse-fail collides `EMP-0001` → 500. Extract numeric suffix, sort numerically (or move to a DB sequence). | TC-CHR-065/066/080/010 + downstream (anything needing a seeded employee) | `WIP` — fix + regression TC written, mutation-proven, local `dotnet test` green 29/29 (2026-07-02). PR not opened; `/verify-fix` pending live stack. |
| P0-3 | **BUG-040** | CRIT | Auth | `AuthService.cs:439-522` | Reset-token flow is a stub accepting any non-empty token (account takeover). Implement hashed, single-use, expiring token. | TC-AUTH-011/012 | `WIP` — SHA-256 hashed, 1h-expiry, single-use, constant-time token on `fix/BUG-040` (+EF migration, +User columns); 6 regression tests, mutation-proven (all fail pre-fix). PR/verify pending. |

**Rationale:** P0-1 is the systemic BUG-003 hole behind nearly every module's isolation failure. P0-2 blocks
realistic-tenant employee creation → gates Core HR *and* anything needing employees to test. P0-3 is an open
account-takeover.

> ⚠️ **Risk — P0-1** is the highest blast-radius change in the repo. Wrong = either break the dev header
> fallback (local test rig stops resolving tenants) or leave the hole open. Own PR + full ISO re-run, not a drive-by.

---

## 3. P1 — Module-gating fixes

| # | Finding(s) | Sev | Area | File / locus | Code change | TCs cleared | Status |
|---|---|---|---|---|---|---|---|
| P1-4 | **BUG-037** + **BUG-086** | HIGH | Leave / Reports | `leave_ledger.entry_type` enum + materialization | Bad row `entry_type='Accrued'` vs enum `Accrual` → 500 on all 2026 balance/report reads. Backfill row **and** harden enum parsing. | US-LV-006/010/011(BR-1)/012, US-RPT-002, TC-232–237/246–247 (~15–20) | `WIP` — tolerant value-converter (LeaveLedgerConfiguration) on `fix/BUG-037`; PG-Testcontainer regression test, mutation-proven; local green. Data-backfill migration = optional follow-up (converter neutralizes the 500). PR/verify pending. |
| P1-5 | **BUG-068 (REC)** | CRIT | Recruitment | `ApplicantConversionService.cs:153-156` | Manual `BeginTransactionAsync` conflicts with EF retry strategy on Postgres → convert 500. Wrap in `CreateExecutionStrategy().ExecuteAsync(...)`. | All US-REC-010 convert TCs (~11) | `WIP` — `CreateExecutionStrategy().ExecuteAsync` wrap on `fix/BUG-068`; PG-Testcontainer regression test with retry, mutation-proven (exact InvalidOperationException); local green. PR/verify pending. |
| P1-6 | **BUG-036** | HIGH | Leave | `DefaultPermissionsFor` | `Leave.ManageLop` granted only to TenantOwner. Grant to TenantAdmin/HRManager/HROfficer. | US-LV-011 LOP surface (~11; some also need attendance/payroll) | `WIP` — granted to the 3 HR roles on `fix/BUG-036`; existing tenants self-heal via startup reconcile (no backfill); unit regression test; local green. PR/verify pending. |
| P1-7 | **BUG-121** | HIGH | Auth / Admin | `AuthService.GetMyTenantsAsync` + config | Redis outage throws on `/auth/me`, `/my-tenants` → SPA can't hydrate. Add DB fallback / fail-soft. (Code fallback is the durable fix vs config-only.) | ~11+ Auth/Admin FE TCs | `WIP` — fail-soft cache read/write on `fix/BUG-121`, mutation-proven; **pushed, PR #120**. Verify pending. |
| P1-8 | **ISSUE-188** | HIGH | Notifications | `INotificationDispatcher` producers | Approval-notification producer never wired (only export producer exists). Wire leave-approval → dispatch. | US-NTF-001 approval arms (~4–8) | `TODO` — **scoped**: `LogOnlyLeaveNotificationService` is a log-only stub; real fix = new `ILeaveNotificationService` impl calling `INotificationService.CreateAndDispatchAsync` (resolve employee→user) for all 5 events + DI swap. Not started. |

---

## 4. P2 — Localized fixes (by area)

### Auth
| # | Finding | Sev | Locus | Change | TCs | Status |
|---|---|---|---|---|---|---|
| P2-9 | BUG-041 | HIGH | `RoleService.cs:115/171/203/287` | Wire `IAuditService` on role create/update/delete/assign (remove Serilog-only stub). | TC-AUTH-050/039/040 | `TODO` |
| P2-10 | BUG-042 | HIGH | `AuthService.SwitchTenantAsync:674-779` | Block tenant-switch during impersonation (403). | TC-AUTH-062 | `TODO` |
| P2-11 | BUG-043 | HIGH | `AuthService` (ChainRevoke) | Refresh-reuse revokes ALL sessions → scope revoke to the reused token chain / add `revocation_reason`. | TC-AUTH-009/005/062 | `TODO` |

### Admin Console
| # | Finding | Sev | Locus | Change | TCs | Status |
|---|---|---|---|---|---|---|
| P2-12 | BUG-001 | HIGH | `ImpersonationService.cs:118-119` | SystemSupport role not detected at runtime → read-only gate bypassed. Ensure roles populated in MediatR scope; assert read-only. | TC-ADM-003-04/-11 | `TODO` |
| P2-13 | BUG-004 | HIGH | `ResetPasswordValidator.cs:21-27` + `AuthService.cs:473-522` | Load real tenant `PasswordPolicy` instead of hardcoded rules. | TC-ADM-006-12, TC-AUTH-012 | `TODO` |
| P2-14 | BUG-007 | HIGH | `AuditLogService.cs:212-219` | `string.Contains` on jsonb → Postgres 500. Search structured columns / FTS instead. | TC-ADM-008-07 | `TODO` |
| P2-15 | BUG-008 + **ISSUE-227** | HIGH/MED | `PlanLimitResolver.cs` (dead) + `EmployeeService.cs:745-763` | Call `PlanLimitResolver.Resolve()` (plan + overrides) instead of `MaxEmployees`/`MaxWorkflows` snapshot. | TC-ADM-009-07/-10, TC-ADM-007-05 | `TODO` |
| P2-16 | BUG-106 | HIGH | `SuspendedTenantMiddleware` | 451 on all endpoints → exempt read-only notice/export GETs. | TC-ADM-004 (suspension) | `TODO` |
| P2-17 | BUG-107 | HIGH | `ImpersonationReadOnlyBehavior` | Blocklist missing ForcePasswordReset/DeactivateUser/Assign+EditUserRoles → add them (403). | TC-ADM-003 (destructive-op) | `TODO` |

### Payroll
| # | Finding | Sev | Locus | Change | TCs | Status |
|---|---|---|---|---|---|---|
| P2-18 | BUG-072 | HIGH | `StatutoryRuleValidator` / `PayrollAdjustmentService` | Add upper-bound validator on monetary/numeric(18,2) fields → 400 not 500. | TC-PAY-006-01/-05 | `TODO` |
| P2-19 | BUG-073 | HIGH | `StatutoryRuleService.cs:141` | Update 500 on RowVersion mismatch → add concurrency token, return 409. | TC-PAY-006-02, TC-PAY-007-01 | `TODO` |

### Core HR
| # | Finding | Sev | Locus | Change | TCs | Status |
|---|---|---|---|---|---|---|
| P2-20 | BUG-119 | HIGH | `Employee.Edit.Own` enforcement | No owner check → horizontal priv-esc. Assert actor == subject. | TC-CHR-123 | `TODO` |
| P2-21 | ISSUE-223 | MED | Employee model | No soft-delete → Terminated still in default list. Add `is_deleted`/activeOnly filter. | TC-CHR-125 | `TODO` |

---

## 4b. Tooling readiness (PREREQUISITE — build before Phase A)

Research verdict (2026-07-02): **agents are complete — no agent changes needed.** `@backend-dev`
(code + `dotnet ef` migrations + Infrastructure/Persistence seed edits), `@frontend-dev`,
`/security-audit` (fix-diff review), `@integration-enforcer` + `@test-authenticator` (post-fix wiring +
anti-test-theater), `/fault-diagnosis` + `/error-recovery` (fix-side governance) all cover the work.

**Skills have a structural gap:** every existing driver is *story-driven* (`/implement-story`,
`/implement-all` accept only `US-###` and read `docs/BA/STATUS.md`); this plan is *finding-driven*.
`/test-us`/`/test-all`/`@test-runner` are strictly REPORT-ONLY and only ever write `OPEN` — none may
close a finding or flip a `[b]` row back for re-test. **Decision: build two new fix-side skills.**

### New skill 1 — `/fix-finding {BUG-###|ISSUE-###}` (fix driver)
- **Input:** a finding ID (or a comma list, or a plan phase like `P0`). Reads `docs/QA/TEST-FINDINGS.md`
  for root cause + `file:line` + affected TCs.
- **Does:** cut `fix/{ID}-{slug}` from fresh `main`; dispatch the owning dev agent (`@backend-dev`/
  `@frontend-dev`) with the verbatim finding; **dispatch `@qa-engineer` to add/strengthen a regression
  TC that would have caught the bug** (see §4c) and `@test-authenticator` to confirm it isn't theater;
  run the verify gate (build/test); run `/security-audit` on the diff for security findings (P0-1/P0-3)
  and `@integration-enforcer` for wiring (P0-1); open a PR `fix({ID})`. **One finding per call**; honours
  `/error-recovery` 3-attempt cap; never weakens a test.
- **Boundary:** this is the human-decided FIX process — it is explicitly **outside** the report-only test
  loop. It edits `src/`; it does **not** touch the ledgers (that's `/verify-fix`'s job, post-merge).

### New skill 2 — `/verify-fix {BUG-###|ISSUE-###}` (close-out)
- **Input:** a finding ID whose fix PR has **merged**.
- **Does:** flip the affected `TEST-STATUS.md` rows `[b]`/`[!]` → `[ ]`; re-run the finding's affected TCs
  via `@test-runner` (needs a TC-scoped invocation — see gap note); on green, flip `TEST-STATUS` to
  `[x]`/`[!]` and mark the finding **RESOLVED** in `TEST-FINDINGS.md` with the fixing PR#; on red, leave
  `OPEN` and append the re-test evidence.
- **Report-only reconciliation:** `@test-runner` stays report-only and keeps writing only `OPEN`. The
  `RESOLVED` transition is performed by `/verify-fix` itself (the fix/close-out process), NOT by the
  test-runner — this preserves the "testing loop never closes its own findings" rule while giving the
  human-driven fix cycle an authorized close-out path.
- **New capability required:** TC-scoped / finding-scoped re-run (today `@test-runner` is story-scoped)
  and a cross-module ISO-suite re-run mode for BUG-003. Add these as options, don't fork the agent.

> ⚠️ **Guard-hook check:** `/verify-fix` writes to `docs/QA/` (allowed) but must never edit `*.spec.ts`
> / `*Tests.cs` — the `test-integrity-guard` hook still applies. `/fix-finding` edits `src/` under the
> `secret-guard` hook. Neither skill may weaken/skip a test to go green.

---

## 4c. User-story & test-case updates

**User stories (`docs/BA/`) — mostly untouched.** A bug fix restores conformance to an existing AC,
so no US edit. **Exceptions = spec decisions, not bugs** — resolve the product question BEFORE fixing, or
the dev agent guesses:

| Finding | US/AC to clarify | Decision needed |
|---|---|---|
| **ISSUE-223** | Core HR employee lifecycle | Is soft-delete/`Archived` the intended model? Should Terminated leave the default list? |
| **ISSUE-227 / BUG-008** | US-ADM-007 / US-ADM-009 | Write the intended limit precedence (override > plan > snapshot) into the AC so it's verifiable. |

*(US-AUTH-012/016, multi-level approval, fiscal-year already have USs — they're **builds** in the non-code
track §6, not US edits.)*

**Test cases (`docs/QA/`) — two moves per fix:**
1. **Existing blocked/failed TC → just flips status** (draft/fail/blocked → pass) after `/verify-fix`. Bulk case, no content change.
2. **Add a regression TC that would have caught the bug** (mandatory — satisfies the `@test-authenticator`
   anti-"happy-path-only" rule; done by `@qa-engineer` inside `/fix-finding`). Known coverage holes:

   | Fix | Regression TC to add |
   |---|---|
   | BUG-093 | Seed a **non-numeric** employee_no (`EMP-MGR01`) then create → expect success, no 500. |
   | BUG-037/086 | Seed a real `Accrual` ledger row + `my-balance?year=2026` → 200, correct balance. |
   | BUG-003 | Strengthen ISO arms to assert **403 on JWT≠header mismatch** (today they assert the leak — invert after fix). |
   | BUG-040 | Expired / already-used / malformed reset-token arms → all rejected. |
   | BUG-107 | One arm per blocked action (ForcePasswordReset/DeactivateUser/Assign+EditUserRoles) → 403 during impersonation. |

> Owners: `@qa-engineer` (TCs), `@business-analyst` (US/AC edits), `@test-authenticator` (audits the new TC).
> All existing — no new agent. `test-integrity-guard` still forbids weakening any test.

---

## 5. Phases & sequencing

**Rule:** merge each PR before the next (loop-stacking gotcha); re-run affected TCs via `/verify-fix` after each merge.

### Phase 0 — Tooling readiness (prerequisite, see §4b) — ✅ built 2026-07-02 (smoke-test pending)
- [x] `/fix-finding` skill (finding-driven fix driver) — `.claude/skills/fix-finding.md`.
- [x] `/verify-fix` skill (close-out: re-run affected TCs + flip TEST-STATUS + mark RESOLVED) — `.claude/skills/verify-fix.md`.
- [x] TC-scoped + ISO-suite re-run options on `@test-runner` (report-only preserved).
- [x] Registered in CLAUDE.md.
- [x] Smoke-test the pair on one low-risk fix (P0-2 BUG-093) — fix + regression TC written, mutation-proven, `dotnet test` green 29/29 (2026-07-02). `/fix-finding` code+verify flow validated; `/verify-fix` (live TC re-run) still needs the stack.

### Phase A — Unblock the test rig (low-risk, high enablement)
- P0-2 (BUG-093 employee-create) — lets other modules seed employees.
- P1-4 (BUG-037/086 leave enum) — unblocks 2026 balance/report reads.
- P1-5 (BUG-068 REC convert) — independent file.
- P1-6 (BUG-036 LOP perms) — independent (permission seed).
> Parallelizable: non-overlapping files. Cut separate branches.

### Phase B — Security gateway (isolated, needs full re-run)
- P0-3 (BUG-040 reset-token).
- P0-1 (BUG-003 tenant invariant) — **own PR**, then re-run the entire ISO suite across all modules.

### Phase C — FE hydration + notifications
- P1-7 (BUG-121 Redis fallback) — unblocks Auth/Admin FE TCs.
- P1-8 (ISSUE-188 approval producer).

### Phase D — Localized module cleanup (batch by area)
- Auth: P2-9, P2-10, P2-11.
- Admin: P2-12 … P2-17.
- Payroll: P2-18, P2-19.
- Core HR: P2-20, P2-21.

---

## 6. Non-code track (will NOT clear via code)

Fix these in fixtures/env/build — a `src/` change won't move them off `[b]`:

- **Seed / persona:** BUG-060 (HR Officer lacks Payroll perms), BUG-101 (`PublicCareersEnabled` off).
- **Infra / env:** Redis off, no k6 harness, missing 5k/1k-row perf seeds, FE pinned to `:4200`, Hangfire jobs not on-demand-triggerable.
- **Deferred features (build, not fix):** multi-level approval, fiscal-year balances, US-AUTH-012/016 (DB-persisted SSO config, enforcement/break-glass).
- **Interactive dep:** US-AUTH-011/013/014 need real Entra sign-in or a mock IdP.
- **Ledger hygiene:** **BUG-068 is a duplicate ID** (Performance vs Recruitment) — renumber one (e.g. REC → BUG-094) before dispatching fixes, or cross-refs cross-wire.

---

## 7. Coverage rollup

| Priority | Code fixes | Approx TCs cleared | Areas |
|---|---|---|---|
| P0 | 3 | ~40–55 | All (isolation), Core HR, Auth |
| P1 | 5 | ~50–60 | Leave, Reports, Recruitment, Auth/Admin, Notifications |
| P2 | 13 | ~25–30 | Auth, Admin, Payroll, Core HR |
| **Total** | **~21 fixes** | **~115–140 TCs** | 8 modules |

**Highest leverage:** P0-1 (BUG-003), P1-4 (BUG-037/086), P0-2 (BUG-093) — three PRs cover the majority of code-clearable blockage.

---

## 8. TODO checklist

### Phase 0 — tooling (do FIRST; see §4b) — **status: DONE except smoke-test**
- [x] Author `.claude/skills/fix-finding.md` — input `BUG-###|ISSUE-###`, reads TEST-FINDINGS.md, cuts `fix/{ID}`, dispatches dev agent + `@qa-engineer` (regression TC) + `@test-authenticator`, runs verify gate + `/security-audit` + `@integration-enforcer`, opens PR. Edits `src/` only; no ledger writes. *(authored 2026-07-02)*
- [x] Author `.claude/skills/verify-fix.md` — input merged `BUG-###` (`--iso` for isolation suite), re-runs affected TCs, flips TEST-STATUS, marks finding RESOLVED w/ PR#. Writes `docs/QA/` only; performs the RESOLVED transition (NOT test-runner). *(authored 2026-07-02)*
- [x] Add TC-scoped + cross-module ISO-suite re-run options to `@test-runner` (kept report-only; only writes OPEN). *(added "Invocation scopes" section 2026-07-02)*
- [x] Register both skills in CLAUDE.md Skills table. Hook note: `test-integrity-guard` targets `*.spec.ts`/`*Tests.cs` + TC removal — it does NOT block TC `status:`-frontmatter / ledger `.md` edits, so `/verify-fix` is unaffected; `/fix-finding` edits `src/` under `secret-guard` (secrets in `.env`/user-secrets only).
- [ ] Smoke-test `/fix-finding` + `/verify-fix` on P0-2 (BUG-093) — **requires the running stack** (API :5000 + FE :4200 + Postgres). Pending.

### Pre-flight
- [ ] Renumber duplicate **BUG-068** (REC → BUG-094) and update all TC cross-refs.
- [ ] Confirm clean working tree on `main`; each fix on its own `fix/{ID}` branch.
- [x] **Product decision — ISSUE-223:** RESOLVED 2026-07-02 → **add Archived/soft-delete + exclude Terminated from the default list** (explicit filter to include). Update Core HR US/AC when implementing.
- [x] **Product decision — ISSUE-227/BUG-008:** RESOLVED 2026-07-02 → precedence **override > plan > snapshot** (use the existing PlanLimitResolver). Write into US-ADM-007/009 AC when implementing.
- [ ] (Optional) Verify exact TC counts per finding to replace ~approx numbers with hard lists.

### Per-fix TC obligation (enforced inside `/fix-finding`)
- [ ] Every fix ships a regression TC that fails pre-fix / passes post-fix (see §4c table); `@test-authenticator` confirms it's not theater.
- [ ] After fix: existing blocked/failed TCs flip via `/verify-fix` (no content edit); new regression TC committed with the fix PR.

### Phase A — rig enablement
- [~] P0-2 BUG-093 employee-no numeric sort → **fix + regression TC, local green 29/29, mutation-proven** on `fix/BUG-093` (2026-07-02). Pending: PR + merge + `/verify-fix`.
- [~] P1-4 BUG-037/086 leave enum parse-harden → **tolerant converter + PG regression test, mutation-proven** on `fix/BUG-037`. Data backfill = optional follow-up. Pending: PR + merge + re-run US-LV-006/010/012, US-RPT-002.
- [~] P1-5 BUG-068(REC) convert ExecutionStrategy → **fix + PG-retry regression test, mutation-proven** on `fix/BUG-068`. Pending: PR + merge + re-run US-REC-010.
- [~] P1-6 BUG-036 grant Leave.ManageLop → **fix + unit regression test, startup self-heal** on `fix/BUG-036`. Pending: PR + merge + re-run US-LV-011 surface.

### Phase B — security gateway
- [~] P0-3 BUG-040 reset-token flow → **hashed/expiring/single-use token + migration + 6 mutation-proven tests** on `fix/BUG-040`. Pending: PR + merge + re-run TC-AUTH-011/012.
- [~] P0-1 BUG-003 post-auth tenant invariant (own PR) → **TenantAccessGuardMiddleware + mutation-proven test** on `fix/BUG-003`; dev header fallback preserved. Pending: PR + merge + **full ISO suite re-run all modules** (`/verify-fix --iso`).

### Phase C — hydration + notifications
- [ ] P1-7 BUG-121 Redis fallback → merge → re-run Auth/Admin FE TCs.
- [ ] P1-8 ISSUE-188 wire approval producer → merge → re-run US-NTF-001.

### Phase D — localized cleanup
- [~] Auth: **P2-9 BUG-041 ✅ + P2-10 BUG-042 ✅** (fix/phase-d-auth, PR #122, mutation-proven). **P2-11 BUG-043 DEFERRED** — refresh-token reuse-revocation scoping is a security-sensitive redesign of the rotation model (needs chain-parent tracking / revocation_reason); do as a focused pass.
- [~] Admin (PR #125): **P2-14 BUG-007 ✅ + P2-17 BUG-107 ✅** (mutation-proven). **Deferred:** P2-12 BUG-001 + P2-16 BUG-106 (runtime role-claim detection — need live repro), P2-15 BUG-008/ISSUE-227 (needs SubscriptionPlan-field + override-key mapping), P2-13 BUG-004 (tenant password-policy model).
- [~] Payroll (PR #123): **P2-18 BUG-072 ✅**. **Deferred:** P2-19 BUG-073 (no concurrency token exists — finding hypothesis disproven; needs live repro).
- [x] Core HR (PR #124): **P2-20 BUG-119 ✅ + P2-21 ISSUE-223 ✅** (Archived + exclude Terminated).

### Deferred backlog — 4 of 6 now CLEARED
- [x] **BUG-043** (Auth) — reuse revocation scoped to the compromised lineage (ReplacedByTokenId chain). **PR #129**, mutation-proven.
- [x] **BUG-073** (Payroll) — root-caused via Postgres repro: rebuilt child slabs assigned to a tracked parent were tracked as MODIFIED (`UPDATE ... WHERE id=<new id>` → 0 rows). Fixed with explicit AddRange. **PR #128** (RowVersion hypothesis disproven).
- [x] **BUG-008 / ISSUE-227** (Admin) — PlanLimitResolver wired (override>plan>snapshot) for employee + workflow caps. **PR #126**.
- [x] **BUG-004** (Admin) — tenant PasswordPolicy enforced on reset via the existing validator. **PR #127**.
- [x] **BUG-001 + BUG-106** (Admin) — CLEARED via HTTP repro. **PR #130**. Root cause: the JWT bearer default `MapInboundClaims=true` remapped the `"roles"` claim to `ClaimTypes.Role`, so `ICurrentUser.Roles` was empty (while `"permissions"` worked) — breaking every role-NAME gate. Fixed with `MapInboundClaims=false`. Mutation-proven by a real System-Support HTTP test (the old unit test had MOCKED `ICurrentUser.Roles`, hiding it). **All 6 deferred blockers are now cleared — 0 remain.**

### Close-out (per fix, via `/verify-fix` — automates the two manual steps below)
- [ ] Run `/verify-fix {ID}` after each merge → re-runs affected TCs, flips `TEST-STATUS.md` (`[b]`→`[x]`/`[!]`), marks the finding RESOLVED in `TEST-FINDINGS.md` with PR#.
- [ ] After P0-1 (BUG-003): run `/verify-fix` in ISO-suite mode (full cross-module isolation re-run).
- [ ] Re-run the non-code track (§6) only after its env/seed/feature work lands.
