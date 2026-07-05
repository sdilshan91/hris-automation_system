# Fix Plan — QA Findings Remediation (2026-07-04)

> Plan to clear the `TEST-FINDINGS.md` backlog. Drives the **human-decided fix cycle** via
> `/fix-finding {ID}` (fix on a `fix/{ID}` branch + PR) → merge → `/verify-fix {ID}` (re-run TCs, flip
> the ledgers). REPORT-ONLY loop (`/test-all`) never fixes; this plan is the fix side.

## 0. Correction (2026-07-04, after Wave 0 ran)

**My first-pass framing was wrong and I'm correcting it.** I initially claimed ~177 open and that reconciliation
would "close 30–60 for free." That over-counted: my parser only matched the *old* one-liner status format and
missed that the **newer findings were already correctly marked RESOLVED**. A large fix campaign (PRs **#114–#136**,
"Phase A/B/D") had already landed **~38 fixes** and mostly reconciled them. **Wave 0 found only 8 genuinely-stale
entries**, now flipped. The real work is NOT reconciliation — it's the genuine long tail of unfixed MED/LOW defects
plus ~12 HIGH. See "Wave 0 — DONE" below for what actually changed.

The staleness *did* exist (below), just at a much smaller scale than the raw count implied:

| Finding | Ledger reads | Reality (per fix history / memory) |
|---|---|---|
| BUG-040 (CRIT reset-takeover) | text says RESOLVED but carries an *earlier-dated* "STILL PRESENT" note | **RESOLVED PR #118**, verified 2026-07-02 |
| BUG-003 family (10 headers, 4 tagged CRIT) | "EXTENDED … STILL PRESENT" / OPEN | **Root fixed** by `TenantAccessGuardMiddleware` PR #119, ISO re-run verified 2026-07-03 |
| BUG-099 | OPEN | **FIXED** PR #132, E2E-verified 2026-07-04 |
| BUG-127 | fixed (inline) | fixed, but ledger status not normalized |
| BUG-236 / BUG-239 / BUG-240 | no clean status line | 236/239 fixed (#133/#135), 240 resolved (#136) |
| BUG-104 | OPEN | still genuinely open (FE→`/tenant/exports` vs BE `/tenant/data-exports`) |

**Conclusion:** a meaningful fraction of the 177 are already fixed by merged systemic PRs (#118, #119, #130, #132, #133, #135, #136) and were simply never reconciled back into the ledger. **The first wave is not fixing — it is reconciliation.** Doing anything else first means paying to "fix" bugs that are already dead.

---

## Wave 0 — Reconcile the ledger — ✅ DONE 2026-07-04

**Result:** 8 stale entries flipped OPEN→RESOLVED in `TEST-FINDINGS.md`, all backed by a merged PR + prior verification:

| Finding | Was | Now | Basis |
|---|---|---|---|
| BUG-099 | OPEN | RESOLVED | PR #132, E2E-verified 2026-07-04 |
| BUG-020, BUG-021 | HIGH·OPEN | RESOLVED | systemic tenant guard #119, ISO-verified 2026-07-03 |
| ISSUE-026 | HIGH·OPEN | RESOLVED | guard #119 fires pre-controller (import WRITE) |
| BUG-003 (EXT leave-entitlements / holiday / carry-forward) | CRIT·OPEN ×3 | RESOLVED | guard #119, ISO-verified 2026-07-03 |
| BUG-037 (EXT leave reports) | HIGH·OPEN | RESOLVED | PR #117 (same root as BUG-086) |

Summary table in the ledger annotated with a reconciliation note. **Net effect: 0 genuine open CRIT remain; genuine
open HIGH ≈ 12** (BUG-014, BUG-019, BUG-025, BUG-030, BUG-035, BUG-045, BUG-048, BUG-055, BUG-102, BUG-104,
ISSUE-018, ISSUE-210 — all re-confirmed OPEN). Everything else open is MED/LOW.

**Ledger-hygiene follow-ups:** de-duplicate reused IDs — **DONE 2026-07-05** (still-open occurrences renumbered
ISSUE-097→ISSUE-243, ISSUE-105→ISSUE-244, BUG-059→BUG-242; mapping banner at the top of TEST-FINDINGS.md).
`TEST-STATUS.md` per-US re-test flips belong to `/verify-fix`, not this pass.

### (original Wave 0 plan, for reference)

**Goal:** turn nominal-open into an accurate, deduped, genuinely-open backlog.

Steps:
1. **Cross-reference every OPEN finding against merged fix PRs.** Known landed fixes to reconcile: #118 (BUG-040), #119 (BUG-003 systemic guard), #130 (BUG-001/107 impersonation), #132/#133/#135 (FE shape-drift class), #136 (BUG-240). For each, run `/verify-fix {ID}` (use `--iso` for the BUG-003 family — it does a cross-module isolation re-run) and flip to RESOLVED/VERIFIED with the PR#.
2. **Collapse the BUG-003 family.** The 10 `BUG-003 (EXTENDED …)` headers are one root cause, now guarded. Verify each surface (leave-entitlements, holiday calendar, carry-forward, PIP, recruitment dashboard, scorecard, sign-off notes, bulk-import ISSUE-026) against the guard; close as covered, or re-file only the *residual* gaps the guard does **not** catch (e.g. any WRITE surface reached before the guard runs — confirm ISSUE-026 bulk-import is post-guard).
3. **Normalize status lines.** Every finding must have exactly one machine-parseable `Status:` on the `Type / Severity / Status` line, and stale earlier-dated "STILL PRESENT" regression notes below a later RESOLVED must be struck through, not left to confuse the next reader.
4. **Update the Summary table** at the top of `TEST-FINDINGS.md` to the reconciled counts.

**Owner:** `/verify-fix` per ID (writes only to `test-cases/`). Safe, no `src/` edits.
**Exit criterion:** the CRIT/HIGH open list contains only findings that reproduce *today* against a fresh build.

---

## Root-cause clusters (fix once, close many)

After Wave 0, group the survivors. The backlog is dominated by a few systemic classes — fix at the shared boundary, not per-call-site (see `docs/vault/` + memory notes).

| Cluster | Shared root | Findings (representative) | Fix locus |
|---|---|---|---|
| **C1 — Missing audit writes** | handlers log to Serilog but never `WriteAuditLogAsync` → `audit_logs` | BUG-039 (logout), BUG-041 (RBAC), BUG-025 (leave-type), BUG-055 (vacancy), BUG-010 (PII read), ISSUE-048/051/058, ISSUE-188 (producer unwired) — ~61 findings mention audit | A shared audit-write helper + a MediatR `AuditBehavior` or per-command call; batch by module |
| **C2 — FE↔BE contract drift** | FE baseUrl ≠ controller `[Route]`; `ApiResponse<T>` envelope vs bare payload | BUG-104 (`/tenant/exports`), the 19-service `/tenant/` prefix set, envelope-mismatch class | Diff every FE `baseUrl` vs controller route in one sweep; global unwrap interceptor for the envelope |
| **C3 — FE shape/null-guard crashes** | paginated `{items,totalCount}` consumed as array; unguarded null render | mostly landed (#132/#133/#135); verify stragglers | Shared service-boundary adapter + null-safe util (already the pattern) |
| **C4 — Authz / horizontal priv-esc** | `.Own`/`.All` permission checks missing an owner/tenant scope | BUG-119 (Edit.Own no owner check — HIGH), BUG-014 (managerId cross-tenant), BUG-019/020/021 (doc list/download no authz + spoofable subdomain), BUG-035 (team-cal org-derived not permissioned), ISSUE-018 (directory literal-perm) | Per-endpoint owner/tenant assertion; some auto-covered by the BUG-003 guard — re-check after Wave 0 |
| **C5 — Auth robustness** | non-atomic / lifecycle edge bugs | BUG-045 (non-atomic `failed_login_count` → brute-force bypass, HIGH), BUG-043 (revoked-refresh nukes all sessions), BUG-042 (switch-during-impersonation) | DB-level atomic increment; rotation-vs-revocation distinction in refresh reuse detection |
| **C6 — 500s on edge input** | unhandled 23505 / enum / strategy | BUG-048 (whitespace shift-name 500), BUG-093 (employee-create EMP-MGR01 collision), BUG-037/086 (Accrued enum 500 in reports) | Guard the unique-violation → 400; fix `GenerateEmployeeNoAsync` sort; handle the enum |
| **C7 — LOW cosmetic / defense-in-depth** | 404-vs-400 codes, contrast, favicon, missing `aria-*` | ISSUE-001, BUG-096 (contrast, systemic), BUG-238 (docs tablist a11y), favicon-404 | Batch or defer; several are WONTFIX candidates |

---

## Prioritized waves (after Wave 0)

Order = severity × exploitability × blast radius. Security-exploitable first, then broken primary flows, then correctness/audit, then cosmetic.

### Wave 1 — Security & data-integrity (HIGH, exploitable) — do first
- **BUG-045** — non-atomic `failed_login_count` defeats lockout under concurrency → brute-force. DB-atomic `UPDATE … SET count = count + 1`.
- **BUG-119** — `Employee.Edit.Own` has no owner check (horizontal priv-esc; one employee edited another's profile). Add owner assertion.
- **BUG-019 / BUG-020 / BUG-021** — employee-document list/download unauthorized + honor spoofed subdomain. Add authz + confirm BUG-003 guard covers the spoof arm.
- **BUG-014** — department `managerId` not tenant-validated (can point at another tenant's employee).
- **BUG-035** — team-leave-calendar scope org-derived, not permission-gated.
- **BUG-043 / BUG-042** — session survivor-integrity + switch-during-impersonation escape.

### Wave 2 — Broken primary flows (HIGH functional)
- **BUG-102** — "Apply for Leave" dropdown empty (`GET /leaves/balance…` shape) — users can't request leave.
- **BUG-104** — Admin Data-Export UI dead (C2 URL drift).
- **BUG-093** — employee CREATE 100% broken in acme (EMP-MGR01 sort collision).
- **BUG-048 / BUG-037 / BUG-086** — 500s (whitespace name; Accrued enum in 3 reports).

### Wave 3 — Audit-trail cluster (C1, mostly MED) — batch by module
One shared audit-write mechanism, then sweep: Auth (BUG-039/041, ISSUE-048/051/058), Core-HR (BUG-010), Leave (BUG-025), Recruitment (BUG-055), Notifications producer wiring (ISSUE-188). ~30–40 findings collapse into a handful of PRs.

### Wave 4 — Contract-drift sweep (C2) — batch
Single pass diffing all FE service `baseUrl`s vs controller routes + a global `ApiResponse` unwrap interceptor. Closes BUG-104 + the prefix/envelope ISSUE set together.

### Wave 5 — LOW cosmetic / a11y / defense-in-depth (C7) — batch or defer
Contrast (BUG-096 systemic — one fix, many TCs), docs tablist (BUG-238), 404→400 code nits, favicon. Explicitly mark env/harness-blocked and true-cosmetic items **WONTFIX** with a reason rather than carrying them as perpetual open noise.

---

## Execution discipline (per finding / per batch)

1. `/fix-finding {ID}` → cuts `fix/{ID}` from fresh `main`, fixes `src/`, adds a regression TC (`@qa-engineer`), runs `@test-authenticator` / `@integration-enforcer` / `/security-audit` gates, opens a PR. **Never weaken/skip a test to go green** (enforced by `test-integrity-guard`).
2. **Merge the PR before starting the next** finding that touches the same files (avoids stacked colliding PRs — see memory `implement-all-merge-before-next`).
3. `/verify-fix {ID}` **after merge** → re-runs the finding's TCs (`--iso` for isolation), flips `TEST-STATUS.md`, marks the finding RESOLVED with the PR#. This is the *only* skill authorized to close a finding.
4. **Batch clusters onto one branch** where findings share a root + files (C1 per module, C4 per endpoint group, C7 cosmetic). One PR can carry several IDs when the fix is genuinely one change — reference every ID it closes.
5. Tenant isolation is non-negotiable — any fix touching a query/route re-checks the 3-layer isolation and gets a `/security-audit` before PR.

## Sequencing / effort snapshot

| Wave | Nature | Rough size | Risk |
|---|---|---|---|
| 0 Reconcile | ledger only, no `src/` | ~0.5 day, closes 30–60 | none |
| 1 Security | small surgical fixes | 6–8 PRs | must security-audit each |
| 2 Broken flows | targeted | 4–6 PRs | user-visible, verify E2E |
| 3 Audit cluster | 1 mechanism + module sweep | 4–6 batched PRs | wide but mechanical |
| 4 Contract sweep | 1 diff pass + interceptor | 1–2 PRs | verify no FE regressions |
| 5 Cosmetic | batch / WONTFIX triage | 1–2 PRs | low |

## Decisions I need from you before executing
1. **Scope of this effort** — everything down to LOW, or CRIT/HIGH/MED only and defer LOW? (Recommend: CRIT/HIGH/MED now, triage LOW to WONTFIX/backlog.)
2. **Autonomy** — should I run Wave 0 reconciliation now (safe, ledger-only), then stop for your review of the *real* backlog before any `src/` fix? (Recommend: yes.)
3. **Batching appetite** — one-PR-per-finding (cleaner audit trail, more PRs) vs cluster-batched PRs (fewer, faster, but a PR closes several IDs). (Recommend: batch the C1/C4/C7 clusters, one-per-finding for security.)
