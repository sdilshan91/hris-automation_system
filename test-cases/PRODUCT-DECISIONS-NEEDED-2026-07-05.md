# Product Decisions Needed — W1/W2 findings (2026-07-05)

> Phase 3 (triage) buckets **W1 (unbuilt features)** and **W2 (product-decision-needed)** from
> `MEDLOW-TRIAGE-2026-07-05.md`. These are NOT quick bug-fixes — each needs an acceptance-criteria or
> business-rule decision from the product owner before code. Per engineering discipline #1 (ask when unsure)
> and the `/fix-finding` guardrail (stop & surface rather than guess), these were deliberately **not** auto-fixed.
> Answer inline, then they become normal fix-cycle work.

---

## W1 — Unbuilt features (treat as ENH / backlog, not defects)

Each has a data model or a stub but no real implementation. They're missing *capability*, not broken behavior — so they belong in a feature backlog with a spec, not a bug sweep.

| ID | Gap | Decision needed |
|---|---|---|
| **ISSUE-101** | Virus scanning is a no-op stub — every upload (incl. EICAR) passes. | Which scanner? (ClamAV sidecar / cloud AV API / defer). This is a real security gap but needs infra choice + a NuGet/service dependency. |
| **ISSUE-105** (attachment-evidence) | Self-assessment file-attachment evidence has a data model but **no upload/list/download API**. | Confirm the feature is in-scope for this release; if so it's a normal story (endpoints + storage), not a fix. |
| **ISSUE-158** | Payslip PDF omits per-tenant branding (logo, address, colours). | Is per-tenant payslip branding required now? Needs the branding source + PDF template work. |
| **ISSUE-160** | Payslip YTD column permanently disabled; no per-tenant YTD-enable flag. | Add a tenant setting + YTD computation? Or defer. |
| **BUG-063** | Hangfire per-cycle performance-review job scheduling is implemented but **not wired** to fire. | Wire the recurring job now, or is manual triggering acceptable for this release? |
| **ISSUE-118** | BR-5 cycle-level anonymity lock not enforced (HR can flip anonymity mid-cycle). | Confirm the intended rule (lock once cycle opens? once first response arrives?) before enforcing. |
| **ISSUE-102 (CAPTCHA half)** | Rate limiting shipped (#157); CAPTCHA on the public application form not added. | Which CAPTCHA (reCAPTCHA/hCaptcha/none)? Needs a client key + UX decision. |
| **ISSUE-066** | Attendance IP allowlist is exact-string only; no CIDR ranges. | Confirm CIDR support is wanted (small parser change) — low-risk, could move to FIX-NOW if you say yes. |
| **BUG-070** | No automatic CTC balancer — a single component override is rejected unless all components are re-supplied. | Define the intended balancing rule (proportional? residual-to-one-component?) before implementing. |

| **ISSUE-021** | Job-title `gradeId` accepts any GUID with no validation — but there is **no `SalaryGrade` entity/DbSet anywhere** (`JobTitle.GradeId` is intentionally FK-less; the Grade entity "does not exist yet"). Nothing to validate against. | Build the `SalaryGrade` entity (Payroll module) first, then gradeId validation is trivial. Also reconcile the existing `JobTitleServiceTests` that *assert* an arbitrary gradeId succeeds. Deferred from B2a. |
| **BUG-056** | Goal weights "must total exactly 100%" is unenforceable: goals are created one-at-a-time (running **≤100%** cap already enforced, 422); there is **no goal-set submit/finalize seam** where an `== 100%` check belongs. | Add a "submit goal set" endpoint (controller+DTO+handler) that enforces `sum == 100` at finalize. Feature, not a guard. Deferred from B2a. |

**Recommendation:** convert W1 to backlog ENH items. The only one I'd pull forward if you approve: **ISSUE-066 (CIDR allowlist)** — it's a contained parser change, not a feature.

---

## W2 — Business-rule / AC decisions (fix is blocked on intended behavior)

Real defects, but "correct behavior" isn't settled in the story — coding them now would be guessing.

| ID | Ambiguity | Options |
|---|---|---|
| **BUG-029** | Leave approval ignores `negative_balance_limit` — a negative-allowed type lets balance go below the configured floor. | (a) enforce the limit as a hard floor at approval; (b) warn-only. Need the intended semantics of `negative_balance_limit`. |
| **BUG-059 (Hired-terminal)** | "Hired is terminal" not enforced; a Hired applicant can be re-advanced / re-converted. | Confirm: is Hired a hard terminal state (block all transitions) or reversible by HR? |
| **BUG-059 (self-assessment reopen)** | A submitted self-assessment can't be reopened; the manager/HR reopen flow is absent. | Should reopen exist, and who can do it (HR only? within a window?)? |
| **BUG-060** | A Rejected applicant can be advanced FORWARD directly (Rejected→Interview). | Is that allowed (re-consideration) or must it be blocked / require an explicit un-reject step? |
| **ISSUE-086** | BR-8 half-day-leave employees not evaluated against a half-day shift schedule. | Confirm the intended half-day attendance evaluation rule before changing the calc. |
| **ISSUE-090** | `payroll-data`/`reconciliation` auto-generate phantom full-month-absent rows for employees with no data. | Should missing data = absent (current), = excluded, or = a distinct "no-data" state? Payroll-impacting — needs sign-off. |
| **BUG-038** | Absenteeism report threshold is hardcoded (not the configured BR-4 value) and flags incorrectly. | Confirm the threshold source (tenant policy field) + the correct flag rule. |

**Recommendation:** these are a 20-minute review for whoever owns the leave/recruitment/payroll rules. Once answered, they're straightforward fixes (mostly a guard or a config read) and can join a FIX-NOW-style batch.

### W2-new — Upfront-entitlement new-joiner shows 0 balance (surfaced by BUG-030 #144)
**Finding:** after BUG-030 reconciled the leave dashboard `balance` to the authoritative ledger running `balance_after`, a brand-new employee with a configured **upfront** annual entitlement (e.g. `AnnualEntitlement=14`, `AccrualFrequency.Upfront`) but **no Accrual ledger entry yet** now sees **"0 available"** — while the entitlement card still shows 14. Worse: apply-preview and approval read the **same** ledger `balance_after` as the single source of truth, so the employee may be **blocked from applying** for leave they're entitled to.
**This is NOT a BUG-030 defect** — the ledger-truth reconciliation is correct. The gap is upstream: nothing writes the **opening/upfront Accrual ledger entry** for an upfront-accrual leave type at onboarding (or leave-year open).
**Decision needed:** confirm where the upfront Accrual entry should be written (employee onboarding? tenant leave-year rollover job? first login?) and its amount source (the leave type's `AnnualEntitlement`, pro-rated by join date?). Then it's a normal fix (write the ledger entry at that seam). Until then, new joiners on upfront-accrual types have a 0-balance dead-end.

---

## Ledger-hygiene TODO (independent, safe — I can do this without decisions)
Duplicate reused IDs still exist: **ISSUE-097** (vacancy-is_deleted L2195 vs goal-audit L2239 — goal one now RESOLVED), **ISSUE-105** (×2), **BUG-059** (×2). Recommend renumbering the still-open second occurrences to fresh IDs (updating any references) so automated counts are trustworthy. I deferred this during the campaign because renumbering an ID that's already referenced in a merged PR is risky — safe to do now for the unreferenced open ones. Say the word.
