---
module: Admin Console
total_user_stories: 10
total_test_cases: 217
created: 2026-06-16
updated: 2026-06-17
status: complete
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

## US-ADM-005 — Tenant Admin Manages Users and Role Assignments

> Fifth Admin Console story (first **Tenant Admin** persona, tenant-scoped — isolation runs in the resolved-tenant EF query-filter context, NOT system context). 25 test cases: 18 run-green functional/security/integration (TC-ADM-005-01..18) + 3 DEFERRED (TC-ADM-005-19/-20/-21) + 4 dedicated multi-tenant isolation (TC-ADM-ISO-010..013, continuing the running ISO counter). All 6 ACs, all 7 BRs, and all 6 FRs traced. Implementation facts tested as built: the `users` table is global; "user management" = `user_tenant` memberships + `user_tenant_role` within the tenant; new `user_invitation` entity (Invited/Accepted/Expired/Revoked, 72h HASHED token). Invite is find-or-create-global (no duplicate user) + plan-limit checked at invite time vs `Tenant.MaxEmployees` (BR-5). Bulk CSV validates per-row (valid rows succeed while invalid rows error). Role edit replaces the set with assigned_at/by + before/after audit; BR-2 blocks removing TenantOwner; BR-4 built-in roles assignable-not-editable; BR-7 multiple roles. Deactivate -> membership Disabled + revoke THIS-tenant tokens + audit; BR-3 no self-deactivation; isolation: Tenant A deactivation leaves Tenant B intact. Force password reset -> revoke ALL tokens across tenants (global credential) + null password_changed_at + reset email + audit. End-all-sessions -> revoke current-tenant tokens only. Invitation expiry 72h; Resend issues a new token (BR-6). Cross-tenant param manipulation -> **404 not 403** (existence non-disclosure, AC-6).
>
> PLATFORM ACCURACY / DEFERRED: NFR-5 names PostgreSQL RLS as a third isolation layer; the platform implements only the app (ITenantContext) + EF (global query filter / TenantInterceptor) layers — RLS is a deferred extension point (TC-ADM-005-21, status: blocked; same family as US-ADM-001..004 / Payroll / Leave). Real invitation/reset email DELIVERY is log-only until US-NTF (TC-ADM-005-19) — the dispatch seam IS asserted run-green in TC-ADM-005-04/-14. NFR-1 5,000-user/1.5s perf needs a perf-representative env (TC-ADM-005-20). Custom roles / auto-assign / SCIM are §10 Phase-2, out of scope (touched negatively in TC-ADM-005-10). STORY MISMATCH to flag to the caller: AC-6/NFR-5 specify Postgres RLS as active — reword to EF query filters as the active layer with RLS as future hardening.

## Coverage by Test Case (US-ADM-005)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category |
|-----------|-------|------|----------|--------------------|----------|
| TC-ADM-005-01 | User list paginated, tenant-scoped, all columns | E2E | Critical | AC-1, FR-1, BR-1 | Happy path / isolation |
| TC-ADM-005-02 | Search (name/email) + filter (status, role) | Functional | High | AC-1, FR-1 | Boundary |
| TC-ADM-005-03 | Pagination boundaries (default 20, max 100) | Functional | Medium | AC-1, FR-1 | Boundary / negative |
| TC-ADM-005-04 | Invite NEW global user: user+invited membership+72h token+dispatch | E2E | Critical | AC-2, FR-2, BR-1/5 | Happy path |
| TC-ADM-005-05 | Invite EXISTING global user: no duplicate, new membership only | Functional | High | AC-2, FR-2, BR-1 | Boundary / isolation |
| TC-ADM-005-06 | Plan limit enforced at invite time (5 ok, 6th rejected) | Functional | Critical | AC-2, BR-5 | Negative / boundary |
| TC-ADM-005-07 | Bulk CSV 5 valid + 2 invalid -> per-row | Functional | High | AC-2, FR-2/3 | Negative / boundary |
| TC-ADM-005-08 | Role edit Manager+HR Officer; assigned_at/by; before/after audit | Functional | Critical | AC-3, FR-4, BR-7 | Happy path |
| TC-ADM-005-09 | BR-2: cannot remove TenantOwner | Security | Critical | AC-3, BR-2 | Negative |
| TC-ADM-005-10 | BR-4: built-in roles assignable not editable/deletable | Functional | High | AC-3, BR-4, §10 | Negative |
| TC-ADM-005-11 | Deactivate: disabled + this-tenant tokens revoked + audit | E2E | Critical | AC-4, FR-1, NFR-2 | Happy path |
| TC-ADM-005-12 | Deactivation isolation: A disable leaves B login intact | Security | Critical | AC-4, BR-1 | Multi-tenant isolation |
| TC-ADM-005-13 | BR-3: cannot self-deactivate | Security | Critical | AC-4, BR-3 | Negative |
| TC-ADM-005-14 | Force password reset: ALL-tenant token revoke + null PwdChangedAt | Security | Critical | AC-5, NFR-2 | Happy path / isolation |
| TC-ADM-005-15 | End All Sessions: current-tenant tokens only | Security | High | AC-4, FR-5 | Boundary / isolation |
| TC-ADM-005-16 | Invitation expiry 72h + resend new token (+ revoke) | Functional | High | AC-2, BR-6 | Negative / boundary |
| TC-ADM-005-17 | Audit completeness sweep (actor/action/before-after/IP/ts) | Integration | High | AC-2/3/4/5, FR-4/5, NFR-2 | Negative / security |
| TC-ADM-005-18 | JWT valid until expiry; next refresh new roles; detail view FR-6 | Functional | High | AC-3, FR-4/6 | Happy path |
| TC-ADM-005-19 | [DEFERRED] real invitation/reset email DELIVERY | Integration | High | AC-2/5, NFR-3 (DEFERRED: US-NTF) | Deferred |
| TC-ADM-005-20 | [DEFERRED] list <= 1.5s @ 5,000 users | Performance | Medium | AC-1, FR-1, NFR-1 (DEFERRED: perf env) | Deferred |
| TC-ADM-005-21 | [DEFERRED] Postgres RLS isolation layer | Security | Medium | AC-6, NFR-5 (DEFERRED: RLS) | Deferred |
| TC-ADM-ISO-010 | User list tenant-scoped (EF query-filter READ block) | Security | Critical | AC-1/6, FR-1, BR-1 | Multi-tenant isolation |
| TC-ADM-ISO-011 | Cross-tenant param manipulation -> 404 not 403 | Security | Critical | AC-6, BR-1 | Multi-tenant isolation |
| TC-ADM-ISO-012 | Mutating endpoints require tenant context + TenantAdmin authz; writes stamped | Security | Critical | AC-6, FR-1/2/4, BR-1 | Multi-tenant isolation |
| TC-ADM-ISO-013 | Token-revocation scoping: deactivate/end-sessions tenant-only vs force-reset global | Security | Critical | AC-4/5, FR-5, BR-1 | Multi-tenant isolation |

## Acceptance-Criteria Coverage (US-ADM-005)

| AC | Covered By |
|----|-----------|
| AC-1 (paginated/searchable/filterable tenant-scoped list, all columns) | TC-ADM-005-01, -02, -03, TC-ADM-ISO-010 (+ -20 DEFERRED perf) |
| AC-2 (invite single/bulk: 72h token, find-or-create, plan limit, pending tab) | TC-ADM-005-04, -05, -06, -07, -16 (+ -19 DEFERRED delivery) |
| AC-3 (edit roles: user_tenant_role updated, before/after audit, JWT valid until refresh) | TC-ADM-005-08, -09, -10, -18 |
| AC-4 (deactivate: disabled + this-tenant token revoke + isolation + audit) | TC-ADM-005-11, -12, -13, -15, TC-ADM-ISO-013 |
| AC-5 (force password reset: ALL-tenant token revoke + null PwdChangedAt + email + audit) | TC-ADM-005-14, TC-ADM-ISO-013 |
| AC-6 (cross-tenant param manipulation rejected -> 404 not 403) | TC-ADM-ISO-010, -011, -012 (+ -21 DEFERRED RLS layer) |

## BR / FR / NFR Coverage (US-ADM-005)

| Requirement | Covered By | Notes |
|-------------|-----------|-------|
| BR-1 (own-tenant only) | TC-ADM-005-05/-12, TC-ADM-ISO-010/-011/-012/-013 | Direct |
| BR-2 (cannot remove TenantOwner) | TC-ADM-005-09 | Direct |
| BR-3 (no self-deactivation) | TC-ADM-005-13 | Direct |
| BR-4 (built-in roles assignable not editable) | TC-ADM-005-10 | Direct |
| BR-5 (plan limit at invite time) | TC-ADM-005-06 | Direct |
| BR-6 (72h expiry; resend new token) | TC-ADM-005-16 | Direct |
| BR-7 (multiple roles per user) | TC-ADM-005-08, -07 | Direct |
| FR-1 (list endpoint: join + tenant filter + pagination/search/filter) | TC-ADM-005-01/-02/-03, TC-ADM-ISO-010 | Direct |
| FR-2 (invite: existence/membership/limit checks + invitation + dispatch) | TC-ADM-005-04/-05/-06/-07 | Direct (delivery deferred -19) |
| FR-3 (bulk CSV per-row validation) | TC-ADM-005-07 | Direct |
| FR-4 (role assignment: assigned_at/by) | TC-ADM-005-08, -18 | Direct |
| FR-5 (end all sessions: current tenant only) | TC-ADM-005-15, TC-ADM-ISO-013 | Direct |
| FR-6 (user detail: profile/roles/employee/audit/sessions/invitations) | TC-ADM-005-18 | Direct |
| NFR-1 (list <= 1.5s @ 5,000 users) | TC-ADM-005-20 (DEFERRED) | Needs perf env; correctness in -01..03 |
| NFR-2 (all actions audited: actor/action/before-after/IP/ts) | TC-ADM-005-17 (sweep) + -04/-08/-11/-14/-15 | Direct |
| NFR-3 (email dispatch <= 30s) | TC-ADM-005-04/-14 (dispatch) + -19 (DEFERRED delivery) | Dispatch real; delivery deferred |
| NFR-4 (mobile responsive 360-4K, Notion aesthetic) | (FE-verified during FE story; not separately scripted here) | FE-verified |
| NFR-5 (three-layer isolation incl. Postgres RLS) | TC-ADM-ISO-010/-012 (app+EF) + -21 (DEFERRED RLS) | App+EF real; RLS deferred |

## Summary (US-ADM-005)

| Metric | Value |
|--------|-------|
| User stories covered | 1 (US-ADM-005) |
| Total test cases | 25 (18 functional/security/integration/e2e + 3 DEFERRED + 4 isolation) |
| AC coverage | 6/6 (AC-1/2/5/6 have DEFERRED sub-parts: perf, email delivery, RLS) |
| BR coverage | 7/7 (BR-1..BR-7) |
| FR coverage | 6/6 (FR-1..FR-6) |
| Run-green now | 22 (TC-ADM-005-01..18 + TC-ADM-ISO-010..013) |
| Deferred (status: blocked) | 3 (TC-ADM-005-19, -20, -21) |
| Functional ID range | TC-ADM-005-01 .. TC-ADM-005-21 |
| ISO ID range | TC-ADM-ISO-010 .. TC-ADM-ISO-013 |

---

## US-ADM-006 — Tenant Admin Configures Company Settings (Logo, Colors, Policies)

> Sixth Admin Console story (second **Tenant Admin** persona, tenant-scoped). 24 test cases: 17 run-green functional/security/integration/e2e/a11y (TC-ADM-006-01..17) + 4 DEFERRED (TC-ADM-006-18..21) + 3 dedicated multi-tenant isolation (TC-ADM-ISO-014..016, continuing the running ISO counter). All 5 ACs (AC-1..AC-5), all 6 BRs (BR-1..BR-6), and all 7 FRs (FR-1..FR-7) traced.
>
> IMPLEMENTATION FACTS (tested as built): settings are realized as **TYPED COLUMNS on the Tenant entity** (org profile, localization, branding URLs, password policy, session policy) — NOT a separate EAV `tenant_setting` table (codebase convention). A migration adds the missing org/localization/branding columns; password/session-policy columns already existed. Org profile / localization / password policy / session policy each have a GET + PUT, all changes audited before/after (NFR-4). Operations target the CURRENT tenant via `ITenantContext` only — there is NO `tenant_id` parameter to manipulate, so settings are inherently tenant-isolated (TC-ADM-ISO-014; cross-tenant access → 404/empty). Branding upload (AC-2/NFR-2) does server-side MAGIC-BYTE + size validation (logo PNG/SVG ≤2MB, favicon ICO/PNG ≤500KB) — a `.png`-extension file with wrong magic bytes is rejected (TC-ADM-006-04); files are stored under the tenant-scoped path `{tenantId}/branding/` (BR-6, TC-ADM-006-03/-06, TC-ADM-ISO-015). Primary color is a validated hex; the FE derives complementary shades into CSS custom properties (FR-3, TC-ADM-006-07). Localization sets tenant defaults for users without a personal preference (BR-5), validated against a supported-language list (FR-4); chosen date/number/currency formats drive UI rendering (TC-ADM-006-10). Password policy is persisted (TC-ADM-006-11) and ENFORCED at the next password change/reset — min length 12 → a 10-char password is rejected (AC-4/FR-5, TC-ADM-006-12). Session policy persists idle/absolute timeout + max concurrent sessions (FR-6, TC-ADM-006-13; enforcement seam is auth middleware). BR-1 limits all writes to TenantAdmin/TenantOwner (TC-ADM-006-14); BR-3 plan-gating disables enterprise-only options in the UI AND rejects them at the API (TC-ADM-006-15).
>
> DEFERRED (status: blocked; honest traceability, never fabricated): real blob/object-storage persistence + signed URLs (TC-ADM-006-18) — no S3/Azure wired; validation + tenant path prefix are real, only cloud persistence is deferred. Redis config-cache invalidation (`t:{tenantId}:config`) + SignalR <60s propagation (FR-7/NFR-3, TC-ADM-006-19) — Redis/SignalR not wired; the invalidation call no-ops gracefully and propagation is next-page-load; settings are always read tenant-filtered. NFR-1 1.5s page load incl. logo preview (TC-ADM-006-20) — needs a perf-representative env. Custom CSS / white-label / login-page customization beyond logo+color (TC-ADM-006-21) — §10 Phase 2. PostgreSQL RLS DB-layer isolation named in AC-5 (TC-ADM-ISO-016) — platform implements app (`ITenantContext`) + EF (query filter/`TenantInterceptor`) layers only; RLS is a deferred extension point (same family as US-ADM-001..005 / Payroll / Leave).
>
> STORY MISMATCH worth flagging to the caller: (1) AC-1/AC-2/AC-3 and §7 describe settings as `tenant_setting` rows; the platform implements them as TYPED Tenant columns — the behavior (per-tenant upsert + audit) is equivalent, but the story should be reworded to match the typed-column model. (2) AC-5 names PostgreSQL RLS as the DB-layer isolation; the active mechanism is EF query filters + `ITenantContext` (RLS deferred) — reword AC-5 with RLS as future hardening. (3) Preconditions assert "File storage service is operational" and §9 lists Redis; neither blob storage nor Redis is wired today (validation/path-prefix + tenant-filtered reads are real; cloud persistence + cache push deferred).

## Coverage by Test Case (US-ADM-006)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category | Status |
|-----------|-------|------|----------|--------------------|----------|--------|
| TC-ADM-006-01 | Org profile GET+PUT persisted, reflected, before/after audited | E2E | Critical | AC-1, FR-1, BR-1/4, NFR-4 | Happy path / isolation | draft |
| TC-ADM-006-02 | Org profile validation — invalid/boundary rejected, no partial write | Functional | High | AC-1, FR-1, BR-4 | Negative / boundary | draft |
| TC-ADM-006-03 | Branding valid PNG logo accepted, tenant-scoped path, URL persisted | Integration | Critical | AC-2, FR-2, BR-6, NFR-2/4 | Happy path / isolation | draft |
| TC-ADM-006-04 | Branding wrong-magic-bytes (spoofed .png) rejected | Security | Critical | AC-2, FR-2, NFR-2 | Negative / security | draft |
| TC-ADM-006-05 | Branding oversize + wrong-type rejected at size/type boundaries | Functional | High | AC-2, FR-2, NFR-2 | Negative / boundary | draft |
| TC-ADM-006-06 | Favicon (ICO + PNG) accepted, tenant-scoped, URL persisted | Functional | Medium | AC-2, FR-2, BR-6, NFR-2/4 | Happy path / boundary | draft |
| TC-ADM-006-07 | Primary color hex validated; FE derives shades into CSS vars | Functional | High | AC-2, FR-3, NFR-2/4 | Happy path / negative | draft |
| TC-ADM-006-08 | Localization defaults persist + apply to users w/o preference | Functional | Critical | AC-3, FR-1/4, BR-2/5, NFR-4 | Happy path | draft |
| TC-ADM-006-09 | Localization unsupported language/format/tz/currency rejected | Functional | High | AC-3, FR-4, BR-5 | Negative / boundary | draft |
| TC-ADM-006-10 | Localization rendering — date/number/currency applied across UI | E2E | High | AC-3, FR-1/4 | Happy path | draft |
| TC-ADM-006-11 | Password policy GET+PUT persists structured policy; audited | Functional | Critical | AC-4, FR-5, NFR-4 | Happy path / negative | draft |
| TC-ADM-006-12 | Password policy ENFORCEMENT — 10-char rejected at next change | Security | Critical | AC-4, FR-5, Test Hints | Negative / boundary | draft |
| TC-ADM-006-13 | Session policy GET+PUT persists timeouts + max sessions; audited | Functional | High | FR-6, NFR-4 | Happy path / negative | draft |
| TC-ADM-006-14 | Authz — only TenantAdmin/TenantOwner write; others 403, unauth 401 | Security | Critical | AC-1..4, BR-1 | Negative / security | draft |
| TC-ADM-006-15 | Plan-gating — enterprise-only disabled in UI + rejected by API | Functional | High | BR-3, §10 | Negative / security | draft |
| TC-ADM-006-16 | Audit completeness sweep — every section before/after audited | Integration | High | AC-1/2/3/4, FR-1/5/6, NFR-4 | Negative / security | draft |
| TC-ADM-006-17 | Settings UI WCAG 2.1 AA + dirty-track Save + responsive 360-4K | Accessibility | Medium | NFR-5, §8 | Accessibility | draft |
| TC-ADM-006-18 | [DEFERRED] real blob/object-storage persistence (S3/Azure) | Integration | Medium | AC-2, FR-2, §10 | Deferred placeholder | blocked |
| TC-ADM-006-19 | [DEFERRED] Redis config-cache invalidation + SignalR <60s propagation | Integration | Medium | FR-7, NFR-3 | Deferred placeholder | blocked |
| TC-ADM-006-20 | [DEFERRED] settings page load < 1.5s incl. logo preview | Performance | Low | NFR-1 | Deferred placeholder | blocked |
| TC-ADM-006-21 | [DEFERRED] custom CSS / white-label / login-page customization | Functional | Low | §10, BR-3 | Deferred placeholder | blocked |
| TC-ADM-ISO-014 | Settings tenant-scoped via ITenantContext; cross-tenant → 404/empty | Security | Critical | AC-5, FR-1, BR-1/6, Test Hints | Multi-tenant isolation | draft |
| TC-ADM-ISO-015 | Branding file storage tenant-scoped; B cannot reach A's path | Security | Critical | AC-2/5, FR-2, BR-6, Test Hints | Multi-tenant isolation | draft |
| TC-ADM-ISO-016 | [DEFERRED] PostgreSQL RLS DB-layer isolation for settings | Security | Medium | AC-5 (DEFERRED: RLS) | Multi-tenant isolation | blocked |

## Acceptance-Criteria Coverage (US-ADM-006)

| AC | Covered By |
|----|-----------|
| AC-1 (org profile update → typed columns, reflected, before/after audit, no cross-tenant) | TC-ADM-006-01, -02, -16, TC-ADM-ISO-014 |
| AC-2 (branding upload tenant-scoped path + URL saved; primary color; magic-byte+size validation) | TC-ADM-006-03, -04, -05, -06, -07, TC-ADM-ISO-015 (+ -18 DEFERRED blob persistence) |
| AC-3 (localization defaults for users w/o preference; UI renders formats; audited) | TC-ADM-006-08, -09, -10, -16 |
| AC-4 (password policy saved + enforced at next change; existing not invalidated; audited) | TC-ADM-006-11, -12, -16 |
| AC-5 (cross-tenant access rejected; ITenantContext-only; RLS at DB layer) | TC-ADM-ISO-014 (ITenantContext/404), TC-ADM-ISO-015 (file path) (+ TC-ADM-ISO-016 DEFERRED RLS) |

## BR / FR / NFR Coverage (US-ADM-006)

| Requirement | Covered By | Notes |
|-------------|-----------|-------|
| BR-1 (only TenantAdmin/TenantOwner) | TC-ADM-006-14, -01/-02 | Direct |
| BR-2 (config hierarchy: user > tenant) | TC-ADM-006-08 | Direct (tenant default vs user preference) |
| BR-3 (plan-constrained settings gated) | TC-ADM-006-15 | UI disable + API reject |
| BR-4 (fiscal year start) | TC-ADM-006-01, -02 | Persisted + range-validated |
| BR-5 (default language for users w/o preference) | TC-ADM-006-08, -09 | Direct |
| BR-6 (tenant-scoped branding files) | TC-ADM-006-03, -06, TC-ADM-ISO-015 | Direct |
| FR-1 (org/settings keyed by ITenantContext.TenantId) | TC-ADM-006-01, -08, TC-ADM-ISO-014 | Typed columns; tenant-keyed |
| FR-2 (branding upload type+size; tenant path; URLs) | TC-ADM-006-03/-04/-05/-06, TC-ADM-ISO-015 (+ -18 DEFERRED signed URLs) | Validation+path real; cloud deferred |
| FR-3 (primary color hex → derived shades / CSS vars) | TC-ADM-006-07 | Direct |
| FR-4 (localization validated vs supported-language list) | TC-ADM-006-08, -09 | Direct |
| FR-5 (password policy stored + enforced on every change) | TC-ADM-006-11, -12 | Direct |
| FR-6 (session policy stored + enforced by auth middleware) | TC-ADM-006-13 | Persist+read real; enforcement seam noted |
| FR-7 (cache invalidation for `t:{tenantId}:config`) | TC-ADM-006-19 (DEFERRED) | Redis not wired; no-ops gracefully |
| NFR-1 (settings load < 1.5s incl. logo preview) | TC-ADM-006-20 (DEFERRED) | Needs perf env |
| NFR-2 (server-side magic-byte + size validation) | TC-ADM-006-04, -05, -07 | Direct |
| NFR-3 (propagate to active sessions < 60s) | TC-ADM-006-19 (DEFERRED) | SignalR not wired; next-page-load today |
| NFR-4 (all settings changes before/after audited) | TC-ADM-006-16, -01/-03/-07/-08/-11/-13 | Direct |
| NFR-5 (responsive 360-4K, a11y) | TC-ADM-006-17 | Direct |

## Summary (US-ADM-006)

| Metric | Value |
|--------|-------|
| User stories covered | 1 (US-ADM-006) |
| Total test cases | 24 (17 functional/security/integration/e2e/a11y + 4 DEFERRED + 3 isolation) |
| AC coverage | 5/5 (AC-2/AC-5 have DEFERRED sub-parts: blob persistence, RLS) |
| BR coverage | 6/6 (BR-1..BR-6) |
| FR coverage | 7/7 (FR-1..FR-7; FR-7 DEFERRED) |
| Run-green now | 20 (TC-ADM-006-01..17 + TC-ADM-ISO-014, -015) |
| Deferred (status: blocked) | 4 (TC-ADM-006-18..21) + 1 (TC-ADM-ISO-016) |
| Functional ID range | TC-ADM-006-01 .. TC-ADM-006-21 |
| ISO ID range | TC-ADM-ISO-014 .. TC-ADM-ISO-016 |

---

## US-ADM-007 — Tenant Admin Manages Approval Workflows

> Seventh Admin Console story (third **Tenant Admin** persona, tenant-scoped). 22 test cases: 15 run-green functional/security/e2e (TC-ADM-007-01..15) + 3 DEFERRED (TC-ADM-007-16/-17/-18) + 4 dedicated multi-tenant isolation (TC-ADM-ISO-017..020, continuing the running ISO counter; ISO-020 DEFERRED). All 5 ACs (AC-1..AC-5), all 7 BRs (BR-1..BR-7), and all 7 FRs (FR-1..FR-7) traced.
>
> IMPLEMENTATION FACTS (tested as built — DEFINITION-MANAGEMENT layer + a PURE evaluator): this story builds the workflow DEFINITION layer — new tenant-scoped `WorkflowDefinition` + `WorkflowStep` entities (EF query-filter read isolation + `TenantInterceptor` write stamping) — plus CRUD, versioning, plan-limit, step/condition/escalation/delegation CONFIG, and audit. List is grouped by entity type, tenant-scoped, with a `Default` flag on seeded workflows (AC-1, TC-ADM-007-01). Create requires >=1 step + valid approver refs + positive SLA (FR-5, TC-ADM-007-06/-07/-08); a 3-step Leave workflow with conditions/SLAs/escalation persists at version=1 and is audited (AC-2, TC-ADM-007-02). BR-2: one active workflow per entity type — creating a new active one auto-archives the previous (TC-ADM-007-03). Edit creates a NEW VERSION (v2) with the prior version retained (AC-3, TC-ADM-007-04). Plan limit via `Tenant.MaxWorkflows` returns the EXACT message "You have reached the maximum number of workflows ({limit}) for your plan. Please upgrade or archive an existing workflow." (AC-4/FR-4, TC-ADM-007-05). Archive/restore works and archived workflows do NOT count toward the plan limit (FR-6, TC-ADM-007-09). Delete is guarded by BR-6 — allowed only with no in-flight instances (none exist yet; guard verified, TC-ADM-007-10). BR-1 limits all writes to TenantAdmin/TenantOwner (TC-ADM-007-11). A PURE, unit-tested `WorkflowEvaluator` is run-green: BR-5 conditional skip — a 3-day request skips a `>5` step, a 10-day includes it (TC-ADM-007-12); BR-3 parallel — a parallel step needs ALL approvers (TC-ADM-007-13); each `{field,operator,value}` operator (>, <, >=, <=, ==, !=) with strict/inclusive boundaries (TC-ADM-007-14). AC-5 delegation CONFIG (enabled + valid backup approver) is stored + audited (run-green, TC-ADM-007-15). Cross-tenant list/read returns empty/404 (BR-7, TC-ADM-ISO-017); cross-tenant ID injection on mutating endpoints -> 404 not 403 (TC-ADM-ISO-018); writes require tenant context + TenantAdmin authz and are tenant-stamped (TC-ADM-ISO-019).
>
> DEFERRED (status: blocked; honest traceability, never fabricated): the RUNTIME ENGINE is NOT built — Leave/Attendance/etc. do not yet route live requests through these definitions. AC-5 LIVE delegation routing of a submitted request (config stored+testable; runtime routing deferred — TC-ADM-007-16). BR-4 SLA-breach auto-escalation FIRING at runtime (escalation config stored at definition time in TC-ADM-007-02; the live SLA-timer firing + notification is deferred — TC-ADM-007-17). NFR-2 Redis workflow-definition cache (`t:{tenantId}:workflows:{entityType}`) + NFR-1 editor <2s + NFR-3 evaluation <100ms (TC-ADM-007-18) — Redis not wired; perf needs a representative env. PostgreSQL RLS DB-layer isolation (TC-ADM-ISO-020) — platform uses app (`ITenantContext`) + EF (query filter/`TenantInterceptor`) layers only; RLS is a deferred extension point (same family as US-ADM-001..006/Payroll/Leave). IMPORTANT distinction: the condition/parallel EVALUATION is a pure, fully-tested function (run-green); only its LIVE INVOCATION from submitted-request flows is deferred. The Test Hints requiring live request processing ("conditional step skipped for a submitted request", "parallel approval gating a submitted request", "delegation routing a submitted request", "let the SLA timer expire") have their DEFINITION/evaluator side run-green and their LIVE-request side deferred to the runtime engine.
>
> STORY MISMATCH worth flagging to the caller: the story (Description, AC-2/AC-3/AC-5, FR-3, BR-3/4/5/6, §7 "Workflow instance / step instance" tables, Dependencies) assumes a working runtime workflow ENGINE that routes live requests, fires SLA timers, and creates per-request instances — none of which is built. The implementable Phase-1 subset is the DEFINITION-management layer + the pure evaluator; the story should be SPLIT so the runtime engine (instance/step-instance creation, SLA-timer escalation, live delegation/conditional/parallel routing, cross-module integration with Leave/Attendance) is a follow-on. Leave (TC-LV-097) and Attendance (TC-ATT-044) already mark their multi-level-approval ACs CONDITIONAL on US-ADM-007 — those remain conditional on this follow-on runtime engine, not satisfied by the definition layer alone. AC-5 / NFR-2 also assume Redis; not wired (deferred).

## Coverage by Test Case (US-ADM-007)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category | Status |
|-----------|-------|------|----------|--------------------|----------|--------|
| TC-ADM-007-01 | Workflow list grouped by entity type, tenant-scoped, Default flag | E2E | Critical | AC-1, FR-1/7, BR-7 | Happy path / isolation | draft |
| TC-ADM-007-02 | Create 3-step Leave workflow w/ conditions/SLAs/escalation; v1; audited | E2E | Critical | AC-2, FR-1/2, BR-1, NFR-4 | Happy path | draft |
| TC-ADM-007-03 | BR-2 auto-archive previous active workflow for same entity type | Functional | Critical | AC-2, FR-1, BR-2, NFR-4 | Boundary | draft |
| TC-ADM-007-04 | Edit creates NEW VERSION (v2); prior version retained; before/after audited | Functional | Critical | AC-3, FR-3, NFR-4 | Boundary | draft |
| TC-ADM-007-05 | Plan limit (MaxWorkflows) blocks create — exact upgrade message | Functional | Critical | AC-4, FR-4, BR-2 | Negative / boundary | draft |
| TC-ADM-007-06 | FR-5 — zero-step workflow rejected | Functional | High | AC-2, FR-5 | Negative / boundary | draft |
| TC-ADM-007-07 | FR-5 — invalid/foreign approver reference rejected | Functional | High | AC-2, FR-5/7, BR-7 | Negative / isolation | draft |
| TC-ADM-007-08 | FR-5 — non-positive SLA rejected; SLA=1 accepted | Functional | High | AC-2, FR-5/2 | Negative / boundary | draft |
| TC-ADM-007-09 | Archive/restore; archived don't count toward plan limit | Functional | High | AC-1/4, FR-6/4, NFR-4 | Boundary | draft |
| TC-ADM-007-10 | BR-6 delete guard — delete only with no in-flight instances | Functional | High | FR-6, BR-6, NFR-4 | Boundary / negative | draft |
| TC-ADM-007-11 | BR-1 authz — only TenantAdmin/TenantOwner write; others 403, unauth 401 | Security | Critical | AC-2/3/4, BR-1, FR-7 | Negative / security | draft |
| TC-ADM-007-12 | Evaluator — BR-5 conditional skip/include (3-day skips >5; 10-day includes) | Functional | Critical | AC-2, FR-2, BR-5 | Boundary | draft |
| TC-ADM-007-13 | Evaluator — BR-3 parallel step requires ALL approvers | Functional | Critical | FR-2, BR-3 | Boundary / negative | draft |
| TC-ADM-007-14 | Evaluator — each operator (>, <, >=, <=, ==, !=) strict/inclusive | Functional | High | FR-2, BR-5, §10.2 | Boundary / negative | draft |
| TC-ADM-007-15 | AC-5 delegation CONFIG stored + audited (live routing deferred) | Functional | High | AC-5, FR-2/5, NFR-4 | Happy path / negative | draft |
| TC-ADM-007-16 | [DEFERRED] AC-5 LIVE delegation routing for a submitted request | Integration | High | AC-5 (LIVE) | Deferred placeholder | blocked |
| TC-ADM-007-17 | [DEFERRED] BR-4 SLA-breach auto-escalation fires at runtime | Integration | High | BR-4 (LIVE), AC-2 | Deferred placeholder | blocked |
| TC-ADM-007-18 | [DEFERRED] Redis workflow cache + editor/eval perf | Performance | Medium | NFR-1/2/3 | Deferred placeholder | blocked |
| TC-ADM-ISO-017 | BR-7 list/read tenant-scoped; A cannot see B's workflows | Security | Critical | AC-1, FR-7, BR-7 | Multi-tenant isolation | draft |
| TC-ADM-ISO-018 | Cross-tenant ID injection on mutating endpoints -> 404 not 403 | Security | Critical | AC-3/4, FR-3/6/7, BR-7 | Multi-tenant isolation | draft |
| TC-ADM-ISO-019 | Mutating endpoints require tenant context + TenantAdmin; writes stamped | Security | Critical | AC-2/3, FR-7/1, BR-1/7 | Multi-tenant isolation | draft |
| TC-ADM-ISO-020 | [DEFERRED] PostgreSQL RLS DB-layer isolation for workflows | Security | Medium | FR-7, BR-7 (DEFERRED: RLS) | Multi-tenant isolation | blocked |

## Acceptance-Criteria Coverage (US-ADM-007)

| AC | Covered By |
|----|-----------|
| AC-1 (list grouped by request type, tenant-scoped, name/steps/status/last-modified, Default flag, editable) | TC-ADM-007-01, -09, TC-ADM-ISO-017 |
| AC-2 (create workflow: steps + conditions + SLAs + escalation; v1; all new requests use it; audited) | TC-ADM-007-02, -03, -06, -07, -08 (+ -12/-14 evaluator for the conditional clause; live "all new requests use it" deferred via -16/-17) |
| AC-3 (edit -> new version v2; in-flight keep v1; before/after audited) | TC-ADM-007-04 (definition/versioning real; in-flight pinning deferred-runtime) |
| AC-4 (plan-limit exceeded -> exact message; create blocked) | TC-ADM-007-05 (+ -09 archived-excluded interplay) |
| AC-5 (delegation: config + LIVE routing to backup when primary on leave; recorded on instance) | TC-ADM-007-15 (CONFIG, real) + TC-ADM-007-16 (DEFERRED LIVE routing) |

## BR / FR / NFR Coverage (US-ADM-007)

| Requirement | Covered By | Notes |
|-------------|-----------|-------|
| BR-1 (only TenantAdmin/TenantOwner create/edit) | TC-ADM-007-11, TC-ADM-ISO-019 | Direct |
| BR-2 (one active per entity type; new active auto-archives previous) | TC-ADM-007-03, -05 | Direct |
| BR-3 (parallel step needs all approvers) | TC-ADM-007-13 | Evaluator (pure); live gating deferred -16/-17 |
| BR-4 (SLA breach -> escalation target, else notify Tenant Admin) | TC-ADM-007-02 (config) + TC-ADM-007-17 (DEFERRED firing) | Config real; runtime firing deferred |
| BR-5 (conditional steps evaluated; unmet -> skipped) | TC-ADM-007-12, -14 | Evaluator (pure); live-request skip deferred |
| BR-6 (delete only with no in-flight instances) | TC-ADM-007-10 | Guard verified; positive in-flight trigger runtime-deferred |
| BR-7 (entirely tenant-scoped; A invisible to B) | TC-ADM-ISO-017, -018, -019 (+ -020 DEFERRED RLS) | Direct (app+EF) |
| FR-1 (definition shape incl. version; created_by/at) | TC-ADM-007-02, -01 | Direct |
| FR-2 (step fields: order/approver/sla/escalation/condition/is_parallel) | TC-ADM-007-02, -12, -13, -14, -15 | Direct |
| FR-3 (versioning; in-flight retain version) | TC-ADM-007-04 | Definition real; in-flight pinning deferred-runtime |
| FR-4 (MaxWorkflows checked before create) | TC-ADM-007-05, -09 | Direct |
| FR-5 (>=1 step, valid approver refs, positive SLA) | TC-ADM-007-06, -07, -08, -15 | Direct |
| FR-6 (archive/restore; archived excluded from plan limit; delete vs archive) | TC-ADM-007-09, -10 | Direct |
| FR-7 (tenant-scoped via ITenantContext) | TC-ADM-007-01, -07, TC-ADM-ISO-017/-018/-019 (+ -020 DEFERRED RLS) | Direct |
| NFR-1 (editor < 2s) | TC-ADM-007-18 (DEFERRED) | Needs perf env |
| NFR-2 (Redis cache + invalidation) | TC-ADM-007-18 (DEFERRED) | Redis not wired |
| NFR-3 (evaluation < 100ms) | TC-ADM-007-18 (DEFERRED) | Correctness covered -12/-13/-14; timing needs perf env |
| NFR-4 (all management actions audited) | TC-ADM-007-02, -03, -04, -09, -15 | Direct (per-action audit) |
| NFR-5 (editor responsive on tablet 768px) | (FE-verified during FE story; not separately scripted) | FE-verified |

## Summary (US-ADM-007)

| Metric | Value |
|--------|-------|
| User stories covered | 1 (US-ADM-007) |
| Total test cases | 22 (15 functional/security/e2e + 3 DEFERRED + 4 isolation, 1 of which DEFERRED) |
| AC coverage | 5/5 (AC-2/AC-3/AC-5 have DEFERRED runtime sub-parts; definition+evaluator side run-green) |
| BR coverage | 7/7 (BR-1..BR-7; BR-3/4/5/6 live-request side deferred, definition/evaluator side run-green) |
| FR coverage | 7/7 (FR-1..FR-7) |
| Run-green now | 18 (TC-ADM-007-01..15 + TC-ADM-ISO-017, -018, -019) |
| Deferred (status: blocked) | 4 (TC-ADM-007-16, -17, -18 + TC-ADM-ISO-020) |
| Functional ID range | TC-ADM-007-01 .. TC-ADM-007-18 |
| ISO ID range | TC-ADM-ISO-017 .. TC-ADM-ISO-020 |

---

## US-ADM-008 — Tenant Admin Views Audit Logs

> Eighth Admin Console story (fourth **Tenant Admin** persona, tenant-scoped — isolation runs in the resolved-tenant EF query-filter context). 25 test cases: 17 run-green functional/security/e2e/integration (TC-ADM-008-01..17) + 4 DEFERRED (TC-ADM-008-18..21) + 4 dedicated multi-tenant isolation (TC-ADM-ISO-021..024, continuing the running ISO counter; ISO-024 DEFERRED). All 5 ACs (AC-1..AC-5), all 6 BRs (BR-1..BR-6), and all 7 FRs (FR-1..FR-7) traced.
>
> IMPLEMENTATION FACTS (tested as built — a tenant-scoped READ feature over the EXISTING `audit_log` table; no new audit columns): LIST is paginated (default 50, max 200), reverse-chronological, filterable by date range / actor / action / resource-type / keyword with AND logic; DETAIL returns before/after JSON, IP, user agent, trace id (TC-ADM-008-01..09). Sensitive-field masking (FR-4) is a PURE `SensitiveFieldMasker` that redacts the values of `password_hash`/`mfa_secret`/`bank_account_number`/`national_id` (+ camelCase variants) to `***REDACTED***`, RECURSIVELY, applied to detail + export (TC-ADM-008-10); the visual DIFF (FR-3) is computed on the FRONTEND (TC-ADM-008-16, FE-verified). EXPORT (AC-4/BR-4) produces CSV/JSON respecting the current filters, masked, and writes a self-audit row Action="AuditLog.Export"; the SYNCHRONOUS small-export path is real (TC-ADM-008-11). The `Auditor` role is READ-ONLY and CANNOT export — export is gated to TenantAdmin/TenantOwner (FR-7, TC-ADM-008-12); read roles are TenantAdmin/TenantOwner/Auditor (BR-1, TC-ADM-008-13). Retention (FR-6/BR-5) is governed by `Tenant.AuditLogRetentionDays` (plan-governed, admin view-only — TC-ADM-008-15); `AuditLogPurgeJob` deletes rows older than the window, keeps recent (TC-ADM-008-14). Immutability (AC-5/NFR-3): no update/delete code path exists — append-only BY CODE CONVENTION today (TC-ADM-008-17). NFR-2 composite indexes added. Isolation: list/detail tenant-scoped (TC-ADM-ISO-021); cross-tenant audit_id injection -> 404 not 403 (TC-ADM-ISO-022); endpoints require tenant context + read role, export-audit row tenant-stamped (TC-ADM-ISO-023).
>
> DEFERRED (status: blocked; honest traceability, never fabricated): the DB-role append-only GRANT — no UPDATE/DELETE privilege on `audit_log` (AC-5/NFR-3 DB layer, TC-ADM-008-18) — deferred platform infra; today immutability is by code convention (TC-ADM-008-17). Async LARGE-export >10k via Hangfire job + emailed link (FR-5, TC-ADM-008-19) — no email/blob wired; the synchronous small-export is real (TC-ADM-008-11). BR-6 PII-READ events (TC-ADM-008-20) — no new PII-read instrumentation added; such rows surface only if a module already emits them. NFR-1 millions-of-records <2s perf (TC-ADM-008-21) — indexes added, needs a perf-representative env; correctness in -01..08. `system_audit_log` separation + PostgreSQL RLS (TC-ADM-ISO-024) — system/tenant audit separation is by context via the System Admin stories (US-ADM-002/003), and RLS is a deferred extension point (same family as US-ADM-001..007/Payroll/Leave).
>
> STORY MISMATCH worth flagging to the caller: (1) AC-5/NFR-3 specify a DB role with no UPDATE/DELETE grant on `audit_log` as the immutability mechanism; today immutability is by code convention (no edit/delete handler) — reword with the DB-grant as future hardening. (2) AC-4/FR-5 assume an email service + storage for the async >10k export; neither is wired — the synchronous small-export is the Phase-1 deliverable, async export follows US-NTF + object storage. (3) BR-6 assumes PII-read events are captured, but no read-side instrumentation was added — split PII-read capture into a follow-on (the audit list/detail/masking machinery will display them once emitted). (4) BR-3 names a `system_audit_log` table — system audit is the System Admin view (US-ADM-002/003), not part of this tenant console.

## Coverage by Test Case (US-ADM-008)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category | Status |
|-----------|-------|------|----------|--------------------|----------|--------|
| TC-ADM-008-01 | List tenant-scoped, paginated (50), reverse-chron, all columns | E2E | Critical | AC-1, FR-1/2, BR-1/3 | Happy path / isolation | draft |
| TC-ADM-008-02 | Pagination boundaries — default 50, max 200, oversize clamped | Functional | High | AC-1/2, FR-1 | Boundary / negative | draft |
| TC-ADM-008-03 | Filter by date range (inclusive boundaries) | Functional | High | AC-2, FR-1 | Boundary / negative | draft |
| TC-ADM-008-04 | Filter by actor | Functional | High | AC-2, FR-1 | Happy path / negative | draft |
| TC-ADM-008-05 | Filter by action type | Functional | High | AC-2, FR-1 | Happy path / negative | draft |
| TC-ADM-008-06 | Filter by resource type | Functional | High | AC-2, FR-1 | Happy path / negative | draft |
| TC-ADM-008-07 | Keyword search over before/after JSON | Functional | High | AC-2, FR-1 | Happy path / negative | draft |
| TC-ADM-008-08 | Combined filters AND logic; pagination + sort preserved | Functional | Critical | AC-2, FR-1 | Boundary / negative | draft |
| TC-ADM-008-09 | Detail — before/after JSON, IP, user agent, trace id | Functional | Critical | AC-3, FR-2/3 | Happy path | draft |
| TC-ADM-008-10 | Sensitive masking — bank/pwd/mfa/national-id recursive + camelCase | Security | Critical | AC-3, FR-4 | Negative / security | draft |
| TC-ADM-008-11 | Export respects filters + masked + self-audited "AuditLog.Export" | E2E | Critical | AC-4, FR-4/5, BR-4 | Happy path / security | draft |
| TC-ADM-008-12 | Auditor can READ but CANNOT export (403) | Security | Critical | AC-4, FR-7, BR-1/2 | Negative / security | draft |
| TC-ADM-008-13 | Read authz — only TenantAdmin/TenantOwner/Auditor; others 403, unauth 401 | Security | Critical | AC-1, BR-1 | Negative / security | draft |
| TC-ADM-008-14 | Retention purge deletes old, keeps recent (90-day, 91/90/today) | Integration | High | FR-6, BR-5 | Boundary / isolation | draft |
| TC-ADM-008-15 | Retention period VIEW-ONLY (plan-governed; change rejected) | Functional | High | AC-1, BR-5 | Negative / security | draft |
| TC-ADM-008-16 | Diff view — added/modified/removed highlighted (FE-verified) | E2E | High | AC-3, FR-3 | Happy path / a11y | draft |
| TC-ADM-008-17 | Immutability by code convention — no update/delete API path | Security | Critical | AC-5, NFR-3 | Negative / security | draft |
| TC-ADM-008-18 | [DEFERRED] DB-role append-only grant (no UPDATE/DELETE) | Security | High | AC-5, NFR-3 | Deferred placeholder | blocked |
| TC-ADM-008-19 | [DEFERRED] Large export >10k via Hangfire + emailed link | Integration | Medium | AC-4, FR-5 | Deferred placeholder | blocked |
| TC-ADM-008-20 | [DEFERRED] PII-read events logged + visible | Security | Medium | BR-6, FR-1/2 | Deferred placeholder | blocked |
| TC-ADM-008-21 | [DEFERRED] First-page <2s at millions of records | Performance | Medium | NFR-1, NFR-2, NFR-4 | Deferred placeholder | blocked |
| TC-ADM-ISO-021 | List/detail tenant-scoped; A cannot see B's audit rows | Security | Critical | AC-1, FR-1, BR-3 | Multi-tenant isolation | draft |
| TC-ADM-ISO-022 | Cross-tenant audit_id injection on detail/export -> 404 not 403 | Security | Critical | AC-1/3/4, BR-3 | Multi-tenant isolation | draft |
| TC-ADM-ISO-023 | Audit endpoints require tenant context + read role; export-audit stamped | Security | Critical | AC-1/4, BR-1/3 | Multi-tenant isolation | draft |
| TC-ADM-ISO-024 | [DEFERRED] system_audit_log separation + PostgreSQL RLS | Security | Medium | BR-3, NFR-3 (DEFERRED: RLS) | Multi-tenant isolation | blocked |

## Acceptance-Criteria Coverage (US-ADM-008)

| AC | Covered By |
|----|-----------|
| AC-1 (paginated, reverse-chron, tenant-scoped list; columns; no other-tenant rows) | TC-ADM-008-01, -02, -13, -15, TC-ADM-ISO-021, -023 |
| AC-2 (filters: date/actor/action/resource/keyword; AND logic; pagination+sort) | TC-ADM-008-03, -04, -05, -06, -07, -08 |
| AC-3 (detail before/after + diff + IP/UA/trace; sensitive masked) | TC-ADM-008-09, -10, -16 |
| AC-4 (export CSV/JSON, filters respected, masked, self-audited "AuditLog.Export"; large=email) | TC-ADM-008-11, -12, TC-ADM-ISO-022, -023 (+ -19 DEFERRED async large-export) |
| AC-5 (audit append-only; modify/delete rejected; DB role lacks UPDATE/DELETE) | TC-ADM-008-17 (code convention, real) + TC-ADM-008-18 (DEFERRED DB-role grant) |

## BR / FR / NFR Coverage (US-ADM-008)

| Requirement | Covered By | Notes |
|-------------|-----------|-------|
| BR-1 (only TenantAdmin/TenantOwner/Auditor view) | TC-ADM-008-13, -12, TC-ADM-ISO-023 | Direct |
| BR-2 (Auditor read-only — cannot modify/export) | TC-ADM-008-12 | Direct |
| BR-3 (records strictly tenant-scoped; system_audit_log = System Admin only) | TC-ADM-ISO-021, -022, -023 (+ -024 DEFERRED system/RLS) | App+EF real; RLS/system separation deferred |
| BR-4 (exports themselves audited "AuditLog.Export") | TC-ADM-008-11, TC-ADM-ISO-023 | Direct |
| BR-5 (retention plan-dependent; Tenant Admin views but cannot change) | TC-ADM-008-15, -14 | Direct |
| BR-6 (PII-read events logged + visible) | TC-ADM-008-20 (DEFERRED) | No new read instrumentation; surfaces if a module emits |
| FR-1 (list: tenant filter, pagination 50/200, sort, all filters) | TC-ADM-008-01..08, TC-ADM-ISO-021 | Direct |
| FR-2 (record fields incl. before/after/ip/ua/trace) | TC-ADM-008-09, -01 | Direct |
| FR-3 (visual diff between before/after — FE-computed) | TC-ADM-008-16 | FE-verified |
| FR-4 (sensitive fields masked `***REDACTED***`, recursive + camelCase) | TC-ADM-008-10, -11 | Direct |
| FR-5 (export respects filters; >10k -> Hangfire + emailed link) | TC-ADM-008-11 (sync) + -19 (DEFERRED async) | Sync real; async deferred |
| FR-6 (retention purge by AuditLogRetentionDays; purge logged) | TC-ADM-008-14 | Direct (purge audit to system context) |
| FR-7 (Auditor read-only, cannot export/other admin) | TC-ADM-008-12 | Direct |
| NFR-1 (<2s first page at scale) | TC-ADM-008-21 (DEFERRED) | Needs perf env; correctness -01..08 |
| NFR-2 (composite indexes) | TC-ADM-008-21 (DEFERRED perf) | Indexes added; perf validation deferred |
| NFR-3 (immutable; DB role no UPDATE/DELETE) | TC-ADM-008-17 (code convention) + -18 (DEFERRED DB grant) | Convention real; DB grant deferred |
| NFR-4 (audit queries don't slow writes) | TC-ADM-008-21 (DEFERRED step 4) | Needs perf env |
| NFR-5 (responsive UI 360px-4K) | TC-ADM-008-16 (FE-verified) | FE-verified |

## Summary (US-ADM-008)

| Metric | Value |
|--------|-------|
| User stories covered | 1 (US-ADM-008) |
| Total test cases | 25 (17 functional/security/e2e/integration + 4 DEFERRED + 4 isolation, 1 of which DEFERRED) |
| AC coverage | 5/5 (AC-4/AC-5 have DEFERRED sub-parts: async large-export, DB-role grant) |
| BR coverage | 6/6 (BR-1..BR-6; BR-6 PII-read deferred) |
| FR coverage | 7/7 (FR-1..FR-7; FR-5 async deferred) |
| Run-green now | 20 (TC-ADM-008-01..17 + TC-ADM-ISO-021, -022, -023) |
| Deferred (status: blocked) | 5 (TC-ADM-008-18, -19, -20, -21 + TC-ADM-ISO-024) |
| Functional ID range | TC-ADM-008-01 .. TC-ADM-008-21 |
| ISO ID range | TC-ADM-ISO-021 .. TC-ADM-ISO-024 |

---

## US-ADM-009 — System Admin Manages Subscription Plans

> Ninth Admin Console story (back to the **System Admin** persona, system context at `admin.yourhrm.com`). 21 test cases: 13 run-green functional/security/integration/e2e (TC-ADM-009-01..13) + 5 DEFERRED (TC-ADM-009-14..18) + 3 dedicated multi-tenant isolation (TC-ADM-ISO-025..027, continuing the running ISO counter; -027 DEFERRED). All 5 ACs (AC-1..AC-5), all 7 BRs (BR-1..BR-7), all 7 FRs (FR-1..FR-7) traced.
>
> IMPLEMENTATION FACTS (tested as built): the existing system-level `SubscriptionPlan` entity (from US-ADM-001) is EXTENDED to the full FR-2 schema (numeric limits, `enabled_modules` jsonb, `feature_flags` jsonb, prices, currency, sla_tier, audit_log_retention_days, trial_days); a NEW `plan_limit_override` table (tenant_id, limit_key, value, expires_at) is added; a migration carries both. Full CRUD from the System Admin context: LIST shows all plans + active-tenant-count per plan + sort by name/price/tenant-count (AC-1/FR-5, TC-ADM-009-01); CREATE persists the full schema + makes the plan assignable + audits (AC-2, -02); code is unique + lowercase-alnum-hyphen format + immutable after creation (FR-3/BR-5, -03/-04/-05); `enabled_modules` validated against the canonical 13-module list with CoreHR always enabled (FR-6, -06); UPDATE reads limits LIVE so existing tenants benefit immediately + before/after audit (AC-3, -07); ARCHIVE sets is_active=false, excludes from provisioning, existing tenants unaffected (AC-4, -08); DELETE guarded — rejected if any tenant (incl. terminated/retained) references the plan, archive-only (FR-7, -09). The pure `PlanLimitResolver` (FR-4) resolves a non-expired override > plan field, NULL = unlimited (BR-3), expired override ignored (AC-5, -10). Provisioning derives `tenant.EnabledModules` + `MaxEmployees` from the chosen plan (-11). AUTHZ: only SystemAdmin writes; SystemSupport/Billing read-only; tenant admins cannot view/modify (BR-1/NFR-5, -12). All ops audited to the system audit log with before/after (NFR-3, -13). ISOLATION: plans are system-only — tenant context cannot read/write them (TC-ADM-ISO-025); `plan_limit_override` is tenant-scoped — an override for Tenant X applies only to X (TC-ADM-ISO-026).
>
> DEFERRED (status: blocked; honest traceability, never fabricated): runtime per-endpoint + Angular-route MODULE GATING that actually blocks a disabled module's API/route across all controllers (BR-6/FR-6 runtime portion, TC-ADM-009-14) — `enabled_modules` is stored + `tenant.EnabledModules` derived (run-green -06/-11), but platform-wide runtime enforcement is a large cross-cutting concern. Redis plan cache + <60s `t:{tenantId}:config` propagation (NFR-1/NFR-4, TC-ADM-009-15) — Redis not wired; limits are read LIVE so propagation is IMMEDIATE today (no stale cache). BR-4 downgrade-doesn't-retroactively-block existing data (TC-ADM-009-16) — preservation half is inherent; the new-creation-block half depends on each module's create-time limit check (the EMPLOYEE limit via `Tenant.MaxEmployees` IS enforced today; storage/API/email/roles/fields/workflows/integrations/sessions are conditional on the owning module adding the check). Billing/Stripe + self-serve plan changes + coupons/proration (§10 Phase-2, TC-ADM-009-17). NFR-2 UI <1.5s perf (TC-ADM-009-18 — needs perf env; correctness in -01). PostgreSQL RLS DB-layer isolation for plan_limit_override (TC-ADM-ISO-027 — same RLS-deferred family as US-ADM-001..008/Payroll/Leave).
>
> STORY MISMATCH worth flagging to the caller: (1) BR-6/FR-6 + §9 assume a working module-gating layer (Angular route guards + ASP.NET Core authorization policies) that gates every module's routes/APIs per tenant — only the entitlement STORAGE + DERIVATION exists today; reword so runtime gating is a follow-on cross-cutting story. (2) NFR-1/NFR-4 + §9 assume Redis is wired for plan-config caching + <60s propagation — Redis is not wired; the live-read path already makes AC-3 propagation immediate, so the Redis/60s contract is a future optimization, not a correctness gap. (3) §10 already scopes billing/self-serve/coupons/proration as Phase-2 (correctly), so AC-1's price columns are definitional only in Phase-1.

## Coverage by Test Case (US-ADM-009)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category | Status |
|-----------|-------|------|----------|--------------------|----------|--------|
| TC-ADM-009-01 | Plan list: all fields + active-tenant-count + sort name/price/count | E2E | Critical | AC-1, FR-1/5, BR-2 | Happy path / boundary | draft |
| TC-ADM-009-02 | Create plan with full FR-2 schema — persisted, assignable, audited | E2E | Critical | AC-2, FR-1/2/6, NFR-3 | Happy path | draft |
| TC-ADM-009-03 | Unique-code rejection (incl. archived-code reuse) | Functional | Critical | FR-3, BR-5 | Negative / boundary | draft |
| TC-ADM-009-04 | Code format — lowercase alphanumeric + hyphens only | Functional | High | FR-3 | Negative / boundary | draft |
| TC-ADM-009-05 | Code immutability — update cannot change code | Functional | High | FR-3, BR-5 | Negative | draft |
| TC-ADM-009-06 | enabled_modules validated vs canonical list; CoreHR always on | Functional | High | FR-6, BR-6 | Negative / boundary | draft |
| TC-ADM-009-07 | Edit propagation — limits read live, tenants benefit immediately; before/after audit | Integration | Critical | AC-3, FR-1/2, NFR-3 | Happy path / boundary | draft |
| TC-ADM-009-08 | Archive — is_active=false, excluded from provisioning, existing unaffected, audited | Integration | Critical | AC-4, FR-1/7, NFR-3 | Happy path / negative | draft |
| TC-ADM-009-09 | Delete guard — referenced plan (incl. terminated) rejected, archive-only | Functional | Critical | FR-7 | Negative / boundary | draft |
| TC-ADM-009-10 | PlanLimitResolver — override > plan; NULL=unlimited; expiry falls back; audited | Functional | Critical | AC-5, FR-4, BR-3, NFR-3 | Happy / negative / boundary | draft |
| TC-ADM-009-11 | Provisioning inherits plan — tenant.EnabledModules + MaxEmployees derived | Integration | Critical | FR-2/6, US-ADM-001 dep | Happy path | draft |
| TC-ADM-009-12 | Authz — SystemAdmin writes; SystemSupport/Billing read-only; tenant admins excluded | Security | Critical | BR-1, NFR-5, FR-1 | Negative / security / isolation | draft |
| TC-ADM-009-13 | Audit completeness sweep — create/edit/archive/delete-attempt/override | Integration | High | NFR-3, AC-2/3/4/5 | Negative / security | draft |
| TC-ADM-009-14 | [DEFERRED] Runtime module gating — disabled module API + Angular route blocked | Integration | High | BR-6, FR-6 (runtime) | Deferred placeholder | blocked |
| TC-ADM-009-15 | [DEFERRED] Redis plan cache + <60s `t:{tenantId}:config` propagation | Integration | Medium | NFR-1/4, AC-3 | Deferred placeholder | blocked |
| TC-ADM-009-16 | [DEFERRED/CONDITIONAL] Downgrade not retroactive; over-limit new creations blocked | Integration | High | BR-4 | Deferred / conditional | blocked |
| TC-ADM-009-17 | [DEFERRED] Billing/Stripe + self-serve + coupons/proration (Phase 2) | Integration | Low | §10, BR-2 | Deferred placeholder | blocked |
| TC-ADM-009-18 | [DEFERRED] Plan management UI loads <= 1.5s | Performance | Medium | NFR-2 | Deferred placeholder | blocked |
| TC-ADM-ISO-025 | Plans system-only — tenant context cannot read/write; context injection rejected | Security | Critical | FR-1, BR-1, NFR-5 | Multi-tenant isolation | draft |
| TC-ADM-ISO-026 | PlanLimitOverride tenant-scoped — Tenant X override applies only to X | Security | Critical | AC-5, FR-4 | Multi-tenant isolation | draft |
| TC-ADM-ISO-027 | [DEFERRED] PostgreSQL RLS DB-layer isolation for plan_limit_override | Security | Medium | FR-4 (DB hardening) | Deferred placeholder | blocked |

## Acceptance-Criteria Coverage (US-ADM-009)

| AC | Covered By | Notes |
|----|-----------|-------|
| AC-1 (plan list: code/name/price/currency/tenant-count/public/active + sortable) | TC-ADM-009-01 (+ -18 DEFERRED perf) | Direct |
| AC-2 (create full schema; assignable; audited) | TC-ADM-009-02, -06 | Direct |
| AC-3 (edit; existing tenants benefit immediately via live read; before/after audit) | TC-ADM-009-07 (+ -15 DEFERRED Redis/60s; immediate via live read) | Direct |
| AC-4 (archive; is_active=false; excluded from provisioning; existing unaffected; logged) | TC-ADM-009-08 | Direct |
| AC-5 (custom plan + per-tenant override; override > plan; audited) | TC-ADM-009-10, TC-ADM-ISO-026 (+ -027 DEFERRED RLS) | Direct |

## BR / FR / NFR Coverage (US-ADM-009)

| Requirement | Covered By | Notes |
|-------------|-----------|-------|
| BR-1 (only SystemAdmin writes; SystemSupport/Billing read-only) | TC-ADM-009-12, TC-ADM-ISO-025 | Direct |
| BR-2 (is_public pricing-page eligibility — Phase-2 self-serve) | TC-ADM-009-01 (surfaced) + -17 (DEFERRED self-serve) | Flag surfaced; self-serve deferred |
| BR-3 (NULL = unlimited) | TC-ADM-009-10 | Direct |
| BR-4 (lowering a limit not retroactive; over-limit new creations blocked) | TC-ADM-009-16 (DEFERRED/CONDITIONAL) | Employee dim real (-07); others module-conditional |
| BR-5 (code not reusable, even archived; immutable) | TC-ADM-009-03, -05 | Direct |
| BR-6 (enabled_modules gate Angular modules + API endpoints) | TC-ADM-009-06 (storage/validation) + -14 (DEFERRED runtime gating) | Storage real; runtime gating deferred |
| BR-7 (plan changes don't affect in-flight operations) | TC-ADM-009-07 (live-read consistency); in-flight payroll-run continuity is module-owned | Live-read consistent; in-flight pinning is owning-module |
| FR-1 (CRUD from system admin context) | TC-ADM-009-01/-02/-07/-08/-09, TC-ADM-ISO-025 | Direct |
| FR-2 (full schema exposed/persisted) | TC-ADM-009-02 | Direct |
| FR-3 (code unique + lowercase-alnum-hyphen + immutable) | TC-ADM-009-03, -04, -05 | Direct |
| FR-4 (plan_limit_override table + resolution order) | TC-ADM-009-10, TC-ADM-ISO-026 (+ -027 DEFERRED RLS) | Direct |
| FR-5 (active-tenant-count per plan) | TC-ADM-009-01 | Direct |
| FR-6 (enabled_modules canonical list; CoreHR always on) | TC-ADM-009-06, -11 (+ -14 DEFERRED runtime gating) | Validation real; gating deferred |
| FR-7 (no delete if referenced; archive only) | TC-ADM-009-09 | Direct |
| NFR-1 (<60s propagation via cache invalidation) | TC-ADM-009-15 (DEFERRED) | Immediate via live read; Redis/60s deferred |
| NFR-2 (UI <= 1.5s) | TC-ADM-009-18 (DEFERRED) | Needs perf env; correctness -01 |
| NFR-3 (all ops audited with full before/after) | TC-ADM-009-13, -02/-07/-08/-10 | Direct |
| NFR-4 (plan data cached in Redis, invalidated on update) | TC-ADM-009-15 (DEFERRED) | Redis not wired; live read today |
| NFR-5 (system-console-only; tenant admins cannot view/modify) | TC-ADM-009-12, TC-ADM-ISO-025 | Direct |

## Summary (US-ADM-009)

| Metric | Value |
|--------|-------|
| User stories covered | 1 (US-ADM-009) |
| Total test cases | 21 (13 functional/security/integration/e2e + 5 DEFERRED + 3 isolation, 1 of which DEFERRED) |
| AC coverage | 5/5 (AC-1/3/5 have DEFERRED sub-parts: perf, Redis/60s, RLS) |
| BR coverage | 7/7 (BR-1..BR-7; BR-4 conditional, BR-6 runtime gating deferred) |
| FR coverage | 7/7 (FR-1..FR-7; FR-6 runtime gating deferred) |
| Run-green now | 15 (TC-ADM-009-01..13 + TC-ADM-ISO-025, -026) |
| Deferred (status: blocked) | 6 (TC-ADM-009-14, -15, -16, -17, -18 + TC-ADM-ISO-027) |
| Functional ID range | TC-ADM-009-01 .. TC-ADM-009-18 |
| ISO ID range | TC-ADM-ISO-025 .. TC-ADM-ISO-027 |

---

## US-ADM-010 — Tenant Data Export on Demand

> TENTH and FINAL Admin Console story (DUAL persona: **Tenant Admin** exports own tenant via `ITenantContext`; **System Admin** exports any tenant via explicit `tenantId`). 24 test cases: 15 run-green functional/security/integration/e2e (TC-ADM-010-01..15) + 5 DEFERRED (TC-ADM-010-16..20) + 4 dedicated multi-tenant isolation (TC-ADM-ISO-028..031, continuing the running ISO counter; -031 DEFERRED). All 6 ACs (AC-1..AC-6), all 7 BRs (BR-1..BR-7), all 9 FRs (FR-1..FR-9) traced.
>
> IMPLEMENTATION FACTS (tested as built): a NEW `ExportRequest` entity (Queued/Processing/Completed/Failed/Expired). A Hangfire job generates per-entity CSVs (UTF-8 **BOM**, comma delimiter, header row, `AsNoTracking`), an `audit_log.jsonl` (one JSON object per line), and a `manifest.json` (`export_id`, `tenant_id`, `tenant_name`, `export_timestamp`, `scope`, and per-file `{filename, entity, row_count, file_size_bytes, sha256_checksum}`), packaged as a ZIP at `{tenantId}/exports/{export_id}/export_bundle.zip`. Sensitive-AUTH fields (password hashes, MFA secrets, token hashes) are NEVER in any CSV — the Users export is name/email/roles only (FR-8/BR-7); **PII (national id, bank account) IS included** (FR-8). Status gate (BR-2/BR-3, AC-4): Active/Trial/PastDue/Terminating allowed; **Suspended rejected for Tenant Admin but allowed for System Admin**; Terminated rejected for both. Rate limit (BR-5/FR-9): one concurrent export per tenant + max 3 per calendar month ("Monthly export limit reached."). Download (AC-3/FR-7): served only while Completed & now < 72h `ExpiresAt`; the cleanup job marks `Expired` + deletes the file. Audit on initiation/completion/download (NFR-4); a System-Admin export is dual-audited (system + tenant logs) with the System Admin as actor (AC-6). Tenant-Admin client-supplied `tenant_id` is IGNORED — export scoped to the resolved tenant (AC-5); cross-tenant export_id download injection -> **404 not 403**. Isolation runs on the EF global query filter (read) + TenantInterceptor (write), per module convention.
>
> DEFERRED (status: blocked; honest traceability, never fabricated): real email DELIVERY + pre-signed S3 / signed-link mechanics (FR-7/AC-2/BR-6, TC-ADM-010-16) — today the link is log-only and the bundle is served from a local tenant-scoped path; the 72h expiry + cleanup + audited download ARE run-green (TC-ADM-010-12). Schema-documentation PDF + "PII clearly marked" (FR-2/FR-8/§10, TC-ADM-010-17) — static build-time stub; PII INCLUSION in CSVs is run-green (TC-ADM-010-05). At-rest encryption + HTTPS (NFR-3, TC-ADM-010-18) — infra. Read-replica/streaming for >50k records + 30-min perf (NFR-1/NFR-2, TC-ADM-010-19) — needs a perf-representative env; `AsNoTracking` is in use and correctness is in TC-ADM-010-01. Uploaded-documents ZIP subtree (FR-4, TC-ADM-010-20) — no blob storage wired (same gap as TC-ADM-004-19). PostgreSQL RLS DB-layer isolation (AC-5, TC-ADM-ISO-031) — deferred RLS family (US-ADM-001..009 / Payroll / Leave).
>
> STORY MISMATCH worth flagging to the caller: (1) AC-5 names PostgreSQL RLS as an active third isolation layer — only the app (ITenantContext) + EF (query filter / TenantInterceptor) layers exist today; reword RLS as future hardening (TC-ADM-ISO-031). (2) AC-2/AC-3/FR-7 assume a signed-URL + email transport (S3 pre-signed + delivery to billing contact) — neither is wired; the bundle is served from a local path and the link is log-only (TC-ADM-010-16). (3) AC-2/FR-4 assume an uploaded-documents ZIP subtree — no blob storage exists (TC-ADM-010-20). (4) §10 correctly scopes the schema PDF as a static build-time artifact, so AC-2's "schema documentation file" is definitional in Phase-1 (TC-ADM-010-17).

## Coverage by Test Case (US-ADM-010)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category | Status |
|-----------|-------|------|----------|--------------------|----------|--------|
| TC-ADM-010-01 | Full export bundle — CSVs + audit_log.jsonl + manifest, packaged ZIP | E2E | Critical | AC-1/2, FR-1/2/5/6 | Happy path | draft |
| TC-ADM-010-02 | Manifest validation — SHA-256 + row counts match actual files | Integration | Critical | AC-2, FR-6 | Happy / negative / security | draft |
| TC-ADM-010-03 | Partial export — only selected entities' CSVs | Functional | High | AC-1, FR-1/2 | Boundary | draft |
| TC-ADM-010-04 | Sensitive-auth fields excluded — no pw/MFA/token hashes in any CSV | Security | Critical | FR-8, BR-7 | Negative / security | draft |
| TC-ADM-010-05 | PII fields INCLUDED (national id, bank account) | Functional | High | FR-8 | Boundary | draft |
| TC-ADM-010-06 | CSV format — UTF-8 BOM, comma delimiter, header row | Functional | High | FR-3 | Boundary | draft |
| TC-ADM-010-07 | Audit log export as JSON Lines (audit_log.jsonl) | Functional | High | AC-2, FR-5 | Boundary | draft |
| TC-ADM-010-08 | Terminating-tenant export ALLOWED (grace-period extraction) | Functional | Critical | AC-4, BR-3 | Boundary / security | draft |
| TC-ADM-010-09 | Suspended — Tenant Admin REJECTED / System Admin ALLOWED | Security | Critical | AC-4/6, BR-1/2 | Negative / boundary / security | draft |
| TC-ADM-010-10 | Terminated tenant — export REJECTED for both personas | Security | High | AC-4, BR-3 | Negative / boundary | draft |
| TC-ADM-010-11 | Rate limit — 3/calendar month + one concurrent per tenant | Functional | Critical | FR-9, BR-5 | Negative / boundary | draft |
| TC-ADM-010-12 | Download served while Completed & <72h; expiry -> Expired + file deleted | Functional | Critical | AC-3, FR-7 | Happy / negative / boundary | draft |
| TC-ADM-010-13 | System-Admin export — dual audit with System Admin as actor | Security | Critical | AC-6, BR-1 | Happy / security | draft |
| TC-ADM-010-14 | Audit trail — initiation + completion + download recorded | Integration | High | AC-1/3, NFR-4 | Negative / security | draft |
| TC-ADM-010-15 | Tenant-Admin scoped to own tenant — foreign tenant_id ignored | Security | Critical | AC-5, FR-1, BR-1 | Negative / security / isolation | draft |
| TC-ADM-010-16 | [DEFERRED] Email delivery + pre-signed/signed download URL | Integration | High | AC-2/3, FR-7, BR-6 | Deferred placeholder | blocked |
| TC-ADM-010-17 | [DEFERRED] Schema-documentation PDF + PII "clearly marked" | Functional | Medium | AC-2, FR-2/8, §10 | Deferred placeholder | blocked |
| TC-ADM-010-18 | [DEFERRED] Bundle encrypted at rest + HTTPS in transit | Security | Medium | NFR-3 | Deferred placeholder | blocked |
| TC-ADM-010-19 | [DEFERRED] 50k/10GB in 30 min + read-replica/streaming | Performance | Medium | NFR-1/2, FR-4 | Deferred placeholder | blocked |
| TC-ADM-010-20 | [DEFERRED] Uploaded-documents ZIP subtree by entity | Integration | Medium | AC-2, FR-4 | Deferred placeholder | blocked |
| TC-ADM-010-21 | Service targets `/tenant/data-exports` + `/system/.../data-exports` (BUG-104 regression) | Functional | High | AC-1, AC-6, FR-9 | Happy path / Negative | automated |
| TC-ADM-ISO-028 | Cross-tenant — export bundle has ZERO Tenant B rows | Security | Critical | AC-5, FR-2, BR-1 | Multi-tenant isolation | draft |
| TC-ADM-ISO-029 | Export endpoints need context; foreign tenant_id ignored; cross-tenant download -> 404 | Security | Critical | AC-5, FR-1/7, BR-1 | Multi-tenant isolation | draft |
| TC-ADM-ISO-030 | EF query filter scopes export queries; ExportRequest + path tenant-stamped | Security | Critical | AC-5, FR-2/6 | Multi-tenant isolation | draft |
| TC-ADM-ISO-031 | [DEFERRED] PostgreSQL RLS DB-layer isolation for the export pipeline | Security | Medium | AC-5 (DB hardening) | Deferred placeholder | blocked |

## Acceptance-Criteria Coverage (US-ADM-010)

| AC | Covered By | Notes |
|----|-----------|-------|
| AC-1 (initiate full/partial; Hangfire job enqueued; confirmation; logged) | TC-ADM-010-01, -03, -14 | Direct (email confirmation copy is FE/log; delivery deferred -16) |
| AC-2 (bundle: CSVs + documents + audit jsonl + schema PDF + manifest; signed link emailed) | TC-ADM-010-01, -02, -07 (CSV/audit/manifest real) + -17 (DEFERRED PDF) + -20 (DEFERRED docs) + -16 (DEFERRED signed link/email) | CSV/audit/manifest/checksum real; PDF/docs/email deferred |
| AC-3 (download within 72h; expire + delete after; logged) | TC-ADM-010-12, -14 (+ -16 DEFERRED signed link) | Serve/expiry/cleanup/audit real; signed-URL transport deferred |
| AC-4 (terminating allowed; suspended/terminated excluded) | TC-ADM-010-08, -09, -10 | Direct |
| AC-5 (Tenant-Admin own-tenant only; foreign tenant_id ignored; RLS) | TC-ADM-010-15, TC-ADM-ISO-028, -029, -030 (+ -031 DEFERRED RLS) | App+EF real; RLS deferred |
| AC-6 (System-Admin export; dual audit, System Admin as actor; link to admin+billing) | TC-ADM-010-13, -09 (+ -16 DEFERRED email) | Dual audit + content real; email deferred |

## BR / FR / NFR Coverage (US-ADM-010)

| Requirement | Covered By | Notes |
|-------------|-----------|-------|
| BR-1 (Tenant Admin own tenant; System Admin any) | TC-ADM-010-13/-15, TC-ADM-ISO-028/-029/-030 | Direct |
| BR-2 (suspended: Tenant Admin blocked, System Admin allowed) | TC-ADM-010-09 | Direct |
| BR-3 (export available during terminating, not after) | TC-ADM-010-08, -10 | Direct |
| BR-4 (no system-level data; tenant-scoped only) | TC-ADM-010-01/-03 (scope is tenant entities) | Inherent in tenant-scoped export set |
| BR-5 (one concurrent + max 3/month) | TC-ADM-010-11 | Direct |
| BR-6 (link to requester + billing contact) | TC-ADM-010-16 (DEFERRED) | Recipients deferred with email transport |
| BR-7 (auth secrets never exported) | TC-ADM-010-04 | Direct |
| FR-1 (initiation: scope full/array; context vs explicit tenant) | TC-ADM-010-01/-03/-15, TC-ADM-ISO-029 | Direct |
| FR-2 (per-entity query + CSV + package; schema doc) | TC-ADM-010-01/-03 (+ -17 DEFERRED schema PDF) | CSV real; PDF deferred |
| FR-3 (UTF-8 BOM, comma, headers) | TC-ADM-010-06 | Direct |
| FR-4 (documents subtree by entity) | TC-ADM-010-20 (DEFERRED) | No blob storage today |
| FR-5 (audit_log.jsonl) | TC-ADM-010-07 | Direct |
| FR-6 (manifest contents incl. row_count/size/sha256) | TC-ADM-010-01, -02 | Direct |
| FR-7 (signed URL 72h + cleanup deletes files) | TC-ADM-010-12 (expiry/cleanup real) + -16 (DEFERRED signed URL) | Expiry/cleanup real; signed-URL transport deferred |
| FR-8 (auth fields excluded; PII included) | TC-ADM-010-04 (excluded) + -05 (PII included) (+ -17 DEFERRED "marked") | Direct |
| FR-9 (one export at a time) | TC-ADM-010-11 | Direct |
| NFR-1 (30 min @ 50k/10GB) | TC-ADM-010-19 (DEFERRED) | Needs perf env |
| NFR-2 (AsNoTracking + read replica + streaming) | TC-ADM-010-19 (DEFERRED) | AsNoTracking real (in -01); replica/streaming deferred |
| NFR-3 (encrypted at rest + HTTPS) | TC-ADM-010-18 (DEFERRED) | Infra |
| NFR-4 (initiation/completion/download/expiry audited) | TC-ADM-010-14, -12, -13 | Direct |
| NFR-5 (responsive mobile UI) | (FE-verified during FE story; not separately scripted here) | FE-verified |

## Summary (US-ADM-010)

| Metric | Value |
|--------|-------|
| User stories covered | 1 (US-ADM-010) |
| Total test cases | 24 (15 functional/security/integration/e2e + 5 DEFERRED + 4 isolation, 1 of which DEFERRED) |
| AC coverage | 6/6 (AC-2/3/5/6 have DEFERRED sub-parts: schema PDF, docs, signed-URL/email, RLS) |
| BR coverage | 7/7 (BR-1..BR-7; BR-6 recipients deferred with email transport) |
| FR coverage | 9/9 (FR-1..FR-9; FR-2 schema-PDF / FR-4 docs / FR-7 signed-URL portions deferred) |
| Run-green now | 19 (TC-ADM-010-01..15 + TC-ADM-ISO-028, -029, -030) |
| Deferred (status: blocked) | 6 (TC-ADM-010-16..20 + TC-ADM-ISO-031) |
| Functional ID range | TC-ADM-010-01 .. TC-ADM-010-21 |
| ISO ID range | TC-ADM-ISO-028 .. TC-ADM-ISO-031 |

---

## Module Totals — Admin Console COMPLETE

All 10 Admin Console user stories (US-ADM-001 .. US-ADM-010) now have IEEE 829 test coverage. US-ADM-010 is the LAST story; the module test suite is complete.

| Metric | Value |
|--------|-------|
| User stories covered | 10 (US-ADM-001 .. US-ADM-010) — COMPLETE |
| Total test cases | 217 (193 prior + 24 for US-ADM-010) |
| Functional ID scheme | per-story suffix TC-ADM-{NNN}-XX |
| ISO ID range (module) | TC-ADM-ISO-001 .. TC-ADM-ISO-031 |
| AC coverage | Every AC of every story has >= 1 test case (deferred sub-parts honestly marked status: blocked) |
