---
module: Admin Console
total_user_stories: 4
total_test_cases: 76
created: 2026-06-16
updated: 2026-06-17
status: in-progress
---

# Admin Console -- Test Matrix

> US-ADM-001 (System Admin Provisions New Tenant) is the FIRST Admin Console story and establishes `test-cases/admin-console/` (dir + this TEST-MATRIX + the root Admin Console section in TRACEABILITY-MATRIX). It adds 16 test cases: 12 functional/security/performance/accessibility (TC-ADM-001-01..12) + 4 dedicated multi-tenant isolation (TC-ADM-ISO-001..004). The Admin Console module reuses the per-story-suffix functional ID scheme from Recruitment/Payroll (TC-ADM-{NNN}-XX) with a separate running ISO counter (TC-ADM-ISO-NNN) starting at 001. All 6 acceptance criteria of US-ADM-001 are covered.
>
> PLATFORM ACCURACY / DEFERRED: AC-6 and FR-6 of US-ADM-001 specify PostgreSQL RLS + the `app.current_tenant_id` session variable. This codebase enforces tenant isolation via **EF Core global query filters (read) + `TenantInterceptor` (write stamping)**, NOT Postgres RLS -- RLS is a deferred platform extension point. The isolation test cases (TC-ADM-001-09, TC-ADM-ISO-001..004) are written against the EF query-filter mechanism in force today; the story's "raw SQL without app.current_tenant_id returns zero rows" RLS-verification hint (FR-6) is documented as CONDITIONAL/deferred. The cross-tenant ID-injection test asserts **404, not 403** per the story Test Hints (existence not disclosed). FR-7 Redis tenant-config cache (TC-ADM-ISO-004) is asserted as tenant-keyed; if no distributed cache layer is wired yet, the test asserts the equivalent always-tenant-filtered property and flags the Redis key as the target. Welcome-email DELIVERY (FR-4) is asserted against a test SMTP sink (the dispatch/enqueue is the assertion). NFR-1 (<60s/<5min) requires a performance-representative environment.

## Coverage by Test Case

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category |
|-----------|-------|------|----------|--------------------|----------|
| TC-ADM-001-01 | Provision new tenant end-to-end | E2E | Critical | AC-1, AC-4, FR-1/3/4/5/7, BR-3/4/5 | Happy path |
| TC-ADM-001-02 | Existing global user linked, not duplicated | Functional | High | AC-3, AC-1, FR-3/4, BR-4 | Existing-user link / boundary |
| TC-ADM-001-03 | Duplicate subdomain rejected (incl. terminated) | Functional | Critical | AC-2, FR-2, BR-2 | Negative |
| TC-ADM-001-04 | Every reserved subdomain rejected | Functional | High | AC-2, FR-2 | Negative (data-driven) |
| TC-ADM-001-05 | Invalid subdomain formats rejected | Functional | High | AC-5, FR-1 | Negative |
| TC-ADM-001-06 | Subdomain length boundaries (3 and 50) accepted | Functional | Medium | AC-5, AC-1, FR-1 | Boundary |
| TC-ADM-001-07 | trial_days = 0 -> active vs > 0 -> trial | Functional | High | AC-1, FR-1, BR-3 | Boundary |
| TC-ADM-001-08 | Only SystemAdmin may provision; SystemSupport denied | Security | Critical | BR-1, BR-5, NFR-3 | Negative / security |
| TC-ADM-001-09 | Cross-tenant ID injection -> 404 not 403 | Security | Critical | AC-6, FR-6 (EF), Test Hints | Multi-tenant isolation |
| TC-ADM-001-10 | Provisioning within < 60s / < 5min SLA | Performance | High | NFR-1, FR-3/7 | Performance |
| TC-ADM-001-11 | Create Tenant form WCAG 2.1 AA + responsive 360-4K | Accessibility | Medium | NFR-4, S8 | Accessibility |
| TC-ADM-001-12 | Idempotent provisioning -- retry never duplicates | Integration | Critical | NFR-2, FR-3/4 | Negative / idempotency |
| TC-ADM-ISO-001 | New tenant data isolated (cross-tenant READ block) | Security | Critical | AC-6, FR-6 (EF), BR-1 | Multi-tenant isolation |
| TC-ADM-ISO-002 | API rejects missing/invalid/mismatched tenant context + IDOR | Security | Critical | AC-6, BR-1 | Multi-tenant isolation |
| TC-ADM-ISO-003 | EF query filter blocks reads; writes tenant-stamped (RLS deferred) | Security | Critical | AC-6, FR-3/6 | Multi-tenant isolation |
| TC-ADM-ISO-004 | Tenant config cache key is tenant-scoped | Security | High | AC-6, FR-7 | Multi-tenant isolation |

## Acceptance-Criteria Coverage (US-ADM-001)

| AC | Covered By |
|----|-----------|
| AC-1 (full provisioning: tenant+user+user_tenant+TenantOwner+seed+lifecycle 'created'+audit+welcome email) | TC-ADM-001-01, -02, -06, -07 |
| AC-2 (duplicate + reserved subdomain rejected) | TC-ADM-001-03, -04 |
| AC-3 (existing global user linked, no duplicate) | TC-ADM-001-02 |
| AC-4 (tenant in list; lifecycle 'created'; system_audit_log) | TC-ADM-001-01 |
| AC-5 (invalid subdomain formats rejected, client + server) | TC-ADM-001-05, -06 |
| AC-6 (tenant resolution + complete data isolation) | TC-ADM-001-09, TC-ADM-ISO-001, -002, -003, -004 |

## NFR / BR Coverage

| Requirement | Covered By |
|-------------|-----------|
| NFR-1 (<60s target / <5min SLA) | TC-ADM-001-10 |
| NFR-2 (idempotent retry, no duplicates) | TC-ADM-001-12 |
| NFR-3 (audit log of provisioning incl. denied) | TC-ADM-001-01, -08 |
| NFR-4 (WCAG 2.1 AA + responsive 360-4K) | TC-ADM-001-11 |
| BR-1 (only SystemAdmin; SystemSupport denied) | TC-ADM-001-08, TC-ADM-ISO-001/002 |
| BR-2 (subdomain not reusable after termination) | TC-ADM-001-03, -12 |
| BR-3 (trial_days -> status) | TC-ADM-001-07 |
| BR-4 (owner email -> billing_email) | TC-ADM-001-01, -02 |
| BR-5 (is_system=false; system tenant not creatable) | TC-ADM-001-01, -08 |

## Summary (US-ADM-001)

| Metric | Value |
|--------|-------|
| User stories covered | 1 (US-ADM-001) |
| Total test cases | 16 (12 functional/security/perf/a11y + 4 isolation) |
| AC coverage | 6/6 |
| Functional ID range | TC-ADM-001-01 .. TC-ADM-001-12 |
| ISO ID range | TC-ADM-ISO-001 .. TC-ADM-ISO-004 |

---

> US-ADM-002 (System Admin Monitors Platform Health and Tenant Usage) is the SECOND Admin Console story. It adds 20 test cases: 18 functional/security/perf/a11y (TC-ADM-002-01..18) + 2 dedicated multi-tenant isolation (TC-ADM-ISO-005..006, continuing the running ISO counter). All 5 acceptance criteria of US-ADM-002 are covered.
>
> PLATFORM ACCURACY / DEFERRED: this platform does NOT yet have the observability infra the story assumes (OpenTelemetry metrics + usage counters). The implementation builds these FOR REAL and they are tested as run-green: platform-health roll-up, active tenant/user counts, tenant-status breakdown, DB/Redis health (Redis may show "not configured"), Hangfire job counts, per-tenant EMPLOYEE usage gauge vs `MaxEmployees` limit, the employee quota-breach queue (80/95/100%), tenant detail operational fields (status/plan/owner/created/last-activity/Hangfire), access control, PII exclusion, audit logging, and POLLING refresh (NOT SignalR). The following are DEFERRED / "not available" and are written as `status: blocked` with the expected behavior being a "Not available — requires observability pipeline" placeholder (NEVER fabricated data): aggregate error-rate % + P95 latency KPIs (TC-ADM-002-14), the error-rate "Attention Required" queue (TC-ADM-002-15), the tenant-detail 24h error/latency trend charts + top-errors (TC-ADM-002-16), SLA uptime % (TC-ADM-002-17), and storage/API/email usage gauges (TC-ADM-002-18). Refresh is via polling: AC-1's "SignalR or polling" is satisfied by polling, and NFR-2 (SignalR push <5s) is deferred (noted in TC-ADM-002-07/-12).
>
> STORY MISMATCH worth flagging to the caller: US-ADM-002 Preconditions/AC-1/FR-1 assume OpenTelemetry metrics are operational and that error rate, latency, SLA uptime, and storage/API/email usage are available — none of which exist yet. The story should be split so the deferred observability metrics are a follow-on once the OpenTelemetry pipeline + Redis usage counters land; the run-green subset (health roll-up, counts, DB/Redis/Hangfire, employee quota, tenant detail, access/PII/audit, polling) is what is implementable today.

## Coverage by Test Case (US-ADM-002)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category | Status |
|-----------|-------|------|----------|--------------------|----------|--------|
| TC-ADM-002-01 | Dashboard loads w/ health roll-up + aggregate counts | E2E | Critical | AC-1, FR-1(subset)/5/6 | Happy path | draft |
| TC-ADM-002-02 | Employee gauge 80% warning (max=5, 4 emp) | Functional | Critical | AC-2, FR-2/3 | Boundary | draft |
| TC-ADM-002-03 | Employee gauge 100% breach (max=5, 5 emp) | Functional | Critical | AC-2, FR-2/3, BR-4 | Boundary / negative | draft |
| TC-ADM-002-04 | Quota-breach queue by severity (80/95/100% employees) | Functional | High | AC-2, FR-3, BR-4 | Boundary | draft |
| TC-ADM-002-05 | DB/Redis health indicators (Redis "not configured") | Functional | High | AC-1, FR-6 | Negative / boundary | draft |
| TC-ADM-002-06 | Hangfire job counts surfaced (queued/proc/succ/failed) | Functional | High | AC-1, FR-5 | Happy path | draft |
| TC-ADM-002-07 | Polling refresh updates "last updated" (not SignalR) | Functional | Medium | AC-1, FR-1(refresh) | Happy path | draft |
| TC-ADM-002-08 | Tenant detail operational fields (status/plan/owner/created/activity/Hangfire) | Functional | High | AC-4, FR-5 | Happy path | draft |
| TC-ADM-002-09 | Access control: SysAdmin full / SysSupport read-only / Tenant Admin 403 | Security | Critical | AC-5, BR-1 | Negative / security | draft |
| TC-ADM-002-10 | PII exclusion — aggregates only, no names/salaries | Security | Critical | AC-5, BR-2 | Negative / security | draft |
| TC-ADM-002-11 | Audit: Monitoring.Viewed + Monitoring.TenantViewed | Security | High | AC-5, NFR-5 | Security | draft |
| TC-ADM-002-12 | Dashboard < 2.5s P95 with 100+ tenants | Performance | High | NFR-1, NFR-3, AC-1 | Performance | draft |
| TC-ADM-002-13 | Dashboard WCAG 2.1 AA + responsive 1024px-4K | Accessibility | Medium | NFR-4 | Accessibility | draft |
| TC-ADM-002-14 | [DEFERRED] error-rate % + P95 latency KPIs | Functional | High | AC-1, FR-1 | Deferred placeholder | blocked |
| TC-ADM-002-15 | [DEFERRED] error-rate "Attention Required" queue | Functional | High | AC-3, FR-1 | Deferred placeholder | blocked |
| TC-ADM-002-16 | [DEFERRED] tenant 24h error/latency trends + top errors | Functional | Medium | AC-4, FR-1 | Deferred placeholder | blocked |
| TC-ADM-002-17 | [DEFERRED] SLA uptime % vs plan tier | Functional | Medium | FR-7 | Deferred placeholder | blocked |
| TC-ADM-002-18 | [DEFERRED] storage/API/email usage gauges | Functional | Medium | AC-2, FR-2 | Deferred placeholder | blocked |
| TC-ADM-ISO-005 | Monitoring aggregates correctly tenant-scoped; no row leakage | Security | Critical | AC-5, BR-1/2 | Multi-tenant isolation | draft |
| TC-ADM-ISO-006 | Monitoring endpoints reject non-system tenant context | Security | Critical | AC-5, BR-1 | Multi-tenant isolation | draft |

## Acceptance-Criteria Coverage (US-ADM-002)

| AC | Covered By | Notes |
|----|-----------|-------|
| AC-1 (health roll-up, error rate, P95, active tenants/users, DB/Redis health, Hangfire depth, auto-refresh) | TC-ADM-002-01, -05, -06, -07 (real) + TC-ADM-002-14 (DEFERRED: error rate, P95) | Real subset run-green; error-rate/latency deferred |
| AC-2 (per-tenant usage gauges; 80% warn / 100% breach) | TC-ADM-002-02, -03, -04 (employee, real) + TC-ADM-002-18 (DEFERRED: storage/API/email) | Employee dimension real; others deferred |
| AC-3 (error-rate "Attention Required" queue) | TC-ADM-002-15 (DEFERRED) | Fully deferred — needs OTel error metrics |
| AC-4 (tenant detail: status/plan/owner/created/activity/jobs + trends/top errors) | TC-ADM-002-08 (operational, real) + TC-ADM-002-16 (DEFERRED: trends/top errors) | Operational fields real; analytics deferred |
| AC-5 (no PII; aggregates only; all access audited) | TC-ADM-002-09, -10, -11, TC-ADM-ISO-005, -006 | Direct |

## NFR / BR / FR Coverage (US-ADM-002)

| Requirement | Covered By | Notes |
|-------------|-----------|-------|
| NFR-1 (dashboard < 2.5s P95, 100+ tenants) | TC-ADM-002-12 | Direct |
| NFR-2 (SignalR push < 5s) | -- | DEFERRED — refresh is polling (TC-ADM-002-07 notes) |
| NFR-3 (no prod DB perf impact) | TC-ADM-002-12 | Direct |
| NFR-4 (responsive 1024px-4K, a11y) | TC-ADM-002-13 | Direct |
| NFR-5 (all access audited incl. which tenant viewed) | TC-ADM-002-11 | Direct |
| FR-1 (real-time health from OTel + health checks) | TC-ADM-002-01, -05, -06 (real) / -07, -14, -15, -16 | Partly deferred (OTel) |
| FR-2 (usage gauges from plan limits + counters) | TC-ADM-002-02, -03 (employee) / -18 (DEFERRED) | Employee real; counters deferred |
| FR-3 (quota breach queue 80/95/100%) | TC-ADM-002-03, -04 | Employee dimension real |
| FR-5 (Hangfire cross-tenant job status + drilldown) | TC-ADM-002-06, -08 | Direct |
| FR-6 (DB/Redis health from /health/ready) | TC-ADM-002-05 | Direct |
| FR-7 (SLA uptime % per tenant) | TC-ADM-002-17 (DEFERRED) | Fully deferred — needs probe history |
| BR-1 (only SystemAdmin/SystemSupport; support read-only) | TC-ADM-002-09, TC-ADM-ISO-006 | Direct |
| BR-2 (no tenant PII in monitoring) | TC-ADM-002-10, TC-ADM-ISO-005 | Direct |
| BR-4 (95% -> alert/notification) | TC-ADM-002-03, -04 | Threshold tier verified; notification dispatch is Notification module |

## Summary (US-ADM-002)

| Metric | Value |
|--------|-------|
| User stories covered | 1 (US-ADM-002) |
| Total test cases | 20 (18 functional/security/perf/a11y + 2 isolation) |
| AC coverage | 5/5 (AC-3 and parts of AC-1/AC-2/AC-4 are DEFERRED placeholders pending observability) |
| Run-green now | 15 (TC-ADM-002-01..13, ISO-005, ISO-006) |
| Deferred (status: blocked) | 5 (TC-ADM-002-14, -15, -16, -17, -18) |
| Functional ID range | TC-ADM-002-01 .. TC-ADM-002-18 |
| ISO ID range | TC-ADM-ISO-005 .. TC-ADM-ISO-006 |

---

> US-ADM-003 (System Admin Impersonates Tenant User, With Audit) is the THIRD Admin Console story. It adds 17 test cases: 13 functional/security/accessibility (TC-ADM-003-01..13) + 3 DEFERRED placeholders (TC-ADM-003-14..16) + 1 dedicated multi-tenant isolation (TC-ADM-ISO-007, continuing the running ISO counter). All 6 acceptance criteria (AC-1..AC-6) and all 6 business rules (BR-1..BR-6) are traced.
>
> IMPLEMENTATION FACTS (tested as built): impersonation mints a SEPARATE JWT for the target user with claims `is_impersonation`, `imp_session_id`, `imp_actor_id`, `imp_reason`, `imp_readonly`, `imp_expires_at`; TTL hard-capped at 60 min and NOT refreshable (NFR-2). Read-only is decided at START (SystemSupport role OR Suspended tenant -> read-only) and enforced by a MediatR pipeline behavior that 403s write Commands (AC-5/AC-6/BR-1). Destructive ops (change/reset password, role/permission mutation, delete user/tenant) are 403'd even for a FULL SystemAdmin impersonation (FR-6); end-session is always allowed. A dedicated `impersonation_sessions` table (FR-4) tracks session_id/impersonator/target/reason/started/ended/expires/actions_count/status (Active/Ended/Expired); end + 60-min expiry are enforced by a per-request middleware. Both a system AuditLog ("Impersonation.Started"/"Impersonation.Ended") and tenant-scoped audit rows carry impersonator attribution (`ImpersonatorUserId`, `ImpersonationSessionId`, `IsImpersonationAction`). The FE banner (NFR-4) is a global, high-contrast, i18n-driven top bar in the main layout shown when `is_impersonation` is true, with an End Session button. BR-2 excludes system-tenant users; BR-3 one active session per impersonator (409); BR-5 excludes terminated tenants. Cross-tenant access during impersonation returns 404 (not 403), per the module convention.
>
> DEFERRED (status: blocked; never fabricated, honest traceability): NFR-1 audit immutability via DB-role UPDATE/DELETE revocation (TC-ADM-003-14) — deferred platform/infra, same family as the deferred Postgres RLS; today audit is append-only BY CONVENTION (insert-only app paths). Real email + in-app notification DELIVERY (AC-4/FR-5, TC-ADM-003-15) — log-only dispatch seam until US-NTF; the DISPATCH is asserted run-green in TC-ADM-003-01. NFR-5 traceId end-to-end correlation (TC-ADM-003-16) — depends on the observability/OTel stack deferred in US-ADM-002.

## Coverage by Test Case (US-ADM-003)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category | Status |
|-----------|-------|------|----------|--------------------|----------|--------|
| TC-ADM-003-01 | Start session: token claims, Active row, dual audit, notification dispatched | E2E | Critical | AC-1, FR-1/2/4/5, BR-4 | Happy path | draft |
| TC-ADM-003-02 | Reason validation < 10 chars rejected; stored verbatim; in notification | Functional | High | AC-1, FR-1, BR-4 | Negative / boundary | draft |
| TC-ADM-003-03 | Read-only: SUSPENDED tenant blocks writes (403), allows reads | Security | Critical | AC-5, FR-3, BR-1 | Negative / security | draft |
| TC-ADM-003-04 | Read-only: SystemSupport always read-only (write -> 403) | Security | Critical | AC-6, FR-3, BR-1 | Negative / security | draft |
| TC-ADM-003-05 | Destructive ops blocked even for FULL admin impersonation (403) | Security | Critical | AC-2, FR-3/6, BR-1 | Negative / security | draft |
| TC-ADM-003-06 | End session: Ended status, "Impersonation.Ended" audit, token rejected after | Functional | Critical | AC-3, FR-4 | Happy path / negative | draft |
| TC-ADM-003-07 | Expiry: past 60-min ExpiresAt rejected; token not refreshable | Security | Critical | AC-3, NFR-2 | Negative / boundary | draft |
| TC-ADM-003-08 | BR-2: cannot impersonate a system-tenant user | Security | High | BR-2 | Negative / security | draft |
| TC-ADM-003-09 | BR-3: second concurrent session rejected (409) | Functional | High | BR-3, FR-4 | Negative | draft |
| TC-ADM-003-10 | BR-5: terminated tenant rejected | Security | High | BR-5, Preconditions | Negative / boundary | draft |
| TC-ADM-003-11 | Access control: only SysAdmin/SysSupport initiate; tenant user 403, unauth 401 | Security | Critical | AC-6, BR-1 | Negative / security | draft |
| TC-ADM-003-12 | Banner: persistent, non-dismissable, un-overridable; i18n + End Session | Accessibility | High | AC-2, NFR-4, BR-6 | Accessibility / security | draft |
| TC-ADM-003-13 | Audit attribution: actions carry impersonator id + session id (both logs) | Security | Critical | AC-2, FR-3/4 | Happy path / security | draft |
| TC-ADM-003-14 | [DEFERRED] Audit immutability via DB-role UPDATE/DELETE revocation | Security | High | NFR-1 | Deferred placeholder | blocked |
| TC-ADM-003-15 | [DEFERRED] Tenant-admin notification DELIVERY (email + in-app) | Integration | High | AC-4, FR-5 | Deferred placeholder | blocked |
| TC-ADM-003-16 | [DEFERRED] traceId end-to-end correlation of impersonation events | Integration | Medium | NFR-5 | Deferred placeholder | blocked |
| TC-ADM-ISO-007 | Tenant A impersonation cannot reach Tenant B data (404) | Security | Critical | FR-6, BR-1, Test Hints | Multi-tenant isolation | draft |

## Acceptance-Criteria Coverage (US-ADM-003)

| AC | Covered By | Notes |
|----|-----------|-------|
| AC-1 (mint time-limited imp JWT with imp claims; open tenant subdomain) | TC-ADM-003-01, -02 | Direct |
| AC-2 (every action dual-audited w/ impersonator id; persistent banner) | TC-ADM-003-13, -12, -05 | Direct |
| AC-3 (60-min expiry or End -> revoke, return to console, end audit) | TC-ADM-003-06, -07 | Direct |
| AC-4 (tenant-admin notification of session start) | TC-ADM-003-01 (dispatch, real) + TC-ADM-003-15 (DEFERRED: delivery) | Dispatch real; delivery deferred to US-NTF |
| AC-5 (suspended tenant -> read-only, no write API) | TC-ADM-003-03 | Direct |
| AC-6 (SystemSupport -> read-only; write 403; only system roles initiate) | TC-ADM-003-04, -11 | Direct |

## NFR / BR / FR Coverage (US-ADM-003)

| Requirement | Covered By | Notes |
|-------------|-----------|-------|
| NFR-1 (audit immutable; DB role no UPDATE/DELETE on audit) | TC-ADM-003-14 (DEFERRED) | Append-only by convention today; DB-role revocation deferred (RLS family) |
| NFR-2 (60-min TTL cap; not refreshable) | TC-ADM-003-07, -01 (step 3) | Direct |
| NFR-3 (start < 2s) | -- | Not separately tested; requires perf-representative env (flagged) |
| NFR-4 (global banner, un-overridable by tenant CSS) | TC-ADM-003-12 | Direct |
| NFR-5 (traceId end-to-end correlation) | TC-ADM-003-16 (DEFERRED) | Depends on deferred observability stack (US-ADM-002) |
| FR-1/FR-2 (start endpoint contract + imp JWT claims) | TC-ADM-003-01, -02 | Direct |
| FR-3 (middleware: expiry, audit attribution, restrict destructive ops) | TC-ADM-003-05, -07, -13 | Direct |
| FR-4 (impersonation_sessions tracking record) | TC-ADM-003-01, -06, -09, -13 | Direct |
| FR-5 (tenant-admin notification template, email+in-app) | TC-ADM-003-01 (dispatch) + -15 (DEFERRED delivery) | Dispatch real; delivery deferred |
| FR-6 (no destructive ops / no cross-tenant data under impersonation) | TC-ADM-003-05, TC-ADM-ISO-007 | Direct |
| BR-1 (only SysAdmin/SysSupport; support read-only) | TC-ADM-003-04, -11, ISO-007 | Direct |
| BR-2 (system-tenant users not impersonatable) | TC-ADM-003-08 | Direct |
| BR-3 (one active session per impersonator) | TC-ADM-003-09 | Direct |
| BR-4 (reason mandatory, >= 10 meaningful chars, verbatim, in notification) | TC-ADM-003-02, -01 | Direct |
| BR-5 (terminated tenants excluded) | TC-ADM-003-10 | Direct |
| BR-6 (banner i18n in all tenant languages) | TC-ADM-003-12 | Direct |

## Summary (US-ADM-003)

| Metric | Value |
|--------|-------|
| User stories covered | 1 (US-ADM-003) |
| Total test cases | 17 (13 functional/security/a11y + 3 DEFERRED + 1 isolation) |
| AC coverage | 6/6 (AC-4 delivery portion DEFERRED; dispatch covered) |
| BR coverage | 6/6 (BR-1..BR-6) |
| Run-green now | 14 (TC-ADM-003-01..13 + TC-ADM-ISO-007) |
| Deferred (status: blocked) | 3 (TC-ADM-003-14, -15, -16) |
| Functional ID range | TC-ADM-003-01 .. TC-ADM-003-16 |
| ISO ID range | TC-ADM-ISO-007 |

---

> US-ADM-004 (System Admin Suspends or Terminates a Tenant) is the FOURTH Admin Console story. It adds 23 test cases: 17 functional/security/e2e/a11y (TC-ADM-004-01..17) + 4 DEFERRED placeholders (TC-ADM-004-18..21) + 2 dedicated multi-tenant isolation (TC-ADM-ISO-008..009, continuing the running ISO counter). All 6 acceptance criteria (AC-1..AC-6), all 7 business rules (BR-1..BR-7), and all 7 functional requirements (FR-1..FR-7) are traced.
>
> IMPLEMENTATION FACTS (tested as built): the tenant gains `SuspendedAt`/`SuspendedReason`/`TerminationScheduledAt`. Transitions enforce BR-1's allowed-state matrix; invalid transitions are rejected with 409/400 (TC-ADM-004-12). SUSPEND -> `Suspended`, revokes all tenant refresh tokens (BR-5), writes lifecycle `'suspended'` + system audit, dispatches a log-only notification; suspended-tenant API returns HTTP 451 to tenant users except Tenant Admin, and suspended login allows only Tenant Admin/Owner (AC-2). TERMINATE -> `Terminating`, `TerminationScheduledAt = now + graceDays` (7-90, default 30, BR-4), schedules the data-deletion job + 14/7/1d reminder jobs, lifecycle `'termination_initiated'`; Terminating is read-only (writes -> 403, BR-6). The DATA-DELETION job (AC-4) hard-deletes per-tenant data, retains the tenant row as `Terminated` with PII redacted, retains audit logs, is tenant-isolated (Tenant B untouched). REACTIVATE (AC-5): Suspended -> Active, fields cleared, `'reactivated'`. RESTORE (AC-6): Terminating -> prior state, `TerminationScheduledAt` cleared, scheduled jobs de-queued, `'restored'`. BR-2: system tenant cannot be suspended/terminated. BR-3: Terminated cannot be restored. BR-7: only SystemAdmin transitions; SystemSupport views lifecycle history only. Typed-subdomain confirmation (FR-4) + no-paste (NFR-5) are frontend (TC-ADM-004-17, FE-verified).
>
> DEFERRED (status: blocked; never fabricated, honest traceability): real email DELIVERY of lifecycle/reminder notifications (TC-ADM-004-18) — log-only dispatch seam until US-NTF; the DISPATCH/SCHEDULING is asserted run-green in TC-ADM-004-01/-06/-10. File-storage (blob) deletion (TC-ADM-004-19) — §10 requires deleting documents/resumes/payslips but no blob storage is wired; the relational hard-delete is covered run-green in TC-ADM-004-09. NFR-3 maintenance-window scheduling (TC-ADM-004-20) — no window config today; deletion fires at grace expiry. NFR-2 50k-record/10-min perf (TC-ADM-004-21) — needs a perf-representative environment; correctness covered by TC-ADM-004-09.

## Coverage by Test Case (US-ADM-004)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category | Status |
|-----------|-------|------|----------|--------------------|----------|--------|
| TC-ADM-004-01 | Suspend active tenant: status, fields, tokens revoked, 451, lifecycle+audit, notification dispatched | E2E | Critical | AC-1, FR-1/7, BR-1/5 | Happy path | draft |
| TC-ADM-004-02 | Suspend past_due tenant (alt valid source) | Functional | High | AC-1, FR-1/7, BR-1/5 | Boundary | draft |
| TC-ADM-004-03 | Suspension reason 10-500 boundary; <10/empty/>500 rejected | Functional | High | AC-1, FR-1 | Negative / boundary | draft |
| TC-ADM-004-04 | Login during suspension: Tenant Admin allowed (read-only); others blocked | Security | Critical | AC-2 | Negative / security | draft |
| TC-ADM-004-05 | Suspended API -> 451 for tenant users; Tenant Admin exempt | Security | Critical | AC-1, AC-2 | Negative / security | draft |
| TC-ADM-004-06 | Terminate active tenant: Terminating, scheduled_at, deletion+reminder jobs, lifecycle | E2E | Critical | AC-3, FR-2/7, BR-1/4 | Happy path | draft |
| TC-ADM-004-07 | Terminate from past_due and suspended (alt valid sources) | Functional | High | AC-3, FR-2/7, BR-1 | Boundary | draft |
| TC-ADM-004-08 | Terminating read-only: writes 403, reads + export OK | Security | Critical | AC-3, BR-6 | Negative / security | draft |
| TC-ADM-004-09 | Data deletion: hard-delete, tenant retained Terminated + PII redacted, audit retained, atomic | Integration | Critical | AC-4, FR-3/7, NFR-4 | Negative / security | draft |
| TC-ADM-004-10 | Reactivate suspended -> active, fields cleared, login normal, lifecycle 'reactivated' | Functional | Critical | AC-5, FR-5/7, BR-1 | Happy path | draft |
| TC-ADM-004-11 | Restore terminating -> prior, scheduled_at cleared, jobs de-queued, 'restored' | Functional | Critical | AC-6, FR-6/7, BR-1 | Happy path | draft |
| TC-ADM-004-12 | Full transition matrix — invalid transitions 409/400, no state change | Functional | Critical | AC-1/3/5/6, BR-1/3 | Negative / boundary | draft |
| TC-ADM-004-13 | BR-3: terminated tenant cannot be restored | Functional | High | BR-3 | Negative / boundary | draft |
| TC-ADM-004-14 | BR-2: system tenant cannot be suspended/terminated | Security | Critical | BR-2 | Negative / security | draft |
| TC-ADM-004-15 | BR-7: only SystemAdmin transitions; SystemSupport view-only; tenant 403; unauth 401 | Security | Critical | BR-7, FR-1/2/5/6 | Negative / security | draft |
| TC-ADM-004-16 | Grace boundaries: 7/90 accepted; <7/>90 rejected; default 30 | Functional | High | AC-3, FR-2, BR-4 | Negative / boundary | draft |
| TC-ADM-004-17 | Typed-subdomain confirmation blocks mismatch; paste prevented (FE-verified) | E2E | High | AC-3, FR-4, NFR-5 | Negative / security | draft |
| TC-ADM-004-18 | [DEFERRED] lifecycle/reminder email DELIVERY | Integration | High | AC-1/3/5, FR-1/2 | Deferred placeholder | blocked |
| TC-ADM-004-19 | [DEFERRED] file-storage (blob) deletion | Integration | Medium | AC-4, FR-3, §10 | Deferred placeholder | blocked |
| TC-ADM-004-20 | [DEFERRED] maintenance-window deletion scheduling | Integration | Medium | NFR-3 | Deferred placeholder | blocked |
| TC-ADM-004-21 | [DEFERRED] 50k-record deletion within 10 min | Performance | Medium | NFR-2 | Deferred placeholder | blocked |
| TC-ADM-ISO-008 | Deleting Tenant A leaves Tenant B unaffected | Security | Critical | AC-4, FR-3, Test Hints | Multi-tenant isolation | draft |
| TC-ADM-ISO-009 | Lifecycle endpoints require system context; cross-tenant injection -> 404 | Security | Critical | BR-7, FR-1/2/5/6/7 | Multi-tenant isolation | draft |

## Acceptance-Criteria Coverage (US-ADM-004)

| AC | Covered By | Notes |
|----|-----------|-------|
| AC-1 (suspend: status, suspended_at/reason, sessions revoked, 451, lifecycle 'suspended', notification) | TC-ADM-004-01, -02, -03, -05 (+ TC-ADM-004-18 DEFERRED delivery) | Dispatch real; email delivery deferred |
| AC-2 (suspended login: only Tenant Admin; read-only notice; others blocked) | TC-ADM-004-04, -05 | Direct |
| AC-3 (terminate: Terminating, scheduled_at, read-only+export, reminders, lifecycle 'termination_initiated') | TC-ADM-004-06, -07, -08, -16, -17 (+ -18 DEFERRED reminder delivery) | Scheduling real; delivery deferred |
| AC-4 (deletion: hard-delete, tenant retained Terminated + PII redacted, audit retained, lifecycle 'terminated') | TC-ADM-004-09, TC-ADM-ISO-008 (+ -19/-20/-21 DEFERRED blob/window/perf) | DB deletion real; blob/window/perf deferred |
| AC-5 (reactivate: active, fields cleared, jobs resumed, login normal, lifecycle 'reactivated') | TC-ADM-004-10 | Direct |
| AC-6 (restore: prior state, scheduled_at cleared, jobs de-queued, lifecycle 'restored') | TC-ADM-004-11 | Direct |

## BR / FR / NFR Coverage (US-ADM-004)

| Requirement | Covered By | Notes |
|-------------|-----------|-------|
| BR-1 (allowed-state transition matrix) | TC-ADM-004-01/-02/-06/-07/-10/-11 (valid) + TC-ADM-004-12 (invalid) | Full matrix |
| BR-2 (system tenant immune) | TC-ADM-004-14 | Direct |
| BR-3 (terminated not restorable) | TC-ADM-004-13, -12 | Direct |
| BR-4 (grace 7-90, default 30) | TC-ADM-004-16, -06 | Direct |
| BR-5 (suspension revokes refresh tokens; data/config preserved) | TC-ADM-004-01, -02 | Direct |
| BR-6 (Terminating read-only; writes 403) | TC-ADM-004-08, -06 | Direct |
| BR-7 (only SystemAdmin; SystemSupport view-only) | TC-ADM-004-15, TC-ADM-ISO-009 | Direct |
| FR-1 (suspend endpoint: token revoke + notifications) | TC-ADM-004-01, -03 | Direct (delivery deferred -18) |
| FR-2 (terminate endpoint: schedule deletion + reminder jobs) | TC-ADM-004-06, -16 | Direct (delivery deferred -18) |
| FR-3 (deletion job: dependency order, transactional, tenant retained + PII redact) | TC-ADM-004-09, TC-ADM-ISO-008 | DB real; blob deferred -19 |
| FR-4 (typed-subdomain confirmation) | TC-ADM-004-17 | FE-verified |
| FR-5 (reactivation reverses suspension; resume jobs) | TC-ADM-004-10 | Direct |
| FR-6 (restoration reverses termination; remove scheduled jobs) | TC-ADM-004-11 | Direct |
| FR-7 (every transition writes lifecycle_event + system_audit_log) | TC-ADM-004-01/-06/-09/-10/-11 | Direct |
| NFR-1 (suspension effective < 30s) | TC-ADM-004-01 (functional) | Effect verified; sub-30s timing needs perf env (flagged) |
| NFR-2 (deletion < 10 min @ 50k) | TC-ADM-004-21 (DEFERRED) | Needs perf env |
| NFR-3 (deletion in maintenance window) | TC-ADM-004-20 (DEFERRED) | No window config today |
| NFR-4 (atomic transitions, no partial state) | TC-ADM-004-09 (step 7) | Direct |
| NFR-5 (no-paste typed confirmation) | TC-ADM-004-17 | FE-verified |

## Summary (US-ADM-004)

| Metric | Value |
|--------|-------|
| User stories covered | 1 (US-ADM-004) |
| Total test cases | 23 (17 functional/security/e2e/a11y + 4 DEFERRED + 2 isolation) |
| AC coverage | 6/6 (AC-1/3/4 have DEFERRED sub-parts: email delivery, blob deletion, window/perf) |
| BR coverage | 7/7 (BR-1..BR-7) |
| FR coverage | 7/7 (FR-1..FR-7) |
| Run-green now | 19 (TC-ADM-004-01..17 + TC-ADM-ISO-008, -009) |
| Deferred (status: blocked) | 4 (TC-ADM-004-18, -19, -20, -21) |
| Functional ID range | TC-ADM-004-01 .. TC-ADM-004-21 |
| ISO ID range | TC-ADM-ISO-008 .. TC-ADM-ISO-009 |

---

## Module Totals

| Metric | Value |
|--------|-------|
| User stories covered | 4 (US-ADM-001, US-ADM-002, US-ADM-003, US-ADM-004) |
| Total test cases | 76 |
| ISO ID range (module) | TC-ADM-ISO-001 .. TC-ADM-ISO-009 |
