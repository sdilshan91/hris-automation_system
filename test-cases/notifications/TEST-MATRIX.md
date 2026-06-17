---
module: Notifications & Audit
total_user_stories: 1
total_test_cases: 16
created: 2026-06-17
updated: 2026-06-17
status: in-progress
---

# Notifications & Audit -- Test Matrix

> US-NTF-001 (In-App Notification System -- Real-Time via SignalR) is the FIRST Notifications story and establishes `test-cases/notifications/` (dir + this TEST-MATRIX + the root Notifications section in TRACEABILITY-MATRIX). It adds 16 test cases: 12 functional/integration/security/performance/accessibility (TC-NTF-001-01..12) + 4 dedicated multi-tenant isolation (TC-NTF-ISO-001..004). The Notifications module reuses the per-story-suffix functional ID scheme from Recruitment/Payroll/Admin Console/Onboarding (TC-NTF-{NNN}-XX) with a separate running ISO counter (TC-NTF-ISO-NNN) starting at 001. All 6 acceptance criteria of US-NTF-001 are covered.
>
> PLATFORM ACCURACY / DEFERRED: (1) NFR-2 specifies PostgreSQL RLS as a tenant-isolation layer. This codebase isolates via **EF Core global query filters (read) + `TenantInterceptor` (write stamping)**, NOT Postgres RLS — RLS is a deferred platform extension (same family as Auth/Leave/Payroll/Admin/Onboarding). Isolation tests (TC-NTF-ISO-001..004) assert the EF mechanism in force today; the "raw SQL without app.current_tenant_id returns zero rows" RLS expectation is documented as CONDITIONAL/deferred (TC-NTF-ISO-003 step 5). Cross-tenant REST ID injection asserts **404, not 403** (existence not disclosed, TC-NTF-ISO-001/-002). (2) Redis is a hard dependency for the SignalR backplane (FR-10) and for NFR-3 (>= 5,000 concurrent connections/instance) and multi-instance fan-out; perf (TC-NTF-001-12) and reconnection (TC-NTF-001-10) need a perf/multi-instance-representative environment — on a dev box record indicative numbers and do NOT relax the 2s NFR-1 threshold. (3) An unread-count cache is treated as conditional: TC-NTF-ISO-004 asserts the tenant+user key shape `notifications:unread:{tenant_id}:{user_id}` if wired, else the equivalent always-tenant-filtered computation. (4) NFR-5 polling fallback (30s) is exercised inside TC-NTF-001-10.
>
> STORY MISMATCH worth flagging to the caller: (a) NFR-2 names PostgreSQL RLS as an active isolation layer — only the app (ITenantContext) + EF (query filter / TenantInterceptor) layers exist today; reword RLS as future hardening (consistent with prior modules). (b) BR-2 (archive notifications > 90 days via Hangfire) and BR-3 (purge beyond 1000 per user) are lifecycle/retention concerns with no UI/endpoint in this real-time-delivery story — out of scope for US-NTF-001; cover under a dedicated retention story. (c) BR-4 (system-generated notifications via the Notification Dispatcher, not directly via SignalR) describes a producer-side architecture not surfaced as a testable UI flow here — flag for a Dispatcher story. (d) AC-3 sound notification (toggleable) is listed only in UI/UX notes (S8), not in the ACs/FRs — treated as optional, not formally tested.

## Coverage by Test Case

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category |
|-----------|-------|------|----------|--------------------|----------|
| TC-NTF-001-01 | Leave approval -> real-time notification within 2s; persisted w/ tenant_id | E2E | Critical | AC-2, FR-3/4/5, NFR-1 | Happy path |
| TC-NTF-001-02 | Badge increments on arrival, decrements on read, persists on reload | Functional | High | AC-2, AC-4, FR-5/7 | Happy / boundary |
| TC-NTF-001-03 | "Mark All as Read" resets badge to 0 and persists to DB | Functional | High | AC-5, FR-7/3 | Happy path |
| TC-NTF-001-04 | Click notification -> mark read + decrement + navigate to resource | Functional | High | AC-3, AC-4, FR-7/8 | Happy / boundary |
| TC-NTF-001-05 | SignalR connection established + tenant/user/role groups joined on bootstrap | Integration | Critical | AC-1, FR-1/2 | Happy / security |
| TC-NTF-001-06 | Unauthenticated SignalR connection rejected; no group join | Security | Critical | AC-1, FR-1 | Negative / security |
| TC-NTF-001-07 | Mark-read on another user's notification denied (IDOR -> 404) | Security | Critical | AC-4, AC-5, FR-7/3 | Negative / security |
| TC-NTF-001-08 | Pagination 20/page with infinite scroll; DESC order; empty/boundary | Functional | High | AC-3, FR-6 | Boundary |
| TC-NTF-001-09 | Badge "99+" display cap (99/100/250 boundary) | Functional | Medium | AC-2, FR-5 | Boundary / cross-browser |
| TC-NTF-001-10 | Reconnection w/ exponential backoff; missed delivered; polling fallback | Integration | High | AC-1, AC-2, FR-9/10, NFR-5 | Negative / boundary |
| TC-NTF-001-11 | ARIA live region announces new notifications; keyboard nav; WCAG 2.1 AA; responsive | Accessibility | Medium | AC-2, AC-3, NFR-6/4 | Accessibility / cross-browser |
| TC-NTF-001-12 | End-to-end delivery latency <= 2s P95; backplane fan-out; concurrency | Performance | High | AC-2, FR-4/10, NFR-1/3 | Performance |
| TC-NTF-ISO-001 | User B (Tenant B) does NOT receive Tenant A's notification | Security | Critical | AC-6, BR-1/5, NFR-2 (EF) | Multi-tenant isolation |
| TC-NTF-ISO-002 | Missing tenant context + cross-tenant ID/group injection -> 404 / hub reject | Security | Critical | AC-6, FR-2/3, BR-5 | Multi-tenant isolation |
| TC-NTF-ISO-003 | EF filter blocks cross-tenant reads; writes tenant-stamped (RLS deferred) | Security | Critical | AC-6, FR-3, NFR-2 | Multi-tenant isolation |
| TC-NTF-ISO-004 | SignalR groups/backplane channels/unread-count cache tenant-scoped | Security | High | AC-6, FR-2/5/10, NFR-2 | Multi-tenant isolation |

## Acceptance-Criteria Coverage (US-NTF-001)

| AC | Covered By |
|----|-----------|
| AC-1 (SignalR connection on bootstrap, JWT auth, tenant/user/role group join) | TC-NTF-001-05, -06, -10 |
| AC-2 (leave approval -> real-time notification <= 2s; badge increment + slide-in) | TC-NTF-001-01, -02, -09, -11, -12 |
| AC-3 (panel shows paginated list w/ icon/title/message/relative time/read status) | TC-NTF-001-04, -08, -11 |
| AC-4 (click -> mark read + decrement badge + navigate to resource) | TC-NTF-001-02, -04, -07 |
| AC-5 (Mark All as Read -> all read, badge=0, persisted) | TC-NTF-001-03 |
| AC-6 (tenant/user isolation; User B does not receive User A's notification) | TC-NTF-ISO-001, -002, -003, -004 |

## FR / NFR / BR Coverage

| Requirement | Covered By |
|-------------|-----------|
| FR-1 (connect on bootstrap, JWT) | TC-NTF-001-05, -06 |
| FR-2 (join tenant/user/role groups) | TC-NTF-001-05, TC-NTF-ISO-002, -004 |
| FR-3 (persist notification, tenant-scoped) | TC-NTF-001-01, -03, -07, TC-NTF-ISO-003 |
| FR-4 (real-time push) | TC-NTF-001-01, -12 |
| FR-5 (unread badge, max "99+") | TC-NTF-001-02, -09, TC-NTF-ISO-004 |
| FR-6 (pagination 20/page, infinite scroll) | TC-NTF-001-08 |
| FR-7 (mark as read + mark all as read) | TC-NTF-001-02, -03, -04, -07 |
| FR-8 (navigate to resource) | TC-NTF-001-04 |
| FR-9 (auto-reconnect, exponential backoff) | TC-NTF-001-10 |
| FR-10 (Redis backplane, multi-instance) | TC-NTF-001-10, -12, TC-NTF-ISO-004 |
| NFR-1 (<= 2s delivery latency) | TC-NTF-001-01, -12 |
| NFR-2 (tenant isolation; RLS deferred -> EF filters) | TC-NTF-ISO-001, -002, -003, -004 |
| NFR-3 (>= 5,000 concurrent connections/instance) | TC-NTF-001-12 |
| NFR-4 (responsive 360px-4K) | TC-NTF-001-09, -11 |
| NFR-5 (degrade to 30s polling) | TC-NTF-001-10 |
| NFR-6 (WCAG 2.1 AA, ARIA live region, keyboard) | TC-NTF-001-11 |
| BR-1 (notifications scoped to current tenant) | TC-NTF-ISO-001, -003 |
| BR-2 (archive > 90 days via Hangfire) | Out of scope for this real-time-delivery story — retention/lifecycle concern (flag to caller) |
| BR-3 (purge beyond 1000 per user) | Out of scope for this story — retention/lifecycle concern (flag to caller) |
| BR-4 (system-generated via Dispatcher, not direct SignalR) | Producer-side architecture, no testable UI flow here (flag to caller; cover in a Dispatcher story) |
| BR-5 (cross-tenant group names rejected at hub) | TC-NTF-ISO-001, -002 |
