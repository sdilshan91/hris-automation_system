# ARCHIVED SNAPSHOT — 'LIVE QUEUE' generation of 2026-08-03.

> Split out of [`../COMPLETION-PLAN.md`](../COMPLETION-PLAN.md) on **2026-09-01**, when the plan was
> audited and rebuilt. It carried five overlapping sections that each claimed to be 'the queue';
> this is one of them, preserved verbatim as history.
>
> **Not current. Do not execute from this file.** The live execution lane is
> [`../GAP-CLOSURE-QUEUE.md`](../GAP-CLOSURE-QUEUE.md); the current backlog is
> [`../COMPLETION-PLAN.md`](../COMPLETION-PLAN.md).

---

## 🔴 LIVE QUEUE (2026-08-03) — reconciled against code, nothing softened

> **Re-verified 2026-08-03 (P1 reconciliation).** The previous queue (2026-07-30) listed 12 MED items that are
> now shipped. Every row below was checked against the code, not against the ledger. `TEST-FINDINGS.md` went
> **47 OPEN → 14**; 31 rows were flipped with per-row evidence. Severities are as-filed except where a code
> check justified a change, which is called out inline.

### HIGH — blocked on a decision, not on build work
| # | Item | State |
|---|------|-------|
| 1 | **[[BUG-291]]** `AccrualFrequency` over-credit | **Code side DONE** (frequency-aware accrual + read-only exposure report). Stays OPEN only until HR/Finance settle the **per-employee correction policy** — correcting balances downward is employee-detriment, decided case-by-case. **Nothing to build; this needs an answer.** |
| 2 | **[[BUG-293]] retroactive tail** | The fix is merged (unpaid leave now deducts). Whether **past underpaid periods** are corrected is the same shape of unmade decision as BUG-291. Pair these two when you take them to the business. |

### MED — real defects
| # | Item | Note |
|---|------|------|
| 3 | **[[ISSUE-359]]** no stored file is encrypted at rest | **NET-NEW 2026-08-03**, supersedes the offer-scoped [[ISSUE-125]]. Payslip PDFs (salary), employee documents (PII) and offer letters are all plaintext on disk — `IFieldProtector` covers DB columns only. Access control IS sound; this is strictly bytes-at-rest. **Needs its own story:** key-management + back-fill decisions first. |
| 4 | **[[ISSUE-197]]** CTC employer contributions 0.00 | Root cause was filed wrong (they ARE computed, from a proxy source). **Blocked on a prerequisite found in P3:** the resolver has no caching, so the correct fix needs a **batch resolver API** first — calling it per employee would fire 5k rule queries, i.e. create an [[ISSUE-284]]. |
| 5 | **[[ISSUE-284]]** Leave-accrual N+1 | Verified still present. **DEFERRED by design (P3):** the [[BUG-291]] fix just re-keyed this path's idempotency guard to include the accrual period, and batching the writes changes when that guard observes prior credits — get it wrong and leave is silently double-credited. Wants a real-PG before/after credit-count arm, not a late-session refactor. **Natural companion to [[ISSUE-197]]** — both are "do the query once" fixes on money paths. |

### LOW — real but contained
~~**Recruitment offer cluster**~~ — ✅ **CLOSED 2026-08-03 (P2):** [[BUG-067]] + [[ISSUE-123]] + [[ISSUE-124]] fixed and mutation-verified; [[ISSUE-125]] re-filed platform-wide as [[ISSUE-359]].
Remaining: [[ISSUE-194]] **narrowed to one call site** (`LeaveReportService.cs:709`) · [[ISSUE-203]] login p95 on BCrypt(12) · [[ENH-024]] missing `aria-describedby` · [[ISSUE-358]] **narrowed to `WhiteLabel` only** (Scim/CustomDomain/Sandbox gained real gates under D3).

### Correction log (things I got wrong and fixed)
- **[[ISSUE-123]] PR #451** — added a coarse permission gate over an already-finer-grained per-step approver check; 403'd legitimate approvers. Reverted 2026-08-03 with two regression guards. See the changelog head.

### Doc-integrity — ✅ ALL FOUR DISCHARGED (verified 2026-08-03)
Obsolete BUG-243 table struck as OBSOLETE · 33 ALREADY-BUILT deferred-AC rows struck · all three lying comments corrected (`PayrollReportDtos`, `PerformanceDashboardService`, `AttendanceService`) · double-counting resolved.

### Remaining feature work
US-PLT-002 **RLS prod flip** (code complete + proven, committed OFF — ops step, not dev) · **Admin monitoring KPIs** (TC-ADM-002-14..18 — the storage gauge is now nearly free since `TenantStorageUsage.ComputeBytesByTenantAsync` exists; email-sends + SLA-uptime have no OTel dependency; error-rate/P95 blocked on a metrics store or sourceable from the GlitchTip API) · **per-tenant API-call counter** (deliberately deferred slice of US-PLT-004) · **deferred ACs — re-verified 2026-08-04, and the list shrank** (custom-field columns in bulk import · interview-guide attachment · scorecard versioning *(the edit-history half only — there is no template entity to version)* · ~~US-REC-010 FR-8/FR-9~~ **DONE #459** · year-end tax *statements* *(not "PDF" — the report PDF already ships; the gap is per-employee + month-wise + bulk ZIP, US-PAY-009 AC-3)* · ~~US-ADM-006 plan-gated enterprise settings~~ **ALREADY SHIPPED in `520ef273`, missed by the ledger**) · **6 LOW DF residuals** · ops flips (ClamAV prod, GlitchTip prod DSN).
