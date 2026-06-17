---
module: Onboarding / Offboarding
total_user_stories: 3
total_test_cases: 47
created: 2026-06-17
updated: 2026-06-17
status: in-progress
---

# Onboarding / Offboarding -- Test Matrix

> US-ONB-001 (Create Onboarding Checklist Template) is the FIRST Onboarding story and establishes `test-cases/onboarding/` (dir + this TEST-MATRIX + the root Onboarding section in TRACEABILITY-MATRIX). It adds 16 test cases: 12 functional/security/performance/accessibility (TC-ONB-001-01..12) + 4 dedicated multi-tenant isolation (TC-ONB-ISO-001..004). The Onboarding module reuses the per-story-suffix functional ID scheme from Recruitment/Payroll/Admin Console (TC-ONB-{NNN}-XX) with a separate running ISO counter (TC-ONB-ISO-NNN) starting at 001. All 5 acceptance criteria of US-ONB-001 are covered.
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
