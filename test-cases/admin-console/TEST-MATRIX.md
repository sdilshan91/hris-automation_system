---
module: Admin Console
total_user_stories: 2
total_test_cases: 36
created: 2026-06-16
updated: 2026-06-16
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

## Module Totals

| Metric | Value |
|--------|-------|
| User stories covered | 2 (US-ADM-001, US-ADM-002) |
| Total test cases | 36 |
| ISO ID range (module) | TC-ADM-ISO-001 .. TC-ADM-ISO-006 |
