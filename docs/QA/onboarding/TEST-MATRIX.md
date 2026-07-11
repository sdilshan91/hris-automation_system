---
module: Onboarding / Offboarding
total_user_stories: 6
total_test_cases: 95
created: 2026-06-17
updated: 2026-06-17
status: complete
---

# Onboarding / Offboarding -- Test Matrix

> US-ONB-001 (Create Onboarding Checklist Template) is the FIRST Onboarding story and establishes `docs/QA/onboarding/` (dir + this TEST-MATRIX + the root Onboarding section in TRACEABILITY-MATRIX). It adds 16 test cases: 12 functional/security/performance/accessibility (TC-ONB-001-01..12) + 4 dedicated multi-tenant isolation (TC-ONB-ISO-001..004). The Onboarding module reuses the per-story-suffix functional ID scheme from Recruitment/Payroll/Admin Console (TC-ONB-{NNN}-XX) with a separate running ISO counter (TC-ONB-ISO-NNN) starting at 001. All 5 acceptance criteria of US-ONB-001 are covered.
>
> PLATFORM ACCURACY / DEFERRED: AC-5 and NFR-2 specify PostgreSQL RLS as a tenant-isolation layer. This codebase enforces isolation via **EF Core global query filters (read) + `TenantInterceptor` (write stamping)**, NOT Postgres RLS — RLS is a deferred platform extension point (same family as Auth/Leave/Payroll/Admin). The isolation tests (TC-ONB-ISO-001..004) are written against the EF query-filter + interceptor mechanism in force today; the story's "raw SQL without app.current_tenant_id returns zero rows" RLS expectation is documented as CONDITIONAL/deferred (TC-ONB-ISO-003 step 4). The cross-tenant ID-injection test asserts **404, not 403** (existence not disclosed, TC-ONB-ISO-002). Cache-key scoping (TC-ONB-ISO-004) is asserted as tenant-keyed; if no distributed cache is wired yet, it asserts the equivalent always-tenant-filtered property and flags `onboarding:templates:{tenant_id}` as the target key shape. NFR-1 (<= 500 ms P95) requires a performance-representative environment.
>
> STORY MISMATCH worth flagging to the caller: AC-5/NFR-2 name PostgreSQL RLS as an active isolation layer — only the app (ITenantContext) + EF (query filter / TenantInterceptor) layers exist today; reword RLS as future hardening (consistent with how prior modules were handled).

## Coverage by Test Case

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category |
|-----------|-------|------|----------|--------------------|----------|
| TC-ONB-001-01 | Create template with multiple categories/tasks; persists with correct tenant_id | E2E | Critical | AC-1, AC-2, AC-4, FR-1/2/3/4/5/8, BR-2/3 | Happy path |
| TC-ONB-001-02 | Universal template (empty dept/job-title scope) saved | Functional | High | AC-1, AC-2, FR-4/3/5 | Happy / boundary |
| TC-ONB-001-03 | Duplicate template name rejected (same name OK across tenants) | Functional | Critical | AC-3, BR-1 | Negative |
| TC-ONB-001-04 | Zero-task template rejected (server-side) | Functional | Critical | AC-2, BR-2 | Negative / boundary |
| TC-ONB-001-05 | Negative due_offset_days rejected; 0 accepted | Functional | High | AC-2, BR-3 | Negative / boundary |
| TC-ONB-001-06 | template_name length boundaries (3/200) + due_offset 0 | Functional | Medium | AC-1, AC-2, BR-3 | Boundary |
| TC-ONB-001-07 | Onboarding.Manage required; 403/401 deny, no create | Security | Critical | AC-1, AC-2, FR-5 | Negative / security |
| TC-ONB-001-08 | XSS/SQLi payloads in free-text fields neutralized | Security | High | AC-1, AC-2, FR-2/3 | Negative / security |
| TC-ONB-001-09 | Clone template — new id, tasks duplicated, independent | Functional | High | AC-2, FR-6/3/5/8, BR-1 | Happy path |
| TC-ONB-001-10 | Deactivate/reactivate — soft toggle, removed from assign list | Functional | High | AC-2, FR-7, BR-4 | Functional |
| TC-ONB-001-11 | Create API <= 500 ms P95 | Performance | High | AC-2, NFR-1 | Performance |
| TC-ONB-001-12 | Keyboard-navigable reorder + up/down alt + responsive 360-4K | Accessibility | Medium | AC-1, AC-2, FR-1, NFR-4/3 | Accessibility / cross-browser |
| TC-ONB-ISO-001 | Tenant A cannot see Tenant B templates (cross-tenant READ block) | Security | Critical | AC-5, NFR-2 (EF), BR-1 | Multi-tenant isolation |
| TC-ONB-ISO-002 | Missing/invalid tenant context + cross-tenant ID injection -> 404 | Security | Critical | AC-5, FR-5 | Multi-tenant isolation |
| TC-ONB-ISO-003 | EF query filter blocks reads; writes tenant-stamped (RLS deferred) | Security | Critical | AC-5, FR-5, NFR-2 | Multi-tenant isolation |
| TC-ONB-ISO-004 | Onboarding cache/lookup keys tenant-scoped | Security | High | AC-5, NFR-2 | Multi-tenant isolation |

## Acceptance-Criteria Coverage (US-ONB-001)

| AC | Covered By |
|----|-----------|
| AC-1 (Create Template form: name, description, dept(s), job title(s), task builder) | TC-ONB-001-01, -02, -06, -07, -12 |
| AC-2 (save persists template + tasks with all task fields; tenant_id from session) | TC-ONB-001-01, -02, -04, -05, -06, -09, -10, -11 |
| AC-3 (duplicate name -> "A checklist template with this name already exists.") | TC-ONB-001-03 |
| AC-4 (mandatory-flagged tasks persisted and non-skippable) | TC-ONB-001-01 |
| AC-5 (cross-tenant isolation; only own tenant templates visible) | TC-ONB-ISO-001, -002, -003, -004 |

## FR / NFR / BR Coverage

| Requirement | Covered By |
|-------------|-----------|
| FR-1 (builder w/ drag-and-drop ordering) | TC-ONB-001-01, -12 |
| FR-2 (free-text categories) | TC-ONB-001-01, -08 |
| FR-3 (task fields: title/desc/category/responsible/offset/mandatory/sort) | TC-ONB-001-01, -05, -06, -08 |
| FR-4 (dept/job-title scope or universal) | TC-ONB-001-01, -02 |
| FR-5 (tenant_id from session, never user input) | TC-ONB-001-01, -07, TC-ONB-ISO-002, -003 |
| FR-6 (clone existing template) | TC-ONB-001-09 |
| FR-7 (activate/deactivate soft toggle) | TC-ONB-001-10 |
| FR-8 (audit columns auto-populated) | TC-ONB-001-01, -09, -10 |
| NFR-1 (create API <= 500 ms P95) | TC-ONB-001-11 |
| NFR-2 (tenant isolation; RLS deferred -> EF filters) | TC-ONB-ISO-001, -002, -003, -004 |
| NFR-3 (responsive 360px-4K) | TC-ONB-001-12 |
| NFR-4 (WCAG 2.1 AA, keyboard drag-and-drop) | TC-ONB-001-12 |
| NFR-5 (audit log via SaveChangesInterceptor) | TC-ONB-001-01, -10 (audit columns) |
| BR-1 (name unique per tenant; repeats across tenants) | TC-ONB-001-03, -09, TC-ONB-ISO-001 |
| BR-2 (>= 1 task to save) | TC-ONB-001-04, -01 |
| BR-3 (due offset non-negative integer; 0 = same day) | TC-ONB-001-05, -06 |
| BR-4 (deactivated not assignable, still visible) | TC-ONB-001-10 |
| BR-5 (soft delete) | Out of scope for US-ONB-001 create flow; deletion is a separate story (flag to caller — no delete endpoint in this story) |

## Summary (US-ONB-001)

| Metric | Value |
|--------|-------|
| User stories covered | 1 (US-ONB-001) |
| Total test cases | 16 (12 functional/security/perf/a11y + 4 isolation) |
| AC coverage | 5/5 |
| Functional ID range | TC-ONB-001-01 .. TC-ONB-001-12 |
| ISO ID range | TC-ONB-ISO-001 .. TC-ONB-ISO-004 |

---

## US-ONB-002 — Assign Onboarding Checklist to New Hire

> US-ONB-002 adds 15 test cases: 12 functional/security/performance/accessibility (TC-ONB-002-01..12) + 3 dedicated multi-tenant isolation continuing the shared running counter (TC-ONB-ISO-005..007). The functional suffix counter resets per story (TC-ONB-002-XX) while the ISO counter is module-wide and continues from US-ONB-001's 004. All 5 acceptance criteria of US-ONB-002 are covered.
>
> PLATFORM ACCURACY / DEFERRED (carried from US-ONB-001):
> - NFR-2 names PostgreSQL RLS; this codebase isolates via **EF Core global query filters (read) + `TenantInterceptor` (write stamping)**. ISO tests assert the EF mechanism in force today; the RLS "raw SQL returns zero rows" expectation is CONDITIONAL/deferred (TC-ONB-ISO-007 step 4). Cross-tenant ID injection asserts **404, not 403** (TC-ONB-ISO-006).
> - AC-2/AC-5 describe end-user notification delivery via SignalR + email. Real delivery is owned by the Notifications module (US-NTF-001 in-app, US-NTF-002 email). The onboarding side of the contract is tested as **outbox intent rows written transactionally (NFR-3) + Hangfire dispatch job enqueued** (TC-ONB-002-06); end-to-end SignalR/email receipt is deferred to the US-NTF test cases.
> - NFR-1 (assignment API <= 1000 ms P95) requires a performance-representative environment (TC-ONB-002-11); on a dev box, record indicative numbers and do NOT relax the threshold.
> - NFR-5 idempotency is asserted as "retry within the same session yields the same checklist, no duplicates" (TC-ONB-002-08); if no idempotency key/mechanism is wired yet, flag to caller.

### Coverage by Test Case (US-ONB-002)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category |
|-----------|-------|------|----------|--------------------|----------|
| TC-ONB-002-01 | Assign 5-task template; 5 instances, due=join+offset, pending, tenant_id | E2E | Critical | AC-1, AC-2, FR-1/2/3/7/8, BR-2 | Happy path |
| TC-ONB-002-02 | Auto-filter by dept/job title + universal; deactivated excluded | Functional | High | AC-1, FR-1, BR-1 | Happy / boundary |
| TC-ONB-002-03 | Duplicate assignment replace/merge prompt; one active checklist | Functional | Critical | AC-3, BR-2, FR-2 | Happy / negative |
| TC-ONB-002-04 | Modify after assign: add ad-hoc, re-date, soft-delete; mandatory protected | Functional | High | AC-4, FR-5/6/8, BR-3 | Happy / negative |
| TC-ONB-002-05 | Responsible-party resolution Manager/HR/IT/Employee | Functional | High | AC-2, FR-3, FR-7 | Happy path |
| TC-ONB-002-06 | Notification dispatch via OUTBOX (Manager+IT) — intent rows + Hangfire | Integration | High | AC-2, AC-5, FR-4, NFR-3 | Happy path |
| TC-ONB-002-07 | Past joining date -> due dates from today | Functional | High | AC-2, FR-2, BR-4 | Boundary |
| TC-ONB-002-08 | Negative/boundary: inactive template, missing employee/template, idempotent retry | Functional | Critical | AC-1, AC-2, FR-2/7, NFR-5, BR-1 | Negative / boundary |
| TC-ONB-002-09 | Onboarding.Manage required; 401/403 deny, no create/modify | Security | Critical | AC-1, AC-2, AC-4, FR-3/5/7 | Negative / security |
| TC-ONB-002-10 | XSS/SQLi in ad-hoc task free-text neutralized | Security | High | AC-2, AC-4, FR-5 | Negative / security |
| TC-ONB-002-11 | Assignment API <= 1000 ms P95 | Performance | High | AC-2, NFR-1, NFR-3 | Performance |
| TC-ONB-002-12 | Keyboard-navigable + responsive 360px-4K assignment UI | Accessibility | Medium | AC-1, AC-2, AC-3, FR-1/6, NFR-4 | Accessibility / cross-browser |
| TC-ONB-ISO-005 | Tenant A cannot see Tenant B assignments (cross-tenant READ block) | Security | Critical | AC-2, NFR-2 (EF) | Multi-tenant isolation |
| TC-ONB-ISO-006 | Missing tenant context + cross-tenant ID injection -> 404 | Security | Critical | AC-2, FR-7 | Multi-tenant isolation |
| TC-ONB-ISO-007 | EF query filter blocks reads; writes+outbox tenant-stamped (RLS deferred) | Security | Critical | AC-2, AC-5, FR-3/7, NFR-2/3 | Multi-tenant isolation |

### Acceptance-Criteria Coverage (US-ONB-002)

| AC | Covered By |
|----|-----------|
| AC-1 (filtered template list shown for the employee) | TC-ONB-002-01, -02, -08, -09, -12 |
| AC-2 (task instances created: due date, pending, responsible party; notifications sent) | TC-ONB-002-01, -05, -06, -07, -08, -09, -10, -11, -12, TC-ONB-ISO-005, -006, -007 |
| AC-3 (already-has-checklist warning with replace/merge) | TC-ONB-002-03, -12 |
| AC-4 (add/remove tasks after assignment; soft-delete; audit) | TC-ONB-002-04, -09, -10 |
| AC-5 (Manager + IT notifications dispatched) | TC-ONB-002-06, TC-ONB-ISO-007 |

### FR / NFR / BR Coverage (US-ONB-002)

| Requirement | Covered By |
|-------------|-----------|
| FR-1 (auto-filter templates by dept/job title) | TC-ONB-002-01, -02, -12 |
| FR-2 (create instances; due = date_of_joining + offset) | TC-ONB-002-01, -03, -07, -08 |
| FR-3 (resolve responsible parties Manager/HR/IT/Employee) | TC-ONB-002-01, -05, -09, TC-ONB-ISO-007 |
| FR-4 (dispatch notifications via SignalR + email/Hangfire) | TC-ONB-002-06 |
| FR-5 (add ad-hoc tasks) | TC-ONB-002-04, -09, -10 |
| FR-6 (modify due dates after assignment) | TC-ONB-002-04, -12 |
| FR-7 (tenant_id from session on all instances) | TC-ONB-002-01, -05, -08, -09, TC-ONB-ISO-006, -007 |
| FR-8 (assignment tracked as audit event) | TC-ONB-002-01, -04 |
| NFR-1 (assignment API <= 1000 ms P95) | TC-ONB-002-11 |
| NFR-2 (tenant isolation; RLS deferred -> EF filters) | TC-ONB-ISO-005, -006, -007 |
| NFR-3 (notification outbox pattern, transactional) | TC-ONB-002-06, -11, TC-ONB-ISO-007 |
| NFR-4 (responsive 360px-4K + WCAG 2.1 AA) | TC-ONB-002-12 |
| NFR-5 (idempotent retry within session) | TC-ONB-002-08 |
| BR-1 (only active templates assignable) | TC-ONB-002-02, -08 |
| BR-2 (at most one active checklist; replace = new version) | TC-ONB-002-01, -03 |
| BR-3 (mandatory tasks cannot be removed) | TC-ONB-002-04 |
| BR-4 (past joining date -> due dates from today) | TC-ONB-002-07 |
| BR-5 (Employee-role tasks visible only after user account linked) | Out of scope for the assignment flow; depends on account-linking (flag to caller — no account-linking step in this story) |

### Summary (US-ONB-002)

| Metric | Value |
|--------|-------|
| User stories covered | 1 (US-ONB-002) |
| Total test cases | 15 (12 functional/security/perf/a11y + 3 isolation) |
| AC coverage | 5/5 |
| Functional ID range | TC-ONB-002-01 .. TC-ONB-002-12 |
| ISO ID range | TC-ONB-ISO-005 .. TC-ONB-ISO-007 (shared module counter) |

---

## US-ONB-003 — New Hire Completes Onboarding Tasks

> US-ONB-003 adds 16 test cases: 12 functional/security/performance/accessibility (TC-ONB-003-01..12) + 4 dedicated multi-tenant isolation continuing the shared running counter (TC-ONB-ISO-008..011). The functional suffix counter resets per story (TC-ONB-003-XX) while the ISO counter is module-wide and continues from US-ONB-002's 007. All 5 acceptance criteria of US-ONB-003 are covered.
>
> PLATFORM ACCURACY / DEFERRED (carried from the US-ONB-001/002 family):
> - NFR-2 names PostgreSQL RLS; this codebase isolates via **EF Core global query filters (read) + `TenantInterceptor` (write stamping)**. ISO tests assert the EF mechanism in force today; the RLS "raw SQL returns zero rows" expectation is CONDITIONAL/deferred (TC-ONB-ISO-010 step 4). Cross-tenant ID injection asserts **404, not 403** (TC-ONB-ISO-009).
> - AC-3/AC-4/AC-5 describe end-user notification delivery via SignalR + email. Real delivery is owned by the Notifications module (US-NTF-001 in-app, US-NTF-002 email). The onboarding side of the contract is tested as **notification intent rows (outbox) raised on completion / overdue detection + Hangfire job execution** (TC-ONB-003-01, -04, -09); end-to-end SignalR/email receipt is deferred to the US-NTF test cases.
> - NFR-3 (ClamAV malware scan before persistence) is asserted at the SEAM level — TC-ONB-003-05 step 4 exercises the scan hook with an EICAR test file; live ClamAV integration is CONDITIONAL/deferred (flag to caller if not wired).
> - NFR-1 (checklist load API <= 500 ms P95) requires a performance-representative environment (TC-ONB-003-11); on a dev box, record indicative numbers and do NOT relax the threshold.
> - Document storage path `{tenantId}/onboarding/{employeeId}/{taskId}/{filename}` (AC-4) is the contract under test; progress cache, if wired, targets `onboarding:progress:{tenant_id}:{employee_id}` (TC-ONB-ISO-011).

### Coverage by Test Case (US-ONB-003)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category |
|-----------|-------|------|----------|--------------------|----------|
| TC-ONB-003-01 | Complete 3 of 5 tasks -> 60%; completion timestamp+actor; HR intent; audit | E2E | Critical | AC-1, AC-3, FR-2/4/8 | Happy path |
| TC-ONB-003-02 | Dashboard widget: %/pending/completed/overdue + checklist link (own tasks only) | Functional | High | AC-1, FR-1/4 | Happy / boundary |
| TC-ONB-003-03 | Checklist grouped by category; status chips; overdue red highlight | Functional | High | AC-2, AC-5, FR-1 | Happy / boundary |
| TC-ONB-003-04 | Upload valid PDF stored at tenant path; task completed w/ file ref; HR notified | Integration | Critical | AC-4, FR-3/8, NFR-6 | Happy path |
| TC-ONB-003-05 | Upload >10MB rejected; bad MIME rejected; malware-scan seam (ClamAV deferred) | Security | Critical | AC-4, FR-3, NFR-3/6 | Negative / boundary / security |
| TC-ONB-003-06 | Role restriction: employee cannot complete IT/Manager/HR task | Security | Critical | AC-2, FR-7, BR-1 | Negative / security |
| TC-ONB-003-07 | Employee cannot revert a completed task (HR-only reopen) | Functional | High | AC-3, BR-3 | Negative / security |
| TC-ONB-003-08 | Mandatory gating: optional done, one mandatory left -> not fully complete | Functional | Critical | AC-3, FR-4, BR-2 | Negative / boundary |
| TC-ONB-003-09 | Overdue Hangfire job -> overdue + outbox to employee/HR/manager (once/day) | Integration | High | AC-5, FR-6, BR-4 | Happy / boundary |
| TC-ONB-003-10 | Self-service authz: act only on own tasks; 401 unauth; XSS/SQLi neutralized | Security | Critical | AC-2, AC-3, FR-1/2/7 | Negative / security |
| TC-ONB-003-11 | Checklist load API <= 500 ms P95 | Performance | High | AC-1, AC-2, NFR-1 | Performance |
| TC-ONB-003-12 | Keyboard nav + screen-reader status announcements + 360px mobile upload | Accessibility | Medium | AC-1, AC-2, NFR-4/5 | Accessibility / cross-browser |
| TC-ONB-ISO-008 | Tenant A cannot see Tenant B tasks/progress/docs (cross-tenant READ block) | Security | Critical | AC-1, AC-2, NFR-2 (EF) | Multi-tenant isolation |
| TC-ONB-ISO-009 | Missing tenant context + cross-tenant ID injection -> 404 | Security | Critical | AC-2, AC-3, FR-7, NFR-2 | Multi-tenant isolation |
| TC-ONB-ISO-010 | EF filter blocks reads; completions/uploads/outbox tenant-stamped (RLS deferred) | Security | Critical | AC-3, AC-5, FR-7/8, NFR-2 | Multi-tenant isolation |
| TC-ONB-ISO-011 | Progress cache + document storage keys tenant-scoped | Security | High | AC-4, FR-4, NFR-2/6 | Multi-tenant isolation |

### Acceptance-Criteria Coverage (US-ONB-003)

| AC | Covered By |
|----|-----------|
| AC-1 (dashboard "Onboarding Progress" widget: %/pending/completed/overdue + link) | TC-ONB-003-01, -02, -11, -12, TC-ONB-ISO-008 |
| AC-2 (checklist grouped by category; fields + status + responsible party; overdue red) | TC-ONB-003-03, -06, -10, -11, -12, TC-ONB-ISO-008, -009 |
| AC-3 (mark complete: status/timestamp/actor recorded; progress updates; HR notified) | TC-ONB-003-01, -07, -08, -10, TC-ONB-ISO-009, -010 |
| AC-4 (document upload stored at tenant path; task completed w/ file ref; HR notified) | TC-ONB-003-04, -05, TC-ONB-ISO-011 |
| AC-5 (overdue red highlight; automated overdue notification to employee/HR/manager) | TC-ONB-003-03, -09, TC-ONB-ISO-010 |

### FR / NFR / BR Coverage (US-ONB-003)

| Requirement | Covered By |
|-------------|-----------|
| FR-1 (personalized checklist; only own assigned tasks) | TC-ONB-003-02, -03, -10, TC-ONB-ISO-008 |
| FR-2 (mark own tasks complete w/ optional comment) | TC-ONB-003-01, -10 |
| FR-3 (file upload with MIME + size validation) | TC-ONB-003-04, -05 |
| FR-4 (overall progress percentage completed/total) | TC-ONB-003-01, -02, -08, -11, TC-ONB-ISO-011 |
| FR-5 (real-time SignalR notify HR + manager on completion) | TC-ONB-003-01, -04 (outbox intent; delivery deferred to US-NTF-001/002) |
| FR-6 (daily Hangfire job detects overdue; notifies employee/HR/manager) | TC-ONB-003-09 |
| FR-7 (prevent completing tasks assigned to other roles) | TC-ONB-003-06, -10, TC-ONB-ISO-009, -010 |
| FR-8 (log task completion in tenant audit log) | TC-ONB-003-01, -04, TC-ONB-ISO-010 |
| NFR-1 (checklist load API <= 500 ms P95) | TC-ONB-003-11 |
| NFR-2 (tenant isolation; RLS deferred -> EF filters) | TC-ONB-ISO-008, -009, -010, -011 |
| NFR-3 (uploads malware-scanned via ClamAV before persistence) | TC-ONB-003-05 (seam; live ClamAV deferred) |
| NFR-4 (responsive 360px-4K, mobile-first) | TC-ONB-003-12 |
| NFR-5 (WCAG 2.1 AA) | TC-ONB-003-12 |
| NFR-6 (uploads limited to 10 MB, configurable) | TC-ONB-003-04, -05, TC-ONB-ISO-011 |
| BR-1 (employee completes only Employee-role tasks; others read-only) | TC-ONB-003-06 |
| BR-2 (mandatory tasks done before "fully complete") | TC-ONB-003-08 |
| BR-3 (completed task not revertible by employee; HR-only reopen) | TC-ONB-003-07 |
| BR-4 (overdue notifications once/day at tenant-configurable time) | TC-ONB-003-09 |
| BR-5 (document retention = employment + tenant policy) | Out of scope for the task-completion flow; retention/lifecycle is a separate concern (flag to caller — no retention-expiry step in this story) |

### Summary (US-ONB-003)

| Metric | Value |
|--------|-------|
| User stories covered | 1 (US-ONB-003) |
| Total test cases | 16 (12 functional/security/perf/a11y + 4 isolation) |
| AC coverage | 5/5 |
| Functional ID range | TC-ONB-003-01 .. TC-ONB-003-12 |
| ISO ID range | TC-ONB-ISO-008 .. TC-ONB-ISO-011 (shared module counter) |

---

## US-ONB-004 — Asset Issuance Tracking During Onboarding

> US-ONB-004 adds 16 test cases: 12 functional/security/performance/accessibility (TC-ONB-004-01..12) + 4 dedicated multi-tenant isolation continuing the shared running counter (TC-ONB-ISO-012..015). The functional suffix counter resets per story (TC-ONB-004-XX) while the ISO counter is module-wide and continues from US-ONB-003's 011. All 5 acceptance criteria of US-ONB-004 are covered.
>
> PLATFORM ACCURACY / DEFERRED (carried from the US-ONB-001/002/003 family):
> - AC-5/NFR-2 name PostgreSQL RLS; this codebase isolates via **EF Core global query filters (read) + `TenantInterceptor` (write stamping)**. ISO tests assert the EF mechanism in force today; the RLS "raw SQL without app.current_tenant_id -> zero rows" expectation is CONDITIONAL/deferred (TC-ONB-ISO-014 step 4). Cross-tenant ID injection asserts **404, not 403** (TC-ONB-ISO-013).
> - NFR-4 (acknowledgment uploads scanned for malware) is asserted at the SEAM level — TC-ONB-004-06 step 5 exercises the scan hook with an EICAR test file; live ClamAV integration is CONDITIONAL/deferred (same family as TC-ONB-003-05; flag to caller if not wired).
> - Document storage key `{tenantId}/onboarding/{employeeId}/assets/{assetId}/{filename}` is the tenant-isolated contract under test (TC-ONB-004-06, TC-ONB-ISO-015); any asset lookup cache targets `onboarding:assets:{tenant_id}:{employee_id}` (TC-ONB-ISO-015) if wired.
> - NFR-1 (issuance API <= 600 ms P95) requires a performance-representative environment (TC-ONB-004-11); on a dev box, record indicative numbers and do NOT relax the threshold.
>
> STORY MISMATCH / SCOPE NOTES worth flagging to the caller: (1) AC-5/NFR-2 name PostgreSQL RLS as an active isolation layer — only the app (ITenantContext) + EF (query filter / TenantInterceptor) layers exist today; reword RLS as future hardening. (2) BR-4 (returned asset reverts to available/disposed) and BR-5 (asset register soft delete) describe lifecycle states beyond this issuance story — return/soft-delete have NO endpoint in the issuance flow; the "available" gate (FR-3, TC-ONB-004-05) exercises non-available statuses as inputs, but the return/disposal/soft-delete transitions belong to a later offboarding/asset-lifecycle story. (3) BR-2 (asset types configurable per tenant via Tenant Admin master data) is assumed satisfied via preconditions; type-configuration itself is an Admin Console concern. (4) AC-2/FR-2 "Asset Management module (lite)" register fields are assumed present; full lifecycle (depreciation/maintenance) is explicitly out of Phase-1 scope per S10.

### Coverage by Test Case (US-ONB-004)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category |
|-----------|-------|------|----------|--------------------|----------|
| TC-ONB-004-01 | Issue laptop + ID card; both linked, status assigned, task completed, audit before/after | E2E | Critical | AC-1, AC-2, FR-1/2/4/8 | Happy path |
| TC-ONB-004-02 | Bulk: 3 assets in one submission persisted in a SINGLE transaction (atomic rollback) | Integration | Critical | AC-1, AC-2, FR-5, NFR-5 | Happy / boundary |
| TC-ONB-004-03 | Double-assignment rejected — current-holder message shown | Functional | Critical | AC-3, BR-1, FR-3/4 | Negative |
| TC-ONB-004-04 | Unique asset_tag (and serial if provided) per tenant — duplicate rejected | Functional | Critical | BR-3, FR-2 | Negative / boundary |
| TC-ONB-004-05 | Available-status gate — cannot issue non-"available" asset | Functional | Critical | FR-3, FR-4, BR-1/4 | Negative / boundary |
| TC-ONB-004-06 | Acknowledgment upload at tenant path; >10MB/bad MIME rejected; malware-scan seam | Integration | Critical | FR-6, NFR-4 | Negative / boundary / security |
| TC-ONB-004-07 | issue_date cannot be in the future (today inclusive, past ok) | Functional | High | FR-1, data: issue_date | Negative / boundary |
| TC-ONB-004-08 | Employee self-service assets/me read-only; cannot issue/modify | Security | Critical | AC-4, BR-6 | Negative / security |
| TC-ONB-004-09 | Onboarding.Manage required to issue; 401/403 deny, no record | Security | Critical | FR-1/4, BR-6 | Negative / security |
| TC-ONB-004-10 | XSS/SQLi in free-text neutralized; client tenant_id ignored (session wins) | Security | High | FR-7, data: notes 500 | Negative / boundary / security |
| TC-ONB-004-11 | Issuance API <= 600 ms P95 | Performance | High | NFR-1, NFR-5 | Performance |
| TC-ONB-004-12 | Issuance form keyboard navigable + 360px mobile + WCAG 2.1 AA | Accessibility | Medium | NFR-3 | Accessibility / cross-browser |
| TC-ONB-ISO-012 | Tenant A cannot see Tenant B assets/issuances (cross-tenant READ block) | Security | Critical | AC-5, NFR-2 (EF) | Multi-tenant isolation |
| TC-ONB-ISO-013 | Missing tenant context + cross-tenant asset ID injection -> 404 | Security | Critical | AC-5, FR-7 | Multi-tenant isolation |
| TC-ONB-ISO-014 | EF filter blocks reads; writes tenant-stamped; uniqueness per tenant (RLS deferred) | Security | Critical | AC-5, FR-7, NFR-2/5, BR-3 | Multi-tenant isolation |
| TC-ONB-ISO-015 | Acknowledgment storage keys + asset lookup cache tenant-scoped | Security | High | AC-5, NFR-2/4 | Multi-tenant isolation |

### Acceptance-Criteria Coverage (US-ONB-004)

| AC | Covered By |
|----|-----------|
| AC-1 (issuance form: type/tag/serial/condition/issue date; multiple assets per session) | TC-ONB-004-01, -02, -07, -12 |
| AC-2 (save: asset linked, status->assigned, task->completed, audit before/after) | TC-ONB-004-01, -02 |
| AC-3 (already-assigned asset -> current-holder rejection message) | TC-ONB-004-03 |
| AC-4 (employee profile "Assets" tab lists own assets: type/serial/issue date/condition) | TC-ONB-004-08 |
| AC-5 (cross-tenant isolation; no Tenant A assets visible to Tenant B) | TC-ONB-ISO-012, -013, -014, -015 |

### FR / NFR / BR Coverage (US-ONB-004)

| Requirement | Covered By |
|-------------|-----------|
| FR-1 (issuance form linked to asset_issuance task) | TC-ONB-004-01, -07, -09, -12 |
| FR-2 (asset register fields) | TC-ONB-004-01, -04 |
| FR-3 (validate "available" before issuance) | TC-ONB-004-03, -05 |
| FR-4 (set "assigned" + link employee on issuance) | TC-ONB-004-01, -03, -05, -09 |
| FR-5 (bulk issuance, multiple assets one submission) | TC-ONB-004-02 |
| FR-6 (attach acknowledgment document) | TC-ONB-004-06 |
| FR-7 (tenant_id from session on all asset records) | TC-ONB-004-10, TC-ONB-ISO-013, -014 |
| FR-8 (issuance audited with before/after state) | TC-ONB-004-01 |
| NFR-1 (issuance API <= 600 ms P95) | TC-ONB-004-11 |
| NFR-2 (tenant isolation; RLS deferred -> EF filters) | TC-ONB-ISO-012, -013, -014, -015 |
| NFR-3 (responsive 360px-4K) | TC-ONB-004-12 |
| NFR-4 (acknowledgment <= 10 MB + malware-scanned) | TC-ONB-004-06, TC-ONB-ISO-015 |
| NFR-5 (atomic: status update + linkage in single transaction) | TC-ONB-004-02, -11, TC-ONB-ISO-014 |
| BR-1 (asset assigned to only one employee at a time) | TC-ONB-004-03, -05 |
| BR-2 (asset types configurable per tenant) | Assumed via preconditions (Admin Console master data) — TC-ONB-004-01 uses configured types; type-config itself out of this story (flag to caller) |
| BR-3 (asset_tag/serial unique per tenant) | TC-ONB-004-04, TC-ONB-ISO-014 |
| BR-4 (returned asset -> available/disposed) | Partial: TC-ONB-004-05 exercises "returned"/"disposed" as non-issuable inputs; the return/disposal TRANSITION has no endpoint in this issuance story (flag to caller — offboarding/lifecycle story) |
| BR-5 (asset register soft delete) | Out of scope for the issuance flow; no delete endpoint here (flag to caller — separate lifecycle story) |
| BR-6 (employee self-service view read-only) | TC-ONB-004-08, -09 |

### Summary (US-ONB-004)

| Metric | Value |
|--------|-------|
| User stories covered | 1 (US-ONB-004) |
| Total test cases | 16 (12 functional/security/perf/a11y + 4 isolation) |
| AC coverage | 5/5 |
| Functional ID range | TC-ONB-004-01 .. TC-ONB-004-12 |
| ISO ID range | TC-ONB-ISO-012 .. TC-ONB-ISO-015 (shared module counter) |

---

## US-ONB-005 — Offboarding / Exit Checklist and Clearance

> US-ONB-005 adds 16 test cases: 12 functional/security/performance/accessibility (TC-ONB-005-01..12) + 4 dedicated multi-tenant isolation continuing the shared running counter (TC-ONB-ISO-016..019). The functional suffix counter resets per story (TC-ONB-005-XX) while the ISO counter is module-wide and continues from US-ONB-004's 015. All 6 acceptance criteria of US-ONB-005 are covered.
>
> PLATFORM ACCURACY / DEFERRED (carried from the US-ONB-001/002/003/004 family):
> - AC-6/NFR-2 name PostgreSQL RLS; this codebase isolates via **EF Core global query filters (read) + `TenantInterceptor` (write stamping)**. ISO tests assert the EF mechanism in force today; the RLS "raw SQL without app.current_tenant_id -> zero rows" expectation is CONDITIONAL/deferred (TC-ONB-ISO-018 step 4). Cross-tenant ID injection asserts **404, not 403** (TC-ONB-ISO-017).
> - SESSION REVOCATION (FR-7 + S10) specifies SignalR disconnect + a **Redis JWT denylist**. A Redis token denylist is NOT yet wired (same deferral family as prior modules). TC-ONB-005-06 asserts the revocation effect in force today — the user account is DEACTIVATED so the old JWT fails the active-account check (401); the Redis denylist hit is CONDITIONAL/deferred and flagged to the caller. NFR-3 (deactivation + revocation <= 30 s, TC-ONB-005-11) is measured against the deactivation effect plus any wired revocation.
> - F&F SETTLEMENT (FR-6, BR-4) is owned by the Payroll module; offboarding only TRIGGERS the notification. TC-ONB-005-01/-19 assert the F&F trigger notification is dispatched (tenant-stamped); actual settlement calculation is out of scope (Payroll).
> - NOTIFICATION DELIVERY (clearance approvals, F&F trigger) ultimately routes through the Notifications module (US-NTF-001 in-app, US-NTF-002 email). The offboarding side is asserted as the trigger/intent + Payroll-queue dispatch; end-to-end receipt is deferred to US-NTF.
> - NFR-1 (initiation API <= 1000 ms P95, TC-ONB-005-11) requires a performance-representative environment; on a dev box, record indicative numbers and do NOT relax the threshold.
> - Offboarding lookup cache (if wired) targets `onboarding:offboarding:{tenant_id}:{employee_id}` (TC-ONB-ISO-019).
>
> STORY MISMATCHES / SCOPE NOTES worth flagging to the caller: (1) AC-6/NFR-2 RLS claim — reword as future hardening (EF filters + TenantInterceptor in force today). (2) FR-7 Redis JWT denylist not yet wired — revocation asserted via account deactivation; recommend the denylist as a follow-up so unexpired tokens are hard-revoked even before account-check propagation. (3) Manager-role exit tasks resolve via employee `reporting_manager_id`; if unset, the Manager clearance/handover task has no resolvable owner (same gap noted on US-ONB-002 FR-3) — recommend a clear unresolved-party warning. (4) BR-6 irreversibility is asserted as "no reactivation path" (TC-ONB-005-06 step 5); if an admin override exists it must be flagged as a deviation from BR-6.

### Coverage by Test Case (US-ONB-005)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category |
|-----------|-------|------|----------|--------------------|----------|
| TC-ONB-005-01 | Full happy path: initiate -> clear all -> complete; terminated + account deactivated + F&F trigger + audit | E2E | Critical | AC-1, AC-4, FR-2/5/6/9, BR-5/6 | Happy path |
| TC-ONB-005-02 | Exit task generation for HR/IT/Finance/Manager/Employee; due = LWD - offset | Functional | Critical | AC-1, FR-2/3, FR-8 | Happy / boundary |
| TC-ONB-005-03 | Asset return -> register status Available/Disposed; task complete; before/after audit | Integration | Critical | AC-2, BR-3, FR-3/9 | Happy path |
| TC-ONB-005-04 | Clearance dashboard: 4 depts, approve 2 / pending 2 -> not fully cleared; traffic lights | Functional | High | AC-3, FR-4, BR-2 | Happy / boundary |
| TC-ONB-005-05 | Blocked completion: pending mandatory tasks -> block with explicit pending list | Functional | Critical | AC-5, BR-2 | Negative / boundary |
| TC-ONB-005-06 | Completion effects: old JWT -> 401 (deactivation; Redis denylist deferred); irreversible | Integration | Critical | FR-7, FR-5, BR-6 | Negative / security |
| TC-ONB-005-07 | BR-1 status gate: cannot initiate for active employee; only accepted statuses | Functional | Critical | BR-1, AC-1 | Negative / boundary |
| TC-ONB-005-08 | LWD today-or-future boundary; reason enum; notes <= 2000; employee must exist | Functional | High | AC-1, data (LWD/reason/notes) | Negative / boundary |
| TC-ONB-005-09 | Authz: HR required for initiate/clearance/complete; 401/403; XSS/SQLi neutralized | Security | Critical | AC-1/3/4, FR-5/9 | Negative / security |
| TC-ONB-005-10 | Audit: each clearance decision + final completion logged, attributable, tenant-scoped | Integration | High | FR-9, AC-2/3/4 | Happy / security |
| TC-ONB-005-11 | Initiation API <= 1000 ms P95 (NFR-1); deactivation + revocation <= 30 s (NFR-3) | Performance | High | NFR-1, NFR-3 | Performance |
| TC-ONB-005-12 | Clearance dashboard keyboard navigable + WCAG 2.1 AA; 360px Kanban -> accordion | Accessibility | Medium | AC-3, AC-4, NFR-4/5 | Accessibility / cross-browser |
| TC-ONB-ISO-016 | Tenant A cannot see Tenant B offboarding records (cross-tenant READ block) | Security | Critical | AC-6, NFR-2 (EF) | Multi-tenant isolation |
| TC-ONB-ISO-017 | Missing tenant context + cross-tenant offboarding ID injection -> 404 | Security | Critical | AC-6, FR-8 | Multi-tenant isolation |
| TC-ONB-ISO-018 | EF filter blocks reads; writes/clearance/audit tenant-stamped (RLS deferred) | Security | Critical | AC-6, FR-8, NFR-2 | Multi-tenant isolation |
| TC-ONB-ISO-019 | Offboarding lookup cache + F&F notification payload tenant-scoped | Security | High | AC-6, FR-6/8, NFR-2 | Multi-tenant isolation |

### Acceptance-Criteria Coverage (US-ONB-005)

| AC | Covered By |
|----|-----------|
| AC-1 (initiate -> exit checklist for HR/IT/Finance/Manager/Employee; due = LWD - offset) | TC-ONB-005-01, -02, -07, -08, -09, -11 |
| AC-2 (asset return -> register Available/Disposed; task complete; audit) | TC-ONB-005-03, -10 |
| AC-3 (clearance dashboard; fully cleared only when all depts approved; traffic lights) | TC-ONB-005-04, -09, -10, -12 |
| AC-4 (complete -> terminated + account deactivated + F&F trigger to Payroll) | TC-ONB-005-01, -06, -09, -10, -11, -12 |
| AC-5 (block completion with pending mandatory tasks; list pending items) | TC-ONB-005-05 |
| AC-6 (cross-tenant isolation; Tenant B sees no Tenant A offboarding data) | TC-ONB-ISO-016, -017, -018, -019 |

### FR / NFR / BR Coverage (US-ONB-005)

| Requirement | Covered By |
|-------------|-----------|
| FR-1 (offboarding checklist templates per tenant) | TC-ONB-005-01, -02 |
| FR-2 (auto-generate exit tasks; due = LWD - offset_days) | TC-ONB-005-01, -02 |
| FR-3 (built-in clearance categories IT/Finance/Admin/Manager) | TC-ONB-005-02, -03, -04 |
| FR-4 (clearance dashboard with green/red/yellow indicators) | TC-ONB-005-04, -12 |
| FR-5 (deactivate user account on completion) | TC-ONB-005-01, -06, -09, -11 |
| FR-6 (trigger F&F settlement notification to Payroll) | TC-ONB-005-01, TC-ONB-ISO-019 |
| FR-7 (revoke active sessions; SignalR + Redis denylist) | TC-ONB-005-06, -11 (denylist deferred -> deactivation in force) |
| FR-8 (tenant_id from session on all offboarding records) | TC-ONB-005-02, TC-ONB-ISO-017, -018, -019 |
| FR-9 (record all offboarding actions in tenant audit log) | TC-ONB-005-01, -03, -09, -10 |
| NFR-1 (initiation API <= 1000 ms P95) | TC-ONB-005-11 |
| NFR-2 (tenant isolation; RLS deferred -> EF filters) | TC-ONB-ISO-016, -017, -018, -019 |
| NFR-3 (deactivation + revocation <= 30 s of completion) | TC-ONB-005-06, -11 |
| NFR-4 (clearance dashboard responsive 360px-4K) | TC-ONB-005-12 |
| NFR-5 (WCAG 2.1 AA) | TC-ONB-005-12 |
| BR-1 (initiate only for resignation_accepted/terminated/contract_ended) | TC-ONB-005-07 |
| BR-2 (all mandatory clearances approved before completion) | TC-ONB-005-04, -05 |
| BR-3 (asset-return tasks auto-update asset register) | TC-ONB-005-03 |
| BR-4 (F&F calc owned by Payroll; offboarding only triggers) | TC-ONB-005-01, TC-ONB-ISO-019 (trigger only; calc out of scope) |
| BR-5 (data retained; only account deactivated) | TC-ONB-005-01 |
| BR-6 (completion irreversible; no reactivation) | TC-ONB-005-06 |

### Summary (US-ONB-005)

| Metric | Value |
|--------|-------|
| User stories covered | 1 (US-ONB-005) |
| Total test cases | 16 (12 functional/security/perf/a11y + 4 isolation) |
| AC coverage | 6/6 |
| Functional ID range | TC-ONB-005-01 .. TC-ONB-005-12 |
| ISO ID range | TC-ONB-ISO-016 .. TC-ONB-ISO-019 (shared module counter) |

---

## US-ONB-006 — Exit Interview Recording

> US-ONB-006 (Exit Interview Recording) is the SIXTH and FINAL Onboarding story; it COMPLETES the module. It adds 16 test cases: 12 functional/security/performance/accessibility (TC-ONB-006-01..12) + 4 dedicated multi-tenant isolation continuing the shared running counter (TC-ONB-ISO-020..023). The functional suffix counter resets per story (TC-ONB-006-XX) while the ISO counter is module-wide and continues from US-ONB-005's 019. All 5 acceptance criteria of US-ONB-006 are covered.
>
> PLATFORM ACCURACY / DEFERRED (carried from the US-ONB-001/002/003/004/005 family):
> - AC-5/NFR-2 name PostgreSQL RLS; this codebase isolates via **EF Core global query filters (read) + `TenantInterceptor` (write stamping)**. ISO tests assert the EF mechanism in force today; the RLS "raw SQL without app.current_tenant_id -> zero rows" expectation is CONDITIONAL/deferred (TC-ONB-ISO-022 step 4). Cross-tenant ID injection asserts **404, not 403** (TC-ONB-ISO-021).
> - AC-3/FR-8 describe HR notification on self-service submission via SignalR + email. Real delivery is owned by the Notifications module (US-NTF-001 in-app, US-NTF-002 email). The onboarding side of the contract is tested as a **notification INTENT row written transactionally (outbox) + Hangfire dispatch enqueued** (TC-ONB-006-02, TC-ONB-ISO-023); end-to-end SignalR/email receipt is deferred to the US-NTF test cases.
> - NFR-1 (form load <= 500 ms P95) and NFR-3 (analytics render <= 2 s for up to 1000 interviews) require a performance-representative environment (TC-ONB-006-11); on a dev box, record indicative numbers and do NOT relax the thresholds.
> - Analytics cache (if wired) targets `onboarding:exit-analytics:{tenant_id}` (TC-ONB-ISO-023); if no cache is wired, the equivalent always-tenant-filtered property is asserted.

### Coverage by Test Case (US-ONB-006)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category |
|-----------|-------|------|----------|--------------------|----------|
| TC-ONB-006-01 | HR-conducted: record 10-question interview; responses persist w/ tenant_id + offboarding linkage; task completed; audit | E2E | Critical | AC-1, AC-2, FR-1/3/6/7 | Happy path |
| TC-ONB-006-02 | Self-service: employee completes questionnaire; saved + linked; HR-notify outbox intent (delivery deferred) | Integration | Critical | AC-3, FR-2/8 | Happy path |
| TC-ONB-006-03 | Duplicate exit interview per offboarding rejected | Functional | Critical | BR-1, AC-2 | Negative |
| TC-ONB-006-04 | Immutability/versioning: edit after submit preserves original + creates new version | Functional | Critical | BR-2, FR-3/7 | Negative |
| TC-ONB-006-05 | Analytics: 10 varied-reason interviews -> reason pie + avg ratings/category correct, tenant-scoped | Functional | High | AC-4, FR-4, BR-4 | Happy / boundary |
| TC-ONB-006-06 | Anonymization: aggregates only; free-text hidden without ExitInterview.ViewDetail; PII access audit-flagged | Security | Critical | FR-5, NFR-6 | Negative / security |
| TC-ONB-006-07 | Self-service deadline: after LWD / account deactivated -> access denied; HR path remains | Functional | High | BR-3 | Negative / boundary / security |
| TC-ONB-006-08 | Boundary/negative: rating 1-5, interview_date not future, free_text<=2000, additional_comments<=5000, required answers, mode enum, conducted_by | Functional | Critical | FR-1, AC-2, data (S7) | Negative / boundary |
| TC-ONB-006-09 | Authz: HR for record/analytics; self-service own-offboarding only; 401/403 | Security | Critical | AC-2/3/4, FR-2/5 | Negative / security |
| TC-ONB-006-10 | XSS/SQLi free-text neutralized; offboarding_id/question_id tenant-belonging; client tenant_id ignored | Security | High | FR-6, data (S7) | Negative / security |
| TC-ONB-006-11 | Form load <= 500 ms P95 (NFR-1); analytics render <= 2 s for 1000 interviews (NFR-3) | Performance | High | NFR-1, NFR-3 | Performance |
| TC-ONB-006-12 | Questionnaire keyboard navigable + WCAG 2.1 AA; 360px touch-friendly rating; responsive to 4K | Accessibility | Medium | NFR-4, NFR-5 | Accessibility / cross-browser |
| TC-ONB-ISO-020 | Tenant A cannot see Tenant B exit interviews or analytics (cross-tenant READ block) | Security | Critical | AC-5, NFR-2 (EF), BR-4 | Multi-tenant isolation |
| TC-ONB-ISO-021 | Missing tenant context + cross-tenant exit-interview ID injection -> 404 | Security | Critical | AC-5, FR-6 | Multi-tenant isolation |
| TC-ONB-ISO-022 | EF filter blocks reads; writes/versions/outbox/audit tenant-stamped (RLS deferred) | Security | Critical | AC-5, FR-6, NFR-2 | Multi-tenant isolation |
| TC-ONB-ISO-023 | Exit interview analytics cache + HR-notify outbox payload tenant-scoped | Security | High | AC-5, FR-8, NFR-2 | Multi-tenant isolation |

### Acceptance-Criteria Coverage (US-ONB-006)

| AC | Covered By |
|----|-----------|
| AC-1 (questionnaire opens, categorized, pre-loaded from tenant template) | TC-ONB-006-01, -12 |
| AC-2 (responses persist against offboarding w/ tenant_id; exit-interview task -> completed) | TC-ONB-006-01, -03, -08, -09 |
| AC-3 (self-service: same questionnaire; saved + linked; HR notified) | TC-ONB-006-02, -07, -09 |
| AC-4 (analytics: reason pie, avg ratings/category, trends; tenant-scoped) | TC-ONB-006-05, -06, -11 |
| AC-5 (cross-tenant isolation; Tenant B sees no Tenant A exit interview data) | TC-ONB-ISO-020, -021, -022, -023 |

### FR / NFR / BR Coverage (US-ONB-006)

| Requirement | Covered By |
|-------------|-----------|
| FR-1 (configurable template: rating 1-5 / multiple choice / free text / yes-no) | TC-ONB-006-01, -08, -12 |
| FR-2 (HR-conducted + self-service modes) | TC-ONB-006-01, -02, -09 |
| FR-3 (link responses to offboarding record) | TC-ONB-006-01, -04, TC-ONB-ISO-022 |
| FR-4 (aggregated analytics: distribution, averages, trends) | TC-ONB-006-05 |
| FR-5 (anonymize individual responses unless ExitInterview.ViewDetail) | TC-ONB-006-06, -09 |
| FR-6 (tenant_id from session on all records) | TC-ONB-006-01, -10, TC-ONB-ISO-021, -022 |
| FR-7 (exit interview completion as audit event) | TC-ONB-006-01, -04 |
| FR-8 (notify HR on self-service submit; SignalR delivery deferred to US-NTF) | TC-ONB-006-02, TC-ONB-ISO-023 |
| NFR-1 (form load <= 500 ms P95) | TC-ONB-006-11 |
| NFR-2 (tenant isolation; RLS deferred -> EF filters) | TC-ONB-ISO-020, -021, -022, -023 |
| NFR-3 (analytics render <= 2 s for up to 1000 interviews) | TC-ONB-006-11 |
| NFR-4 (responsive 360px-4K) | TC-ONB-006-12 |
| NFR-5 (WCAG 2.1 AA) | TC-ONB-006-12 |
| NFR-6 (free-text PII access flagged in audit) | TC-ONB-006-06 |
| BR-1 (one exit interview per offboarding instance) | TC-ONB-006-03 |
| BR-2 (immutable after submit; edits create a new version, original preserved) | TC-ONB-006-04 |
| BR-3 (self-service must be submitted before LWD) | TC-ONB-006-07 |
| BR-4 (analytics show only current-tenant data) | TC-ONB-006-05, TC-ONB-ISO-020 |
| BR-5 (retention per tenant policy; anonymized data may be kept longer for trends) | Out of scope for the recording/analytics flow; data-retention/expiry is a separate lifecycle concern (flag to caller — no retention-expiry step in this story) |
| BR-6 (questionnaire template configurable by Tenant Admins) | Assumed via preconditions (Admin Console / template config); template-authoring itself is out of this recording story (flag to caller) |

### Summary (US-ONB-006)

| Metric | Value |
|--------|-------|
| User stories covered | 1 (US-ONB-006) |
| Total test cases | 16 (12 functional/security/perf/a11y + 4 isolation) |
| AC coverage | 5/5 |
| Functional ID range | TC-ONB-006-01 .. TC-ONB-006-12 |
| ISO ID range | TC-ONB-ISO-020 .. TC-ONB-ISO-023 (shared module counter) |

---

## Module Total (Onboarding / Offboarding — COMPLETE)

| Metric | Value |
|--------|-------|
| User stories covered | 6 (US-ONB-001 .. US-ONB-006) |
| Total test cases | 95 (72 functional/security/perf/a11y + 23 isolation TC-ONB-ISO-001..023) |
| AC coverage | 31/31 (5+5+5+5+6+5) |
| Functional ID scheme | TC-ONB-{NNN}-01..12 (per-story suffix) |
| ISO ID range | TC-ONB-ISO-001 .. TC-ONB-ISO-023 (module-wide running counter) |
