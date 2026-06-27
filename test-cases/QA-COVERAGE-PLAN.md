# QA Coverage Plan — Full Testing (prioritized)

> Created 2026-06-27. Companion to [TEST-STATUS.md](TEST-STATUS.md) (per-story state) and
> [TEST-FINDINGS.md](TEST-FINDINGS.md) (defect ledger). **Report-only** — executing test cases and
> logging findings; never fixes code. Tracks the remaining full-testing campaign by phase.

## Reality check
- **Modules 1–8** (auth, core-hr, leave, attendance, recruitment, payroll, performance, admin):
  all executed at least once — `[!]` (findings logged) / a few `[x]` clean. ~70 stories.
- **Zero coverage (16 stories):** Onboarding (6), Notifications & Audit (5), Reports & Analytics (5).
- **SSO epic (US-AUTH-011..016):** shipped in PR #112, **not yet in the tracker**; only AC-1/2/5/7 of
  US-AUTH-011 live-verified (2026-06-26). See [authentication/SSO-EPIC-STATUS-AND-TODO.md](../user-stories/authentication/SSO-EPIC-STATUS-AND-TODO.md).
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

## Recommended sequence
**P0 SSO → P1 (Notifications → Reports → Onboarding) → [fix cycle] → P2 regression.**

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

## Status: P1 + P2 COMPLETE (2026-06-27)
All 16 zero-coverage stories tested + all 8 modules-1–8 regression re-verified. Only P0 SSO
(US-AUTH-015 FE + 013/014 interactive-login happy-path) remains. **Top fix: BUG-003** (systemic
cross-tenant via `X-Tenant-Subdomain`, root US-AUTH-007). Other priority new/CRIT: **BUG-093** (employee
create + bulk import broken), **BUG-068** (convert-to-employee broken on Postgres).
