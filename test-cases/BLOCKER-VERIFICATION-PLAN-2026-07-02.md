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

### Deferred-cleared (PRs #126–129)
| Finding | PR | Affected TCs (verify) | Command |
|---|---|---|---|
| **BUG-008/ISSUE-227** | #126 | TC-ADM-009-07/-10 (employee cap), TC-ADM-007-05 (workflow cap) | `/verify-fix BUG-008` |
| **BUG-004** | #127 | TC-ADM-006-12, TC-AUTH-012 (tenant password policy on reset) | `/verify-fix BUG-004` |
| **BUG-073** | #128 | TC-PAY-006-02, TC-PAY-007-01 (statutory rule update) | `/verify-fix BUG-073` |
| **BUG-043** | #129 | TC-AUTH-009/005/062 (per-session revocation) | `/verify-fix BUG-043` |

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

## 3. Still-open (NOT verifiable yet — need HTTP repro, then fix, then verify)

- **BUG-001** (impersonation read-only gate) and **BUG-106** (suspended-tenant admin access) remain OPEN.
  Both are "role not detected at runtime." The fix is not written (static paths look correct), so there is
  nothing to `/verify-fix` yet. **Next step is reproduction, not verification:** build an ApiTestFactory
  (Testcontainer) HTTP test that logs in as a **seeded System Support persona**, starts impersonation, and
  asserts `is_read_only=true` (BUG-001) / that a Tenant Admin reaches the suspended read-only notice
  (BUG-106). That repro pins whether the operator's token actually carries the role, then drives the fix.
- Their TCs stay `[b]`/OPEN until then: TC-ADM-003-04/-11 (BUG-001), TC-ADM-004 suspension arm (BUG-106).

---

## 4. Close-out checklist (per finding, done by `/verify-fix`)
- [ ] Merge PRs #114–129.
- [ ] Bring the stack up (§0); reseed personas.
- [ ] `/verify-fix BUG-003 --iso` (gateway) — full cross-module ISO re-run.
- [ ] Run the rest of the §1 matrix (`/verify-fix {ID}` each).
- [ ] Each green fix → `TEST-STATUS.md` flips `[b]`/`[!]`→`[x]`/`[!]`; finding → `RESOLVED` w/ PR#.
- [ ] Any red re-run → leave `OPEN`, append re-test evidence, open a fresh `/fix-finding`.
- [ ] Reproduce + fix + verify **BUG-001** and **BUG-106** (§3).
- [ ] Re-run the non-code track (env/persona/perf-harness) from the remediation plan §6.
