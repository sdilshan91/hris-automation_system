# LOW-Tier Deferred Decisions — for one-sitting review

Compiled 2026-07-18 during the LOW-tier fix campaign. Each item is a LOW finding (or a
campaign-surfaced choice) whose fix needs a **product/design decision** you should make before
implementing. Format: **ID (module)** — the choice · *my recommendation*. Pick per item; I'll
then implement the chosen ones in a follow-up batch.

## A. Formula / value / strictness choices
- **ISSUE-034 (Leave)** — pro-rata uses day-count ratio (10.08) vs spec month-fraction (10.00). · *rec: adopt the spec's month-fraction formula.*
- **ISSUE-152 JSON-string half (Payroll)** — `annualCtc` still accepts quoted-string numerics (`"600000"`) because ASP.NET `NumberHandling=AllowReadingFromString` is global. The >2-decimal half is FIXED (#363). · *rec: leave global NumberHandling as-is (platform-wide + FE impact); accept the LOW residue. Only tighten if you want strict numeric-token typing platform-wide.*
- **ISSUE-192 (Notifications)** — audit-log pageSize cap is 200; docs say 100. · *rec: update the docs to 200 (already WONTFIX'd; noted for completeness).*

## B. Naming / contract / spec alignment
- ✅ **DONE (doc-align, 39 files)** **ISSUE-068 permission drift (Attendance)** — spec `Attendance.Clock.Self` vs shipped `Attendance.CheckIn`. · *rec: align the spec/US to the shipped `Attendance.CheckIn`.* (multi-location geofence = DF-23.)
- ✅ **DONE (US-ADM-005 contract note)** **ISSUE-211 status-casing (Admin/FE)** — FE type is lowercase, BE wire is PascalCase; FE now normalizes case-insensitively (#368). · *rec: pin the serialized casing in the Swagger contract so FE model + i18n keys match exactly.*
- **ISSUE-007 (Admin)** — `TenantUsersController` `{id}` means two things + mixed authz gates. · *rec: split into distinct routes; needs an API-shape call.*
- **ISSUE-280 (Payroll)** — BASIC component identified by Code vs Name; `PayrollSlipLine` drops Code. · *rec: durable refactor to key on Code; defer until a payroll-model pass.*
- ✅ **DONE (#371)** **ISSUE-072 validator frame (Attendance)** — the tenant-local future-date service guard is CORRECT (kept); the validator uses naive-UTC (ISSUE-065 family). · *rec: align the validator to the tenant-local frame (small); keep the service guard.*

## C. New setting / endpoint / feature (small)
- ✅ **DONE (#371, TC added; code already seeded)** **ISSUE-222 (Leave)** — LOP leave type created lazily on first assign vs at tenant setup. · *rec: seed at provisioning.*
- ✅ **DONE (#371)** **ISSUE-077 (Attendance)** — no API to set/transfer the tenant default shift. · *rec: add a small admin endpoint.*
- ✅ **DONE (#371)** **ISSUE-081 (Attendance)** — monthly OT report has no export endpoint (AC-5). · *rec: add CSV/XLSX export (reuse CsvExport/ExportFormatNormalizer).*
- ✅ **DONE (#371)** **ISSUE-159 (Payroll)** — payslip footer disclaimer hardcoded, not tenant-configurable. · *rec: make it a tenant setting.*
- ⏸ **DEFERRED (#371 → DF-31, story-sized)** **ISSUE-162 (Payroll)** — no per-employee payslip retry endpoint (FR-8). · *rec: add the endpoint.*
- **ISSUE-036 (Leave)** — attachment 5MB cap + tenant-scoped blob storage not implemented. · *rec: story-sized; defer.*
- **ISSUE-248-style change-password (done #367)** — no decision; noted resolved.

## D. Delivery / notification-dependent (US-NTF-006 family)
- ✅ **per-late DONE (#374 verify); chronic→DF-33** **ISSUE-087 (Attendance)** — late/chronic-lateness notification dispatch seam absent. · *rec: fold into US-NTF-006.*
- ✅ **DONE (#374, already-wired verified)** **ISSUE-110 (Recruitment)** — stage-transition notifications sync log-only + no BR-5 template substitution. · *rec: US-NTF-006.*
- ✅ **DONE (#374)** **ISSUE-229 (Payroll)** — payslip tenant sender-domain not implementable (ResolveFromAddress null). · *rec: US-NTF-006 delivery wiring.*
- **ISSUE-108 (Recruitment)** — interview-stage soft-gate warning seam absent. · *rec: product call (warn vs block on stage move w/o interview).*
- ✅ **DONE (#374)** **ISSUE-063 lockout tenant-name (Auth)** — content built (#367); tenant-name enrichment needs the seam signature. · *rec: fold into US-NTF-006 delivery.*

## E. Data-model / BA-gated
- **ISSUE-293 (Core-HR)** — National ID not modeled on Employee → PII-read audit can't cover it. · *rec: needs-BA (model the field?).*
- **ISSUE-286 (Core-HR)** — legacy free-text `Employee.Location` vs structured `LocationId`; import sets only free-text. · *rec: migrate import to LocationId; data-migration call.*
- **ISSUE-246 (Core-HR)** — EXIF stripping doesn't cover WebP (ImageSharp limit). · *rec: reject WebP upload OR accept the gap; product call.*
- **ISSUE-021 / BUG-056 / ISSUE-285** (parked pre-campaign) — SalaryGrade entity / goal-finalize seam / dashboard-SLA birthday-index. · *rec: separate scoping.*

## F. Infra / platform / observability
- **ISSUE-032 (Leave/DB)** — RLS not enabled (0 policies); EF filter only. · *rec: the RLS prod flip is the standing ops step (built + proven OFF).*
- **ISSUE-062 (Auth)** — lockout audit dual-write needs a system audit store that doesn't exist. · *rec: defer to US-PLT-004 observability.*
- ✅ **DONE (doc-align to shipped route)** **ISSUE-060 (Auth)** — session-policy path drift vs spec. · *rec: align to spec path or document the deviation.*
- **ISSUE-083 (Attendance)** — stale materialized monthly summary vs live drill-down. · *rec: define an invalidation strategy.*
- **ISSUE-276 (Cache)** — Redis `IDistributedCache`→shared-multiplexer coupling would break a future non-API host. · *rec: refactor when a second host is added.*
- **ISSUE-295 (Payroll)** — BUG-079 residual clauses (encashment BASIC basis / carry-forward parity). · *rec: needs a payroll call.*

## G. FE work (deferred to a FE pass)
- **ISSUE-289 (Performance/FE)** — sign-off UI collapses structured BE fields into one Body. · *rec: FE pass (P6).*
- **ISSUE-271 (Training/FE)** — manager eligible-plans endpoint has no FE consumer. · *rec: FE pass (P6).*
- ✅ **DONE (#369)** **ISSUE-317 (Recruitment/FE)** — no `Unknown` badge for a tolerated corrupt enum row (DF-12). · *rec: add the badge.*
- ✅ **DONE (#369)** **DF-24 / DF-26 / DF-27** — bulk-assign message field, vacancy headcount, auth FE (tenant_required / regenerate prompt / change-password form). · *rec: one FE follow-up batch.*

## H. Code-quality (reuse, from the sweep)
- ✅ **DONE (#371, FileNameSanitizer helper)** **SanitizeFileName ×4** — 4 copies differ on fallback string. · *rec: unify with a shared `SanitizeFileName(fileName, fallback)` helper.*
- **Non-tolerant `Enum.TryParse` endpoints** (Payroll/Notification/Onboarding/Offboarding) — reject kebab-case where the report endpoints accept it. · *rec: decide per-endpoint whether to make them separator-tolerant (behavior change) vs leave strict.*
