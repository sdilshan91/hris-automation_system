---
module: Payroll
total_user_stories: 4
total_test_cases: 64
created: 2026-06-15
updated: 2026-06-15
status: in-progress
---

# Payroll -- Test Matrix

> US-PAY-001 (Configure Salary Structure and Components per Tenant) is the FIRST Payroll story and establishes `test-cases/payroll/` (dir + TEST-MATRIX + the root Payroll section in TRACEABILITY-MATRIX). It adds 16 test cases: 12 functional/security/performance/accessibility (TC-PAY-001-01..12) + 4 dedicated multi-tenant isolation on the new `salary_component` / `salary_structure` / `salary_structure_component` tables (TC-PAY-ISO-001..004). The Payroll module reuses the same per-story-suffix functional ID scheme as Recruitment (TC-PAY-{NNN}-XX) with a separate running ISO counter (TC-PAY-ISO-NNN). All 6 acceptance criteria of US-PAY-001 are covered.
>
> US-PAY-002 (Assign Salary Structure to Employee) adds 16 test cases: 12 functional/security/performance/accessibility (TC-PAY-002-01..12) covering CTC-driven `employee_salary_component` creation, breakdown preview, component overrides, inactive-structure rejection, CTC-sum tolerance, future-dated supersession, numeric(18,2) precision, bulk assign, authz, injection, perf SLAs, and revision history; plus 4 dedicated multi-tenant isolation on the new `employee_salary_component` / `salary_revision_history` tables (TC-PAY-ISO-005..008, continuing the ISO counter from 004). All 5 acceptance criteria of US-PAY-002 are covered.
>
> US-PAY-003 (Run Monthly Payroll for All Employees) adds 16 test cases: 12 functional/security/performance/accessibility (TC-PAY-003-01..12) covering the initiate->202+runId+Queued->Hangfire compute->slips persisted->ReviewPending->notify happy path, duplicate-Finalized-period 409 (BR-1), attendance-not-locked block (BR-3), no-salary-structure skip+continue (AC-6), LOP calc (BR-2), pro-rata joiner/separator (BR-4/5), penny reconciliation + half-up rounding (BR-8), the status transition matrix + Finalized immutability (BR-6/7/FR-7), authz (Payroll.Run only), idempotency-key replay + distributed lock (FR-9/NFR-2/3), the 5,000-employee < 10-min batch-insert perf SLA (AC-5/NFR-1/6), and the Runs-table/new-run-modal/progress-bar/status-stepper a11y; plus 4 dedicated multi-tenant isolation on the new `payroll_run` / `payroll_slip` / `payroll_slip_detail` tables and the compute pipeline (TC-PAY-ISO-009..012, continuing the ISO counter from 008): end-to-end compute isolation (AC-7), run/slip API tenant-context rejection + IDOR, cross-tenant write/job-arg block, and tenant-scoped SignalR group / distributed lock / structure cache. All 7 acceptance criteria of US-PAY-003 are covered.
>
> US-PAY-004 (Generate Individual Payslips) adds 16 test cases: 12 functional/security/performance/accessibility (TC-PAY-004-01..12) covering the generate-on-ReviewPending/Finalized -> Hangfire job -> one QuestPDF per employee at `{tenantId}/payroll/{runId}/{employeeId}.pdf` with all required content sections happy path (AC-1/2, FR-1/2/4/5/7), single + Download-All ZIP (AC-3, FR-6), regenerate-overwrites-with-new-template on non-finalized runs (AC-5, FR-3), generate-on-disallowed-status -> 400 (BR-1), failed-render -> pdf_status=Failed + individually retryable (FR-8), the size/name/YTD/terminated boundary set (NFR-2 <=200KB, BR-5 filename, BR-4 YTD sums, BR-6 terminated final-month), path-traversal block + no-JS-in-PDF (NFR-6/NFR-5), authz Payroll.*.All (403/401), the 5,000-PDF <=5-min parallel-batch perf SLA (NFR-1/3), point-in-time snapshot + disclaimer + branding content fidelity (BR-2/3, FR-2/3), per-slip status/timestamp + status-bar progress (FR-7), and the payslip-list-table/status-bar/inline-preview-modal/Download-All a11y; plus 4 dedicated multi-tenant isolation on payslip blob storage + download/preview (TC-PAY-ISO-013..016, continuing the ISO counter from 012): cross-tenant read/download block (AC-4), download/preview API tenant-context rejection + IDOR, cross-tenant blob/slip write block (server/job-arg-derived path), and tenant-scoped blob layout / ZIP staging / download cache. All 5 acceptance criteria of US-PAY-004 are covered.

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 4 (US-PAY-001, US-PAY-002, US-PAY-003, US-PAY-004) |
| Total Test Cases | 64 (48 functional/security/perf/a11y + 16 dedicated multi-tenant isolation) |
| US-PAY-001 Test Cases | 16 (TC-PAY-001-01..12 + TC-PAY-ISO-001..004) |
| US-PAY-002 Test Cases | 16 (TC-PAY-002-01..12 + TC-PAY-ISO-005..008) |
| US-PAY-003 Test Cases | 16 (TC-PAY-003-01..12 + TC-PAY-ISO-009..012) |
| US-PAY-004 Test Cases | 16 (TC-PAY-004-01..12 + TC-PAY-ISO-013..016) |
| Critical Priority | 26 (PAY-001-01, -07, -08, ISO-001..003; PAY-002-01, -03, -08, ISO-005..007; PAY-003-01, -02, -07, -08, -09, -10, ISO-009..011; PAY-004-01, -04, -05, -07, -08, ISO-013..015) |
| High Priority | 38 (remaining PAY-001 + PAY-002 + PAY-003 + PAY-004 functional/perf/a11y + ISO-004, ISO-008, ISO-012, ISO-016) |
| Medium Priority | 0 |
| Low Priority | 0 |
| Blocked Test Cases | 0 |
| Acceptance Criteria Coverage | US-PAY-001 6/6 (AC-1..AC-6); US-PAY-002 5/5 (AC-1..AC-5); US-PAY-003 7/7 (AC-1..AC-7); US-PAY-004 5/5 (AC-1..AC-5) |
| Conditional/Deferred Test Cases | (1) EF-query-filter-vs-PostgreSQL-RLS: US-PAY-001 AC-6/FR-8 and US-PAY-002 AC-5/FR-8 specify PostgreSQL RLS policies on the payroll tables; this platform enforces isolation via EF Core global query filters + TenantInterceptor. TC-PAY-ISO-001/003/005/007 describe the EF mechanism and note RLS session-level assertion as an extension point if RLS policies are later added. (2) Redis/cache: US-PAY-001 list cache (TC-PAY-ISO-004, TC-PAY-001-11) and US-PAY-002 salary-preview cache (TC-PAY-ISO-008) -- assume cache available per S10; if preview is computed on-demand without caching today, TC-PAY-ISO-008 is CONDITIONAL and asserts no shared/global key is used. (3) Historical-payslip-unchanged (AC-2, TC-PAY-001-02 step 4) and BR-7 type-change-after-finalized-run are CONDITIONAL on the Payroll Run/Payslip story existing (not yet built). (4) Audit assertions (NFR-5/NFR-4) reference the Audit logging module (S24); the enqueue/record is asserted, full audit-store verification owned by the Audit module. (5) US-PAY-002 BR-4 (probation vs confirmed structure), BR-5 (Payroll Incomplete exclusion from runs), BR-6 (no backdating into a finalized run), and BR-7 (employer-side statutory in CTC) touch surfaces owned by later stories (US-PAY-006 statutory, US-PAY-007 adjustment, the Payroll Run story); BR-5 "Payroll Incomplete" flag is asserted in TC-PAY-002-01, the exclusion-from-runs is CONDITIONAL on the Payroll Run story. (6) US-PAY-003: SignalR progress + run notifications and the structure cache / distributed lock (TC-PAY-003-01/12, TC-PAY-ISO-012) are written as REAL tests but the email/in-app DELIVERY is CONDITIONAL on the Notification System (S25) + Hangfire (the enqueue is asserted); if the structure cache (NFR-7) or progress is computed on-demand without Redis today, TC-PAY-ISO-012's cache/lock-key steps are CONDITIONAL and assert no shared/global key is used. AC-7/FR-3 say "RLS enforces isolation throughout the pipeline"; this platform enforces via EF Core global query filters + TenantInterceptor + the tenant-scoped job arg -- TC-PAY-ISO-009..011 describe the EF/job-arg mechanism and note Postgres RLS as an extension point. The 5,000-employee perf SLA (TC-PAY-003-11) requires a seeded load environment; statutory deduction math depends on US-PAY-006 config. (7) US-PAY-004: payslip generation depends on US-PAY-003 having produced `payroll_slip` rows (the run/slip surface exists once US-PAY-003 is built) -- the generate/render/store flow is asserted as a REAL test against those slips; the "Download All" ZIP link DELIVERY is CONDITIONAL on the Notification System (S25) -- the ZIP build + availability are asserted; tenant branding (logo/company/address) for FR-3/AC-2 depends on US-TENANT-xxx config (system-default template used as fallback); the 5,000-PDF <=5-min perf SLA (TC-PAY-004-09) requires a seeded load environment; blob storage is local filesystem in Phase 1 (cloud Phase 2) -- isolation TCs describe the `{tenantId}/payroll/...` prefix + EF-filtered slip lookup as the enforcement, with Postgres RLS noted as an extension point (TC-PAY-ISO-013); the download/preview CACHE / signed-URL keying (TC-PAY-ISO-016) is CONDITIONAL on a cache/CDN layer existing -- if none today, it asserts direct tenant-filtered serving with no shared/global key; YTD sums (TC-PAY-004-06, BR-4) assume prior-month payslips exist for the same fiscal year. |
| Status | All Draft |

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-PAY-001 | Configure Salary Structure and Components per Tenant | TC-PAY-001-01, TC-PAY-001-02, TC-PAY-001-03, TC-PAY-001-04, TC-PAY-001-05, TC-PAY-001-06, TC-PAY-001-07, TC-PAY-001-08, TC-PAY-001-09, TC-PAY-001-10, TC-PAY-001-11, TC-PAY-001-12 | 12 |
| Cross-cutting (PAY-001) | Multi-tenant isolation (salary_component / salary_structure / junction) | TC-PAY-ISO-001, TC-PAY-ISO-002, TC-PAY-ISO-003, TC-PAY-ISO-004 | 4 |
| US-PAY-002 | Assign Salary Structure to Employee | TC-PAY-002-01, TC-PAY-002-02, TC-PAY-002-03, TC-PAY-002-04, TC-PAY-002-05, TC-PAY-002-06, TC-PAY-002-07, TC-PAY-002-08, TC-PAY-002-09, TC-PAY-002-10, TC-PAY-002-11, TC-PAY-002-12 | 12 |
| Cross-cutting (PAY-002) | Multi-tenant isolation (employee_salary_component / salary_revision_history) | TC-PAY-ISO-005, TC-PAY-ISO-006, TC-PAY-ISO-007, TC-PAY-ISO-008 | 4 |
| US-PAY-003 | Run Monthly Payroll for All Employees | TC-PAY-003-01, TC-PAY-003-02, TC-PAY-003-03, TC-PAY-003-04, TC-PAY-003-05, TC-PAY-003-06, TC-PAY-003-07, TC-PAY-003-08, TC-PAY-003-09, TC-PAY-003-10, TC-PAY-003-11, TC-PAY-003-12 | 12 |
| Cross-cutting (PAY-003) | Multi-tenant isolation (payroll_run / payroll_slip / payroll_slip_detail + compute pipeline) | TC-PAY-ISO-009, TC-PAY-ISO-010, TC-PAY-ISO-011, TC-PAY-ISO-012 | 4 |
| US-PAY-004 | Generate Individual Payslips | TC-PAY-004-01, TC-PAY-004-02, TC-PAY-004-03, TC-PAY-004-04, TC-PAY-004-05, TC-PAY-004-06, TC-PAY-004-07, TC-PAY-004-08, TC-PAY-004-09, TC-PAY-004-10, TC-PAY-004-11, TC-PAY-004-12 | 12 |
| Cross-cutting (PAY-004) | Multi-tenant isolation (payslip blob storage / download / preview) | TC-PAY-ISO-013, TC-PAY-ISO-014, TC-PAY-ISO-015, TC-PAY-ISO-016 | 4 |

## Test Case Detail

| Test Case ID | Title | Type | Priority | Category | ACs / FRs / BRs |
|--------------|-------|------|----------|----------|-----------------|
| TC-PAY-001-01 | Create component, create structure, link + reorder + activate (happy path) | E2E | Critical | Happy path | AC-1, AC-3, AC-4, FR-1, FR-2, FR-3, FR-5, BR-1, BR-2 |
| TC-PAY-001-02 | Edit component -> future runs use new def; historical payslips unchanged | Functional | High | Alternative path | AC-2, FR-1, NFR-1, NFR-5, BR-7 |
| TC-PAY-001-03 | Duplicate component code per tenant rejected; allowed in another tenant | Functional | High | Negative | AC-1, FR-1, BR-2 |
| TC-PAY-001-04 | Delete component in use -> 409 + affected-count; statutory protected | Functional | Critical | Negative | AC-5, FR-1, FR-3, BR-3 |
| TC-PAY-001-05 | Activate structure with no earning rejected (FR-5) | Functional | High | Negative | AC-3, FR-2, FR-3, FR-5 |
| TC-PAY-001-06 | numeric(18,2) precision; deduction > gross (BR-4); pagination max 100 | Functional | High | Boundary | AC-1, AC-3, FR-1, FR-3, NFR-3, BR-4 |
| TC-PAY-001-07 | Formula rejects circular refs + invalid syntax; safe eval only (BR-6) | Functional | Critical | Negative/Security | AC-1, AC-3, FR-1, FR-4, BR-6 |
| TC-PAY-001-08 | Authz: only Payroll.*.All / Tenant Admin configure; others 403 | Security | Critical | Security | AC-1, AC-2, AC-3, AC-4, AC-5, FR-1, FR-2, FR-3 |
| TC-PAY-001-09 | Names/formula fields resist XSS + SQL injection | Security | High | Security | AC-1, AC-3, FR-1, FR-2, FR-4, NFR-5 |
| TC-PAY-001-10 | Single default structure (BR-1) + clone structure (FR-6) | Functional | High | Negative/Alternative | AC-3, FR-2, FR-3, FR-6, BR-1 |
| TC-PAY-001-11 | Fetch all components <= 200ms P95 (NFR-2); pagination scales (NFR-3) | Performance | High | Performance | AC-1, NFR-1, NFR-2, NFR-3 |
| TC-PAY-001-12 | Slide-over + inline table + drag-reorder WCAG 2.1 AA | Accessibility | High | Accessibility | AC-1, AC-3, AC-4 |
| TC-PAY-ISO-001 | Tenant A cannot see/retrieve Tenant B's components/structures (read iso) | Security | Critical | Multi-tenant isolation | AC-6, FR-8 |
| TC-PAY-ISO-002 | Payroll APIs reject no/invalid/mismatched tenant context | Security | Critical | Multi-tenant isolation | AC-6, FR-8 |
| TC-PAY-ISO-003 | Cross-tenant writes blocked; tenant_id session-derived | Security | Critical | Multi-tenant isolation | AC-6, FR-1, FR-2, FR-3, FR-8 |
| TC-PAY-ISO-004 | Payroll list caches tenant-scoped (no cross-tenant cache leak) | Security | High | Multi-tenant isolation | AC-6, FR-8, NFR-1 |
| TC-PAY-002-01 | Assign structure w/ CTC -> employee_salary_component rows from rules + breakdown preview before confirm (happy path) | E2E | Critical | Happy path | AC-1, FR-1, FR-2, FR-3, BR-1 |
| TC-PAY-002-02 | Component override saved while others stay calculated | Functional | High | Negative | AC-3, FR-1, FR-2, FR-3, FR-6 |
| TC-PAY-002-03 | Assigning inactive/deactivated structure rejected 400 | Functional | Critical | Negative | FR-7, FR-1 |
| TC-PAY-002-04 | Component sum != CTC beyond +/-1 tolerance rejected | Functional | High | Negative/Boundary | FR-6, FR-2, FR-3 |
| TC-PAY-002-05 | Future-dated assignment doesn't supersede current until date arrives | Functional | Critical | Negative/Boundary | AC-2, BR-1, BR-2, BR-3, FR-1, FR-4 |
| TC-PAY-002-06 | numeric(18,2) precision + CTC-derivation boundary values | Functional | High | Boundary | FR-1, FR-2, FR-3, FR-6 |
| TC-PAY-002-07 | Bulk assign to multiple employees w/ individual CTCs + progress indicator | Functional | High | Negative | AC-4, FR-5, FR-2, FR-6, BR-3 |
| TC-PAY-002-08 | Authz: only Payroll.*.All may assign; others 403 | Security | Critical | Security | AC-1, AC-2, AC-3, AC-4, FR-1, FR-3, FR-4, FR-5 |
| TC-PAY-002-09 | Revision history captures old/new structure + CTC + changed_by/at + reason (regression) | Functional | High | Negative | AC-2, FR-4, BR-3, NFR-4 |
| TC-PAY-002-10 | CTC/override/reason fields resist XSS + SQL injection; PII not leaked | Security | High | Security | FR-1, FR-4, FR-5, NFR-4, NFR-5 |
| TC-PAY-002-11 | Preview <= 500ms (NFR-2); bulk assign 500 emps <= 30s (NFR-1) | Performance | High | Performance | AC-1, AC-4, NFR-1, NFR-2, FR-3, FR-5 |
| TC-PAY-002-12 | Compensation tab + breakdown table + revision timeline + bulk spreadsheet WCAG 2.1 AA | Accessibility | High | Accessibility | AC-1, AC-2, AC-3, AC-4 |
| TC-PAY-ISO-005 | Tenant B cannot access Tenant A employee salary assignment/revisions (read iso) | Security | Critical | Multi-tenant isolation | AC-5, FR-8 |
| TC-PAY-ISO-006 | Salary APIs reject missing/invalid/mismatched tenant context | Security | Critical | Multi-tenant isolation | AC-5, FR-8 |
| TC-PAY-ISO-007 | Cross-tenant salary writes blocked; tenant_id session-derived (incl. bulk) | Security | Critical | Multi-tenant isolation | AC-5, FR-1, FR-5, FR-8 |
| TC-PAY-ISO-008 | Salary preview/breakdown caches tenant-scoped (no cross-tenant cache leak) | Security | High | Multi-tenant isolation | AC-5, FR-8, NFR-2 |
| TC-PAY-003-01 | Initiate run -> 202+runId+Queued; job computes, persists slips, ReviewPending, notifies HR (happy path) | E2E | Critical | Happy path | AC-1, AC-2, AC-3, FR-1, FR-2, FR-3, FR-4, FR-5, FR-6, FR-8, BR-6 |
| TC-PAY-003-02 | Duplicate run for a Finalized period -> 409; one non-cancelled run per period (BR-1) | Functional | Critical | Negative/Boundary | AC-4, FR-1, BR-1 |
| TC-PAY-003-03 | Run blocked when attendance not locked/finalized (BR-3) | Functional | High | Negative | AC-1, AC-2, FR-4, BR-3 |
| TC-PAY-003-04 | Employee without salary structure skipped with warning; run continues (AC-6) | Functional | High | Negative | AC-6, FR-5, FR-8 |
| TC-PAY-003-05 | LOP calc -- 3 unapproved absences in a 22-working-day month (BR-2) | Functional | High | Boundary | AC-2, FR-5, BR-2 |
| TC-PAY-003-06 | Pro-rata mid-month joiner / separator (BR-4/5) | Functional | High | Boundary | AC-2, FR-5, BR-4, BR-5 |
| TC-PAY-003-07 | Penny reconciliation -- sum(components)==net; round half-up (BR-8) | Functional | Critical | Boundary | AC-2, FR-5, BR-8 |
| TC-PAY-003-08 | Status transition matrix; re-run in ReviewPending/Cancelled; Finalized immutable (BR-6/7/FR-7) | Functional | Critical | Negative/Boundary | AC-3, AC-4, FR-1, FR-7, BR-6, BR-7 |
| TC-PAY-003-09 | Authz: only Payroll.Run may initiate/cancel/re-run; others 403; 401 unauth | Security | Critical | Security | AC-1, FR-1, FR-2, FR-7 |
| TC-PAY-003-10 | Idempotency-Key replay no duplicate run/job; distributed lock blocks concurrent same-tenant+period (FR-9/NFR-2/3) | Security | Critical | Negative/Security | AC-1, FR-1, FR-2, FR-9, NFR-2, NFR-3 |
| TC-PAY-003-11 | 5,000 employees < 10 min; batch insert; cached structure reads (AC-5/NFR-1/6/7) | Performance | High | Performance | AC-5, NFR-1, NFR-6, NFR-7 |
| TC-PAY-003-12 | Runs table + new-run modal + progress bar + status stepper WCAG 2.1 AA | Accessibility | High | Accessibility | AC-1, AC-3, FR-6 |
| TC-PAY-ISO-009 | Tenant A run includes only Tenant A employees; B excluded throughout compute pipeline (compute iso) | Security | Critical | Multi-tenant isolation | AC-7, FR-3, FR-5, FR-8 |
| TC-PAY-ISO-010 | Run/slip APIs reject missing/invalid/mismatched tenant context; no cross-tenant read/IDOR | Security | Critical | Multi-tenant isolation | AC-7, FR-1, FR-3, FR-8 |
| TC-PAY-ISO-011 | Cross-tenant payroll writes blocked; tenant_id session/job-arg-derived, not client-supplied | Security | Critical | Multi-tenant isolation | AC-7, FR-1, FR-2, FR-3, FR-8 |
| TC-PAY-ISO-012 | SignalR group / run notifications / distributed lock / structure cache all tenant-scoped | Security | High | Multi-tenant isolation | AC-7, FR-6, FR-8, NFR-3, NFR-7 |
| TC-PAY-004-01 | Generate payslips -> Hangfire job renders 1 PDF/employee at {tenantId}/payroll/{runId}/{employeeId}.pdf with all sections (happy path) | E2E | Critical | Happy path | AC-1, AC-2, FR-1, FR-2, FR-4, FR-5, FR-7, BR-1 |
| TC-PAY-004-02 | Single payslip download + Download-All ZIP archive | Integration | High | Happy path/Boundary | AC-3, FR-6, BR-5 |
| TC-PAY-004-03 | Regenerate on non-finalized run overwrites PDFs w/ updated template + bumps timestamp | Functional | High | Negative/Alternative | AC-5, FR-1, FR-3, FR-5, FR-7, BR-1, BR-2 |
| TC-PAY-004-04 | Generate for a run NOT in ReviewPending/Approved/Finalized -> 400 | Functional | Critical | Negative/Boundary | AC-1, FR-4, BR-1 |
| TC-PAY-004-05 | Failed render -> pdf_status=Failed, logged, batch continues, individually retryable | Functional | Critical | Negative | AC-1, FR-7, FR-8 |
| TC-PAY-004-06 | PDF <=200KB (NFR-2); BR-5 filename; BR-4 YTD sums; BR-6 terminated final-month payslip | Functional | High | Boundary | AC-1, AC-2, FR-2, FR-5, NFR-2, BR-4, BR-5, BR-6 |
| TC-PAY-004-07 | Path-traversal blocked (NFR-6); no JS/executable content in PDF (NFR-5) | Security | Critical | Negative/Security | AC-4, FR-5, NFR-5, NFR-6 |
| TC-PAY-004-08 | Authz: only Payroll.*.All generate/regenerate/retry/download; others 403; 401 unauth | Security | Critical | Security | AC-1, AC-3, AC-4, AC-5 |
| TC-PAY-004-09 | 5,000 PDFs <=5 min; parallel batch w/ configurable concurrency | Performance | High | Performance | AC-1, NFR-1, NFR-3, FR-4, FR-5 |
| TC-PAY-004-10 | Payslip list table + status bar + inline PDF preview modal + Download All WCAG 2.1 AA | Accessibility | High | Accessibility | AC-1, AC-3 |
| TC-PAY-004-11 | Point-in-time snapshot (BR-2) + disclaimer/footer (BR-3) + per-tenant branding (FR-3); all FR-2 sections | Functional | High | Boundary | AC-2, FR-2, FR-3, BR-2, BR-3 |
| TC-PAY-004-12 | Per-slip pdf_status/timestamp recorded; status-bar reflects progress + failed count | Functional | High | Boundary | AC-1, FR-7 |
| TC-PAY-ISO-013 | Tenant B cannot read/download/enumerate Tenant A payslip PDFs (cross-tenant read iso) | Security | Critical | Multi-tenant isolation | AC-4, FR-5, FR-6 |
| TC-PAY-ISO-014 | Payslip generate/download/preview APIs reject missing/invalid/mismatched tenant context; no IDOR | Security | Critical | Multi-tenant isolation | AC-4, FR-5, FR-6 |
| TC-PAY-ISO-015 | Cross-tenant payslip writes blocked; blob path/slip fields server/job-arg-derived, not client-supplied | Security | Critical | Multi-tenant isolation | AC-4, FR-4, FR-5, FR-7 |
| TC-PAY-ISO-016 | Blob layout / ZIP staging / download cache tenant-scoped (no cross-tenant PDF/path/byte leak) | Security | High | Multi-tenant isolation | AC-4, FR-5, FR-6, NFR-6 |

## Acceptance Criteria Coverage (US-PAY-001)

| AC | Description | Covered By |
|----|-------------|-----------|
| AC-1 | Create component saved with tenant_id, visible to this tenant only | TC-PAY-001-01, -03, -06, -07, -08, -09, -11, -12, TC-PAY-ISO-001 |
| AC-2 | Edit component -> future runs use new def; history unchanged | TC-PAY-001-02, TC-PAY-001-08 |
| AC-3 | Create structure + add components with rules | TC-PAY-001-01, -05, -06, -07, -09, -10, -12 |
| AC-4 | Reorder component processing priority | TC-PAY-001-01, TC-PAY-001-12 |
| AC-5 | Prevent delete of component in use; show affected count | TC-PAY-001-04, TC-PAY-001-08 |
| AC-6 | Tenant A sees only its components; RLS-level isolation | TC-PAY-ISO-001, TC-PAY-ISO-002, TC-PAY-ISO-003, TC-PAY-ISO-004, TC-PAY-001-03 |

## Acceptance Criteria Coverage (US-PAY-002)

| AC | Description | Covered By |
|----|-------------|-----------|
| AC-1 | Assign active structure w/ CTC -> employee_salary_component rows calculated from rules | TC-PAY-002-01, TC-PAY-002-06, TC-PAY-002-08, TC-PAY-002-11, TC-PAY-002-12 |
| AC-2 | Future-dated assignment saved; current remains active until date; revision history maintained | TC-PAY-002-05, TC-PAY-002-09, TC-PAY-002-08, TC-PAY-002-12 |
| AC-3 | Component override saved while others retain calculated values | TC-PAY-002-02, TC-PAY-002-08, TC-PAY-002-12 |
| AC-4 | Bulk assign to multiple employees w/ individual CTC + progress indicator | TC-PAY-002-07, TC-PAY-002-11, TC-PAY-002-08, TC-PAY-002-12 |
| AC-5 | Tenant B cannot access Tenant A employee salary assignment; RLS prevents cross-tenant access | TC-PAY-ISO-005, TC-PAY-ISO-006, TC-PAY-ISO-007, TC-PAY-ISO-008 |

## Acceptance Criteria Coverage (US-PAY-003)

| AC | Description | Covered By |
|----|-------------|-----------|
| AC-1 | Initiate run -> payroll_run Queued + Hangfire job enqueued + 202 with runId | TC-PAY-003-01, TC-PAY-003-03, TC-PAY-003-09, TC-PAY-003-10, TC-PAY-003-12 |
| AC-2 | Worker locks attendance/leave, fetches employees w/ structures, computes earnings/deductions/LOP/statutory/net | TC-PAY-003-01, TC-PAY-003-03, TC-PAY-003-05, TC-PAY-003-06, TC-PAY-003-07 |
| AC-3 | Slips persisted, status -> ReviewPending, HR notified (SignalR + email) | TC-PAY-003-01, TC-PAY-003-08, TC-PAY-003-12 |
| AC-4 | Run for an already-Finalized period -> 409 Conflict | TC-PAY-003-02, TC-PAY-003-08 |
| AC-5 | 5,000-employee run completes within 10 minutes | TC-PAY-003-11 |
| AC-6 | Employee w/o salary structure skipped with warning; run continues | TC-PAY-003-04 |
| AC-7 | Only Tenant A employees included; isolation enforced throughout the compute pipeline | TC-PAY-ISO-009, TC-PAY-ISO-010, TC-PAY-ISO-011, TC-PAY-ISO-012 |

## Acceptance Criteria Coverage (US-PAY-004)

| AC | Description | Covered By |
|----|-------------|-----------|
| AC-1 | Generate Payslips on ReviewPending/Finalized -> Hangfire job stores 1 PDF/employee at {tenantId}/payroll/{runId}/{employeeId}.pdf | TC-PAY-004-01, TC-PAY-004-04, TC-PAY-004-05, TC-PAY-004-06, TC-PAY-004-08, TC-PAY-004-09, TC-PAY-004-10, TC-PAY-004-12 |
| AC-2 | Each PDF contains employee/company details, earnings, deductions, statutory, net, branding | TC-PAY-004-01, TC-PAY-004-06, TC-PAY-004-11 |
| AC-3 | Download All -> ZIP; individual payslip PDFs downloadable per employee | TC-PAY-004-02, TC-PAY-004-08, TC-PAY-004-10 |
| AC-4 | Tenant B denied access to Tenant A payslip storage path; tenant-scoped paths + API isolation | TC-PAY-004-07, TC-PAY-ISO-013, TC-PAY-ISO-014, TC-PAY-ISO-015, TC-PAY-ISO-016 |
| AC-5 | Regenerate on a non-finalized run overwrites existing PDFs with the updated template | TC-PAY-004-03, TC-PAY-004-08 |
