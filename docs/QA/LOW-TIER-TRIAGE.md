# LOW-Tier Triage & Working Plan (2026-07-18)

Working register for the user-directed **LOW-tier fix campaign** (bugs+issues only; ENH excluded).
Survey found **120 OPEN·LOW** findings (3 BUG, 117 ISSUE). Approach (user-chosen):
**survey+triage → batch-fix the worthwhile ones (value-first) + explicitly WONTFIX/defer the noise.**

> **STALENESS SWEEP (2026-07-18):** verify-before-fix pass found **6 of the 71 REAL-FIX already fixed** (with tests + fix commits) but left stale-OPEN — now closed RESOLVED: **ISSUE-049, ISSUE-050, ISSUE-053, ISSUE-054, ISSUE-006, ISSUE-244** (cf5bb243 / f806d890 / fd99a3bb). **65 genuinely open** remain. Lesson: the LOW ledger is stale-prone — grep the code for each ID before "fixing" it.

- **Execution order:** security → data-integrity → a11y → then the rest by module.
- **NEEDS-DECISION (25):** deferred to the "Deferred decisions" section below (one review sitting), NOT surfaced one-by-one.
- **Per fix:** own `fix/{ID}` (or `fix/low-{module}` batch) branch → build + targeted tests → auditor(s) on security/data/isolation ones → gated auto-merge → ledger close-out.

Status legend: ☐ pending · ▶ in progress · ✅ done (PR#) · ⛔ WONTFIX · ⏸ deferred-decision.

---

## A. REAL-FIX — value-first queue (71)

> **PROGRESS:** A1 security (7) ✅ #358 · Leave (7) ✅ #359 · reusability sweep ✅ #360 · Attendance (5) ✅ #361 (ISSUE-072 reclassified keep-branch; ISSUE-068 spec-align + geofence→DF-23). Next: Core-HR.
> **A2 DATA-INTEGRITY COMPLETE (2026-07-18):** all module batches merged — Leave #359, Attendance #361, Core-HR #362, Payroll #363, Admin #364, misc(Rec/Perf/Auth/Reports) #365, + reuse sweep #360. Several were already-fixed (verify-before-fix caught them; bound TCs added). Deferred (feature-sized): DF-19/20/21/22/23. Follow-ups: DF-24/25/26. ISSUE-072 keep-branch; ISSUE-068 spec-align. **Next: A3 audit (7) / A4 seam (5) / A5 a11y (8) / WONTFIX sweep (20) / decisions doc.**
> **NEW needs-decision surfaced (add to §C review):** (a) ISSUE-072 validator naive-UTC vs service tenant-local future-date frame (ISSUE-065 family) — keep the service guard, fix the validator frame? (b) non-tolerant `Enum.TryParse` endpoints (Payroll/Notification/Onboarding/Offboarding) — make them separator-tolerant like the report endpoints? (c) `SanitizeFileName` ×4 differ on fallback string — unify with a `(fileName, fallback)` shared helper?

### A1. Security / authz / info-disclosure (do first)
- ☐ ISSUE-049 (Auth/BE) refresh token accepted on ANY subdomain → cross-tenant replay · TC-AUTH-ISO-001
- ☐ ISSUE-099 (Perf/BE) `goals/{id}` stub returns 200 for any/foreign-tenant id (IDOR) · TC-PRF-ISO-002
- ☐ ISSUE-267 (Admin/BE) workflow step-chain read open to any authenticated tenant user (authz gap)
- ☐ ISSUE-053 (Auth/BE) password reset doesn't enforce password history
- ☐ ISSUE-057 (Auth/BE) tenant-switch leaks target-tenant lifecycle status to non-members
- ☐ ISSUE-244 (Rec/BE) applicant detail leaks raw `resumeStorageKey` (NFR-5)
- ☐ ISSUE-014 (Admin/BE) per-entity export CSVs dump raw EF cols (TenantId/RowVersion/IsDeleted/CreatedBy)
- ☐ ISSUE-103 (Rec/BE) applicant free-text unsanitized (sibling vacancy IS) — stored-XSS defense
- ☐ ISSUE-050 (Auth/BE) refresh-token reuse detection logs only, no security audit row
- ☐ ISSUE-054 (Auth/BE) authz-denied security log records actor as `User=unknown`
- ☐ ISSUE-064 (Auth/BE) admin unlock of not-locked account writes misleading audit
- ☐ ISSUE-092 (Att/BE) malformed `employeeIds` silently dropped → whole-tenant set returned (no 400)

### A2. Data-integrity / correctness
- ☐ BUG-117 (Leave/BE) renaming LOP leave type via PUT wipes system Color/Description (data loss)
- ☐ ISSUE-091 (Att/BE) terminated-employee payroll hard-zeros present/absent/LOP (under-counts LOP)
- ☐ ISSUE-045 (Leave/BE) carry-forward-pool restoration not pool-aware on cancel
- ☐ ISSUE-085 (Att/BE) `late_minutes` persisted non-zero on non-late rows (false badge)
- ☐ ISSUE-040 (Leave/BE) holiday list default returns deactivated holidays
- ☐ ISSUE-038 (Leave/BE) `/leaves/mine` ignores status/leaveTypeId/year filters
- ☐ ISSUE-044 (Leave/BE) cancellation window hardcoded const=0 (never settable)
- ☐ ISSUE-194 (Rpt/BE) department aggregation case-sensitive (Engineering/engineering split)
- ☐ ISSUE-028 (CHR/BE) custom-field name uniqueness case-sensitive + un-trimmed
- ☐ ISSUE-022 (CHR/BE) job-title `title_name` not whitespace-trimmed
- ☐ ISSUE-152 (Pay/BE) declared annual CTC accepts >2 decimals (no numeric(18,2) normalize)
- ☐ ISSUE-161 (Pay/BE) payslip omits masked bank account (FR-2)
- ☐ ISSUE-184 (Pay/BE) unparseable `status` filter → full unfiltered list
- ☐ ISSUE-003 (Admin/BE) `tenant-usage` ignores unparseable `status=` → all tenants
- ☐ ISSUE-013 (Admin/BE) plans list not server-sortable
- ☐ ISSUE-012 (Admin/BE) inverted audit date range → 200 + 0 rows instead of 400
- ☐ ISSUE-042 (Leave/BE) team-calendar accepts unbounded/degenerate date range
- ☐ ISSUE-033 (Leave/BE) entitlement-rule validation incomplete (no upper bound/dup/job_level)
- ☐ ISSUE-035 (Leave/BE) ineligible leave types leak into apply dropdown (BR-4/BR-5)
- ☐ ISSUE-047 (Leave/BE) report routes require PascalCase; kebab-case 400s
- ☐ ISSUE-096 (Rec/BE) omitted `headcount` silently defaults to 1 (no validation error)
- ☐ ISSUE-079 (Att/BE) daily/weekly cap flags not exposed on any OT DTO
- ☐ ISSUE-080 (Att/BE) unapproved overtime minutes invisible in monthly report
- ☐ ISSUE-068 (Att/BE) geofence single-center only + permission-name drift
- ☐ ISSUE-072 (Att/BE) validation rejections lack machine-readable `code`
- ☐ ISSUE-076 (Att/BE) employee can't read own shift (gated by Manage → 403 self)
- ☐ ISSUE-309 (Att/BE) tenant-wide sweeps ignore per-location overrides
- ☐ ISSUE-061 (Auth/BE) idle-reset defeated when activity debounce ≥ idle timeout
- ☐ ISSUE-098 (Perf/BE) future goal window returns "closed" not "not yet open"
- ☐ ISSUE-107 (Perf/BE) self-assessment before-window uses "has ended" wording
- ☐ ISSUE-023 (CHR/BE) org-tree `reportingViewAvailable` flag self-contradicts between views
- ☐ ISSUE-027 (CHR/BE) employee detail DTO omits reporting-manager FK; bulk-assign mislabels
- ☐ ISSUE-225 (CHR/BE) employee profile DTO omits reporting manager
- ☐ ISSUE-039 (Leave/BE) my-balance N+1; P95 ~341ms > 200ms NFR-1
- ☐ ISSUE-198 (Rpt/BE) CSV UTF-8 BOM inconsistent across exports
- ☐ ISSUE-008 (Admin/BE) at-2MB logo → opaque Kestrel 400 before friendly validator
- ☐ ISSUE-009 (Admin/BE) session-policy lacks idle≤absolute invariant + unit mismatch
- ☐ ISSUE-001 (Admin/BE) `impersonation/targets` 404 instead of 400 on missing tenantId

### A3. Audit-completeness (semantic action / envelope)
- ☐ ISSUE-037 (Leave) approve/reject audited as generic Update, not Approved/Rejected
- ☐ ISSUE-046 (Leave) LOP writes lack distinct semantic audit action
- ☐ ISSUE-020 (CHR) deactivate audited as generic Department.Update
- ☐ ISSUE-015 (CHR) employee audit stores actor email not user UUID
- ☐ ISSUE-006 (Admin) US-ADM-005 audit rows omit ip/user_agent
- ☐ ISSUE-010 (Admin) BR-2 auto-archive writes no workflow.archived audit
- ☐ ISSUE-059 (Auth) session audit rows carry no session metadata
- ☐ ISSUE-266 (Admin) WorkflowService Create/Update drop ErrorCode on step-validation fail

### A4. Notification/seam gaps (in-module, non-delivery)
- ☐ ISSUE-224 (CHR) async employee-import completion emits no in-app notification
- ☐ ISSUE-063 (Auth) lockout notification is a stub (in-module content missing)
- ☐ ISSUE-241 (Auth) recovery-code login lacks "regenerate recovery codes" prompt
- ☐ ISSUE-248 (Auth) no authenticated self-service change-password endpoint
- ☐ ISSUE-220 (Auth) SSO challenge returns misleading `not_configured`

### A5. A11y / WCAG (FE)
- ☐ BUG-096 (Auth/FE) login page WCAG 2.1 AA color-contrast (public)
- ☐ BUG-105 (Admin/FE) monitoring gauges no accessible name (4.1.2)
- ☐ ISSUE-205 (Rpt/FE) dashboard label-content-name mismatch (2.5.3)
- ☐ ISSUE-213 (Ntf/FE) audit-log table missing th scope + caption/aria (1.3.1)
- ☐ ISSUE-215 (Onb/FE) onboarding helper text fails 1.4.3 (2.52:1)
- ☐ ISSUE-204 (Admin/FE) tenant branding logo 404s every load
- ☐ ISSUE-211 (Admin/FE) Users page renders raw i18n keys
- ☐ ISSUE-216 (Admin/FE) Enterprise plan card "Up to employees" for null max

---

## B. WONTFIX / stale (20) — close in the ledger with reason
- ⛔ ISSUE-182 POSITIVE finding (IDOR-protected write) — no defect
- ⛔ ISSUE-016 duplicate of BUG-096 (same login-contrast) — close with BUG-096
- ⛔ ISSUE-070 / ISSUE-082 stale TC expected values (engine correct) — fix the TC, not code
- ⛔ ISSUE-273 / ISSUE-282 TEST-HEALTH / coverage residue (not a product defect)
- ⛔ ISSUE-199 / ISSUE-196 documented by-design behavior notes
- ⛔ ISSUE-011 / ISSUE-030 / ISSUE-151 HTTP status-code nits (correct semantics/messages)
- ⛔ ISSUE-192 pageSize cap 200 vs docs 100 (doc-vs-impl nit)
- ⛔ ISSUE-219 plan live-read not Redis-cached but meets SLA
- ⛔ ISSUE-017 favicon 404 console noise
- ⛔ ISSUE-031 leave-type sanitization defense-in-depth (Angular escapes; TC passes)
- ⛔ ISSUE-183 completedAt-before-startedAt presentation nit
- ⛔ ISSUE-279 RLS reconciler warning false-fires in correct config (log noise)
- ⛔ ISSUE-270 / ISSUE-274 ENH-typed (notification category reuse; OTel span coverage)
- ⛔ ISSUE-292 ENH-flavored generic-vs-semantic onboarding audit names

---

## C. Deferred decisions (25) — review in one sitting; each: choice + my recommendation
- ISSUE-060 (Auth) session-policy path drift vs spec — *rec: align to spec path or document the deviation.*
- ISSUE-062 (Auth) lockout audit dual-write needs a system audit store that doesn't exist — *rec: defer until US-PLT-004 observability.*
- ISSUE-007 (Admin) TenantUsersController `{id}` overload + mixed authz — *rec: split routes; needs API-shape call.*
- ISSUE-246 (CHR) EXIF strip doesn't cover WebP (ImageSharp limit) — *rec: reject WebP upload OR accept the gap; needs product call.*
- ISSUE-286 (CHR) legacy free-text Location vs structured LocationId — *rec: migrate import to LocationId; needs data-migration call.*
- ISSUE-293 (CHR/NTF) National ID not modeled → PII audit can't cover — *rec: needs-BA (model the field?).*
- ISSUE-032 (Leave/DB) RLS not enabled — platform tech-debt (US-PLT-002 flip is ops).
- ISSUE-034 (Leave) pro-rata day-count vs month-fraction (10.08 vs 10.00) — *rec: pick the spec formula; product call.*
- ISSUE-036 (Leave) attachment 5MB cap + tenant blob storage feature gap — *rec: story-sized; defer.*
- ISSUE-222 (Leave) LOP leave type lazy-created vs at setup — *rec: seed at provisioning; small but spec call.*
- ISSUE-077 (Att) no API to set/transfer tenant default shift — *rec: add endpoint; small feature.*
- ISSUE-081 (Att) monthly OT report has no export endpoint (AC-5) — *rec: add export; feature.*
- ISSUE-083 (Att) stale materialized summary vs live drill-down — *rec: invalidation strategy call.*
- ISSUE-087 (Att) late/chronic-lateness notification seam absent — *rec: US-NTF-006 family.*
- ISSUE-108 (Rec) interview-stage soft-gate seam absent — *rec: product call (warn vs block).*
- ISSUE-110 (Rec) stage-transition notifications log-only + no template sub — *rec: US-NTF-006 family.*
- ISSUE-317 (Rec/FE) no `Unknown` badge for tolerated corrupt enum row — *rec: add badge (DF-12).*
- ISSUE-159 (Pay) payslip footer disclaimer hardcoded, not tenant-config — *rec: make configurable; small.*
- ISSUE-162 (Pay) no per-employee payslip retry endpoint (FR-8) — *rec: add endpoint; feature.*
- ISSUE-229 (Pay) tenant sender-domain not implementable (ResolveFromAddress null) — *rec: US-NTF-006 family.*
- ISSUE-280 (Pay) BASIC identified by Code vs Name; PayrollSlipLine drops Code — *rec: durable refactor; defer.*
- ISSUE-295 (Pay) BUG-079 residual clauses (encashment basis/carry-forward parity) — *rec: needs payroll call.*
- ISSUE-289 (Perf/FE) sign-off UI collapses structured fields into Body — *rec: FE work; defer to P6.*
- ISSUE-271 (TRN) manager eligible-plans endpoint has no FE consumer — *rec: FE work; defer to P6.*
- ISSUE-276 (Cache) Redis multiplexer coupling would break a non-API host — *rec: refactor; defer.*

---

## D. Needs-investigation (4)
- ☐ ISSUE-100 (Perf) route-prefix drift TCs/FE vs live — verify FE targets, then fix/close
- ☐ ISSUE-197 (Rpt/DATA) CTC Analysis 0.00 employer contributions — likely missing seed config; verify
- ☐ ISSUE-315 (Onb) idempotency-key header-vs-body precedence untested (= DF-10) — add test arm
- ☐ ISSUE-278 (Platform) Hangfire needs CREATE ON DATABASE on greenfield RLS deploy — ops/verify
