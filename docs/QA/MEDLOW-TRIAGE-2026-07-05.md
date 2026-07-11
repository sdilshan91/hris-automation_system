# MED/LOW Findings Triage (2026-07-05)

> Phase 3 of `FIX-FINDINGS-PLAN-2026-07-04.md`. After Wave 1 (12 HIGH, PRs #137–148) and Phase 2
> (audit-write cluster, 22 findings, PRs #149–154), **0 genuine open CRIT/HIGH remain**. This triages the
> remaining MED/LOW long tail into **FIX-NOW · BATCH · WONTFIX/BACKLOG**, each with a recommendation.
> Counts are approximate. (The former duplicate reused IDs were de-duplicated on 2026-07-05: the still-open
> occurrences became ISSUE-243 / ISSUE-244 / BUG-242 — see the mapping banner in TEST-FINDINGS.md. Some older
> mentions in this triage doc predate the renumber.) "STAYS OPEN" notes still defeat naive automated counting;
> clusters below are by theme, not exhaustive lists.

## Recommendation at a glance

| Bucket | Clusters | Rough size | Effort |
|---|---|---|---|
| **FIX-NOW** | 500-on-edge · security-hardening (rate limits) · ledger-reconcile siblings · permission-mapping · FE render/contract | ~18 findings | small, mechanical — proven patterns |
| **BATCH** | case-insensitive uniqueness · validation gaps · audit refinements · FE nav-orphan | ~25 findings | medium, grouped PRs |
| **WONTFIX / BACKLOG** | unbuilt features · product-decision-needed · data-anomaly investigations · cosmetic nits | ~40+ findings | defer or explicitly close |

---

## FIX-NOW (do these next — small, exploitable, or user-visible; proven patterns)

### F1 — 500-on-edge (unhandled DB/exception) — same class as the shipped BUG-048
- **BUG-047** — concurrent clock-in race → HTTP 500 (unhandled 23505) instead of a graceful conflict. Same 23505 pattern as BUG-048; add the unique-violation catch → 409.
- **BUG-033** — carry-forward preview throws unhandled 500 (`ArgumentOutOfRangeException`) for certain configs. Guard the input.
- **Why now:** a benign input 500s a primary flow; the fix pattern is already in the codebase (BUG-048's `DbUpdateException`/23505 catch).

### F2 — Security hardening: rate limiting on anonymous/credential endpoints
- **ISSUE-052** — forgot-password has no rate limiting (FR-9).
- **ISSUE-102** — public application endpoint has no rate limiting / CAPTCHA (15 rapid anonymous submits accepted).
- **Why now:** unauthenticated abuse surface. Recommend a shared rate-limiter (ASP.NET `RateLimiter` middleware / a per-IP+email policy) covering both. CAPTCHA is a bigger UX change → defer the CAPTCHA half to backlog, ship the rate limit.

### F3 — Ledger-reconcile siblings of the just-shipped BUG-030
- **ISSUE-043** — leave-cancel response `balanceAfter` doesn't reconcile with the ledger running total. **Exact sibling of BUG-030** (just fixed) — apply the same "derive from the authoritative ledger `balance_after`" fix.
- **Why now:** one-line-of-reasoning reuse of a fix already reviewed and merged.

### F4 — FE render / field-contract crashes (same class as shipped BUG-099/102)
- **BUG-101** — carry-forward preview totals render `NaN`, rows show 0 (FE↔BE field mismatch).
- **BUG-103** — Admin → Users pagination footer renders `Showing 1–NaN of {{total}}` (unrendered i18n + count binding).
- **ISSUE-212** — Company Settings branding: invalid-hex error shown via bare `alert()` not inline.
- **Why now:** user-visible breakage; the FE↔BE shape-drift + null-guard pattern (BUG-099/102/239) is well-trodden.

### F5 — Permission-mapping gaps (same class as shipped ISSUE-018)
- **BUG-027** — HR Officer (named persona) can't configure leave entitlements (missing perm on the endpoint).
- **BUG-034** — carry-forward preview gated on `Leave.ConfigurePolicy`, not granted to the intended persona.
- **BUG-050** — Manager persona fully locked out of the attendance dashboard/reports (403).
- **Why now:** these dead-end real personas; the fix is the any-of / correct-perm pattern from ISSUE-018 (verify the intended role holds the perm before widening — do NOT just grant).

---

## BATCH (group into a few PRs — medium effort, shared root)

### B1 — Case-insensitive + trimmed uniqueness (one normalization approach)
- **BUG-013** (department), **BUG-016** (job title), **BUG-017** (office-location), **ISSUE-074** (shift name), **ISSUE-022** (job-title trim, LOW), **ISSUE-028** (custom-field, LOW).
- One PR: normalize the uniqueness checks (trim + case-insensitive via `LOWER()`/`citext` or a normalized-name column) consistently across these entities. Note: BUG-048 already trims shift names — extend to case-insensitivity there too (ISSUE-074).

### B2 — Validation gaps (batch by module)
- **BUG-002** (terminate `graceDays` default), **ISSUE-004** (`status=Invited` filter no-op), **ISSUE-021** (job-title `gradeId` unvalidated), **BUG-005** (localization date-format/timezone unvalidated), **ISSUE-088** (empty period-lock body → garbage lock), **BUG-056** (goal weights must total 100%), **ISSUE-095** (public-careers toggle no-op), **ISSUE-002** (tenant `search=` ignored), **ISSUE-019** (directory sort param-name mismatch).
- Mechanical "add the missing validation / honor the param" fixes; group by module (admin, core-hr, leave, attendance, performance).

### B3 — Audit refinements (follow-on to Phase 2's audit-write cluster)
- **ISSUE-055** (denied tenant-switch not audited), **ISSUE-058** (session-revoke audit attributed to victim not admin), **ISSUE-025** (status-change snapshot in `employee_field_audit` not the main trail), **ISSUE-050/059/062/064** (LOW: missing security-event rows / empty detail / phantom rows).
- These are "wrong content / missing edge rows," distinct from Phase 2's "no row at all." One "audit refinements" PR per module using the now-standard `AuditLogs.Add` helper.

### B4 — FE navigation-orphan (same class as shipped ISSUE-210)
- **ISSUE-208** (attendance sub-pages orphaned from nav), **ISSUE-209** (public careers not reachable/exercisable).
- Apply the ISSUE-210 nav pattern (correct permission/role gate + add the missing menu entries).

---

## WONTFIX / BACKLOG (defer or explicitly close — not quick fixes)

### W1 — Unbuilt features / needs product spec (backlog as ENH, not defects)
- **ISSUE-105** (file-attachment evidence: data model but no API), **ISSUE-158** (payslip per-tenant branding), **ISSUE-160** (YTD column disabled, no per-tenant flag), **BUG-063** (Hangfire per-cycle scheduling unwired), **ISSUE-118** (cycle-level anonymity lock), **ISSUE-101** (virus scanning is a no-op stub — needs a real scanner integration), **ISSUE-102** CAPTCHA half, **ISSUE-066** (IP allowlist CIDR support), **BUG-070** (automatic CTC balancer).
- **Recommendation:** these are feature work, not regressions. Convert to backlog/ENH items with product sign-off; don't fold into a bug-fix sweep.

### W2 — Product-decision-needed (surface, don't guess)
- **BUG-029** (leave approval + `negative_balance_limit` semantics), **BUG-059** ("Hired is terminal" enforcement) + **BUG-242** (self-assessment reopen flow, formerly the second BUG-059), **BUG-060** (Rejected→forward transition policy), **ISSUE-086** (half-day-leave vs half-day-shift evaluation), **ISSUE-090** (phantom full-month-absent auto-generation), **BUG-038** (absenteeism threshold hardcoded).
- **Recommendation:** each needs an AC/business-rule decision before coding. Batch a short "product questions" doc for the owner.

### W3 — Data-anomaly investigations (not code-clearable yet)
- **ISSUE-243** (formerly ISSUE-097 — 24/26 acme vacancies `is_deleted=true`, cause unpinned, LOW confidence — needs a repro), **BUG-058** (resume MIME content-sniffing — real but needs a magic-byte check lib decision).

### W4 — Cosmetic / defense-in-depth nits (LOW — batch cheaply or WONTFIX)
- **BUG-044** (threshold lockout message one request late), **ISSUE-056** (my-tenants cache invalidation), **BUG-006** (workflow restore edge), **ISSUE-057/060/061/063/064** and the bulk of the ~75 LOW (404-vs-400 codes, favicon-404, WCAG contrast **BUG-096** systemic, empty audit-summary fields, `created_by` email-vs-UUID **ISSUE-015**).
- **Recommendation:** one cheap "cosmetic/contract-nit" PR for the handful that are one-liners (status codes, favicon, contrast token); explicitly **WONTFIX** the rest with a reason so they stop reading as open.

---

## Suggested execution order (if you greenlight)
1. **FIX-NOW** (F1–F5, ~18 findings) — highest value/effort ratio, all proven patterns → ~4–6 batched PRs.
2. **BATCH B1–B4** (~25 findings) → ~4 PRs.
3. **W1/W2** → a product-questions doc for sign-off (no code yet).
4. **W4** → one cosmetic sweep + a WONTFIX pass to clean the ledger.

## Ledger-hygiene TODO (independent, cheap, high-signal)
De-duplicate the reused IDs — **DONE 2026-07-05** (ISSUE-097→ISSUE-243, ISSUE-105→ISSUE-244, BUG-059→BUG-242 for the still-open occurrences; see the mapping banner in TEST-FINDINGS.md). Still outstanding: strip stale "STAYS OPEN" phrasing from RESOLVED notes so automated counts become fully trustworthy.
