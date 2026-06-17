---
module: Notifications & Audit
total_user_stories: 5
total_test_cases: 80
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

---

## US-NTF-002 -- Email Notification Templates per Tenant

> US-NTF-002 (Email Notification Templates per Tenant) adds 16 test cases: 12 functional/integration/security/performance/accessibility (TC-NTF-002-01..12) + 4 multi-tenant isolation continuing the module-wide running ISO counter (TC-NTF-ISO-005..008, from US-NTF-001's 004). Functional suffix counter resets per story (TC-NTF-002-XX); the ISO counter is shared/running. All 5 acceptance criteria of US-NTF-002 are covered.
>
> PLATFORM ACCURACY / DEFERRED (carried from the US-NTF-001 family): (1) NFR-2 / AC-5 name PostgreSQL RLS as a tenant-isolation layer; this codebase isolates via **EF Core global query filters (read) + `TenantInterceptor` (write stamping)**, NOT Postgres RLS -- RLS is a deferred platform extension. ISO tests (TC-NTF-ISO-005..008) assert the EF mechanism in force today; the "raw SQL without app.current_tenant_id -> zero rows" RLS expectation is documented as CONDITIONAL/deferred (TC-NTF-ISO-007 step 5); cross-tenant REST ID injection asserts **404, not 403** (existence not disclosed, TC-NTF-ISO-005 step 4 / TC-NTF-ISO-006). (2) Email dispatch uses the outbox pattern: template rendering happens in the **Hangfire worker**, not inline -- send-time happy-path/fallback/render-isolation TCs (TC-NTF-002-01/-02, TC-NTF-ISO-008) exercise the worker path. (3) NFR-1 (editor load <= 1s P95) and NFR-3 (template+data render <= 200ms/email) need a perf-representative environment (TC-NTF-002-12); on a dev box record indicative numbers and do NOT relax the thresholds.
>
> STORY MISMATCH / SCOPE NOTES worth flagging to the caller: (a) NFR-2 / AC-5 name Postgres RLS as an active isolation layer -- only the app (ITenantContext) + EF (query filter / TenantInterceptor) layers exist today; reword RLS as future hardening (consistent with prior modules). (b) FR-7 (custom sender domain with SPF/DKIM setup guidance) and BR-4 (DNS verification before the custom sender is used) describe a domain-verification feature that is largely operational/DNS-dependent and not a core template-editing flow; it is NOT covered by a dedicated TC in this story and should be flagged for a separate "custom sender domain / deliverability" story (the platform cannot automate DNS, per S10). (c) Version history with diff highlighting (S8 UI/UX note) is exercised only as version-increment + before/after audit (TC-NTF-002-10, -12); the diff-rendering UI is not separately tested. (d) BR-6 variant cap is plan-configurable (default 2); TC-NTF-002-09 asserts the default-2 boundary and notes the plan-config path.

### Coverage by Test Case (US-NTF-002)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category |
|-----------|-------|------|----------|--------------------|----------|
| TC-NTF-002-01 | Custom "Leave Approved" for Tenant A used; placeholders resolved at send | E2E | Critical | AC-2, AC-3, FR-1/2/10, BR-1/3 | Happy path |
| TC-NTF-002-02 | No override -> system default used (fallback); never send without a template | Integration | Critical | AC-1, FR-6, BR-2 | Happy / boundary |
| TC-NTF-002-03 | Live preview renders placeholders with sample data; reference panel inserts | Functional | High | AC-2, FR-3/4 | Happy path |
| TC-NTF-002-04 | Reset to Default soft-deletes override; future emails revert + audit record | Functional | High | AC-4, FR-6/9 | Happy path |
| TC-NTF-002-05 | Send Test Email delivers rendered template to specified address; bad addr rejected | Integration | High | FR-8, FR-2, BR-3 | Happy / negative |
| TC-NTF-002-06 | Per-language variants (en + secondary); recipient language selects variant | Integration | High | FR-5, BR-6, BR-2 | Happy / boundary |
| TC-NTF-002-07 | Unresolved placeholder -> empty string, not raw token; send not aborted | Functional | High | BR-5, FR-2, BR-2 | Negative / boundary |
| TC-NTF-002-08 | Non-admin cannot view/edit/save/reset/send-test templates (authz) | Security | Critical | AC-1, AC-3, FR-1/8/9 | Negative / security |
| TC-NTF-002-09 | Max 2 language variants per template per tenant (3rd rejected) | Functional | Medium | BR-6, FR-5 | Negative / boundary |
| TC-NTF-002-10 | Template change audited with before/after via SaveChanges interceptor | Security | High | FR-9, NFR-6 | Security |
| TC-NTF-002-11 | Editor WCAG 2.1 AA; keyboard-operable; responsive 360px-4K | Accessibility | Medium | NFR-5, NFR-4 | Accessibility / cross-browser |
| TC-NTF-002-12 | List Default/Custom + version/last-modified; persist; load/render SLA | Performance | Medium | AC-1, AC-3, NFR-1/3, BR-3 | Happy / boundary / performance |
| TC-NTF-ISO-005 | Tenant A custom template invisible/unusable to Tenant B (ID injection -> 404) | Security | Critical | AC-5, NFR-2 (EF), BR-1 | Multi-tenant isolation |
| TC-NTF-ISO-006 | Missing tenant context rejected; cross-tenant template ID/tenant injection -> 404/ignored | Security | Critical | AC-5, FR-10/1/9, NFR-2 | Multi-tenant isolation |
| TC-NTF-ISO-007 | EF filter blocks cross-tenant reads; writes tenant-stamped + audited (RLS deferred) | Security | Critical | AC-5, AC-3, FR-10, NFR-2/6 | Multi-tenant isolation |
| TC-NTF-ISO-008 | Send/render pipeline selects templates strictly within recipient's tenant | Security | High | AC-5, FR-2/6/10, NFR-2 | Multi-tenant isolation |

### Acceptance-Criteria Coverage (US-NTF-002)

| AC | Covered By |
|----|-----------|
| AC-1 (template list shows all event types + Default/Custom status) | TC-NTF-002-02, -08, -12 |
| AC-2 (editor with placeholders + reference panel + live preview with sample data) | TC-NTF-002-01, -03 |
| AC-3 (save persists tenant override with tenant_id; future emails use custom) | TC-NTF-002-01, -10, -12, TC-NTF-ISO-007 |
| AC-4 (Reset to Default removes override, reverts to default, audit record) | TC-NTF-002-04 |
| AC-5 (Tenant A customization invisible to Tenant B; B sees system default) | TC-NTF-ISO-005, -006, -007, -008 |

### FR / NFR / BR Coverage (US-NTF-002)

| Requirement | Covered By |
|-------------|-----------|
| FR-1 (template editor: subject + HTML body + plain-text) | TC-NTF-002-01, -03, -08 |
| FR-2 (placeholder variables resolved at send time) | TC-NTF-002-01, -05, -07, TC-NTF-ISO-008 |
| FR-3 (variable reference panel) | TC-NTF-002-03 |
| FR-4 (live preview with sample data) | TC-NTF-002-03 |
| FR-5 (per-language template variants) | TC-NTF-002-06, -09 |
| FR-6 (fall back to system default if no override) | TC-NTF-002-02, -04, TC-NTF-ISO-008 |
| FR-7 (custom sender domain + SPF/DKIM guidance) | Out of scope for this story -- operational/DNS feature; flag for a "custom sender domain / deliverability" story |
| FR-8 (send a test email on demand) | TC-NTF-002-05 |
| FR-9 (log template changes in tenant audit log) | TC-NTF-002-04, -10 |
| FR-10 (tenant_id set from session on all overrides) | TC-NTF-002-01, TC-NTF-ISO-006, -007, -008 |
| NFR-1 (editor page load <= 1s P95) | TC-NTF-002-12 |
| NFR-2 (tenant isolation; RLS deferred -> EF filters) | TC-NTF-ISO-005, -006, -007, -008 |
| NFR-3 (email render <= 200ms/email) | TC-NTF-002-12 |
| NFR-4 (responsive 360px-4K) | TC-NTF-002-11 |
| NFR-5 (WCAG 2.1 AA editor) | TC-NTF-002-11 |
| NFR-6 (audited via SaveChanges interceptor) | TC-NTF-002-10, TC-NTF-ISO-007 |
| BR-1 (system defaults read-only; overrides take precedence) | TC-NTF-002-01, TC-NTF-ISO-005 |
| BR-2 (every event has a default; never send without a template) | TC-NTF-002-02, -06, -07 |
| BR-3 (HTML + plain-text versions both present) | TC-NTF-002-01, -05, -12 |
| BR-4 (custom sender requires DNS verification) | Out of scope -- tied to FR-7; flag for a deliverability story |
| BR-5 (unresolved placeholders -> empty string, not raw text) | TC-NTF-002-07 |
| BR-6 (max 2 language variants per template per tenant) | TC-NTF-002-06, -09 |

---

## US-NTF-003 -- Notification Preferences per User

> US-NTF-003 (Notification Preferences per User) adds 16 test cases: 12 functional/integration/security/performance/accessibility (TC-NTF-003-01..12) + 4 multi-tenant isolation continuing the module-wide running ISO counter (TC-NTF-ISO-009..012, from US-NTF-002's 008). Functional suffix counter resets per story (TC-NTF-003-XX); the ISO counter is shared/running. All 5 acceptance criteria of US-NTF-003 are covered.
>
> PLATFORM ACCURACY / DEFERRED (carried from the US-NTF-001 family): (1) NFR-2 names PostgreSQL RLS as a tenant-isolation layer; this codebase isolates via **EF Core global query filters (read) + `TenantInterceptor` (write stamping)**, NOT Postgres RLS -- RLS is a deferred platform extension. ISO tests (TC-NTF-ISO-009..012) assert the EF mechanism in force today; the "raw SQL without app.current_tenant_id -> zero rows" RLS expectation is documented as CONDITIONAL/deferred (TC-NTF-ISO-011 step 4); cross-tenant REST ID injection asserts **404, not 403** (existence not disclosed, TC-NTF-ISO-010). (2) NFR-3 specifies a Redis preference cache (TTL 5 min) consulted at dispatch time; Redis is a deferred infra item on the dev box -- TC-NTF-003-08, TC-NTF-003-12 and TC-NTF-ISO-012 are written CONDITIONAL on Redis being wired (assert tenant+user-scoped key `notif:prefs:{tenant_id}:{user_id}` and invalidation-on-change), else assert the equivalent always-fresh, always-tenant-scoped DB lookup so behavior is correct regardless -- the NFR-1 500ms / NFR-3 thresholds are never relaxed. (3) Quiet Hours email queuing (FR-9/BR-5) runs through the outbox/Hangfire worker; TC-NTF-003-07 exercises the worker/scheduled-release path and in-app real-time bypass.
>
> STORY MISMATCH / SCOPE NOTES worth flagging to the caller: (a) NFR-2 names Postgres RLS as an active isolation layer -- only the app (ITenantContext) + EF (query filter / TenantInterceptor) layers exist today; reword RLS as future hardening (consistent with prior modules). (b) FR-4/AC-4 mandatory categories are configured by the Tenant Admin via the Admin Console (S35.2.11) -- the configuration UI is out of scope for this per-user story; the mandatory flag is consumed here (TC-NTF-003-02) but its admin-side authoring belongs to an Admin Console story. (c) SMS channel (FR-3 "Phase 2") is out of scope; the data model accommodates it but no SMS TC is written. (d) FR-5 cascade (System < Tenant < User) is exercised via inheritance (TC-NTF-003-03) and reset (TC-NTF-003-04); tenant-default authoring is an Admin-side concern.

### Coverage by Test Case (US-NTF-003)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category |
|-----------|-------|------|----------|--------------------|----------|
| TC-NTF-003-01 | Disable email for "Leave Updates" -> leave approval in-app only, no email | E2E | Critical | AC-2, FR-6/3/8, BR-6 | Happy path |
| TC-NTF-003-02 | Mandatory "Security Alerts" toggle locked + tooltip; cannot disable (UI + API) | Functional | Critical | AC-3, AC-4, FR-4, BR-2 | Negative / security |
| TC-NTF-003-03 | New user inherits tenant default preferences | Integration | High | AC-1, FR-5/2/4, BR-1 | Happy / boundary |
| TC-NTF-003-04 | "Reset to Defaults" restores tenant-level defaults (+ cancel branch) | Functional | High | AC-1, FR-7/5, BR-1 | Happy / boundary |
| TC-NTF-003-05 | Cannot disable BOTH channels for a non-mandatory category (>= 1 stays on) | Functional | High | AC-2, FR-3/6, BR-3 | Negative / boundary |
| TC-NTF-003-06 | Invalid IANA timezone for Quiet Hours rejected; valid accepted; injection blocked | Functional | Medium | AC-2, FR-9 | Negative / boundary / security |
| TC-NTF-003-07 | Quiet Hours queues email at 23:00 (sent after 07:00); in-app stays real-time | E2E | High | AC-2, FR-9/6, BR-5 | Happy / boundary |
| TC-NTF-003-08 | Preference change invalidates cache; next dispatch reflects change (Redis-conditional) | Integration | High | AC-2, FR-6, NFR-3, BR-6 | Happy / boundary / performance |
| TC-NTF-003-09 | A user cannot modify another user's preferences (current-user only; IDOR -> 404) | Security | Critical | AC-2, FR-1/8, BR-4 | Negative / security |
| TC-NTF-003-10 | Unauthenticated / no-tenant-context preference requests rejected | Security | Critical | AC-2, AC-5, FR-8, NFR-2 | Negative / security |
| TC-NTF-003-11 | Toggles keyboard-navigable + ARIA labels; matrix collapses to cards at 360px | Accessibility | Medium | AC-1, AC-4, NFR-5/4 | Accessibility / cross-browser |
| TC-NTF-003-12 | Page load <= 500ms P95; dispatch lookup cheap (Redis cache, conditional) | Performance | Medium | AC-1, NFR-1/3 | Boundary / performance |
| TC-NTF-ISO-009 | Same user's prefs in Tenant X independent from Tenant Y | Security | Critical | AC-5, NFR-2 (EF), BR-4 | Multi-tenant isolation |
| TC-NTF-ISO-010 | Cross-tenant preference ID injection -> 404; missing tenant context rejected | Security | Critical | AC-5, FR-8, NFR-2, BR-4 | Multi-tenant isolation |
| TC-NTF-ISO-011 | EF filter blocks cross-tenant reads; writes tenant-stamped (RLS deferred) | Security | Critical | AC-5, FR-8, NFR-2, BR-4 | Multi-tenant isolation |
| TC-NTF-ISO-012 | Dispatch-time lookup + cache keys tenant+user scoped (Redis-conditional) | Security | High | AC-5, FR-6/8, NFR-2/3, BR-4 | Multi-tenant isolation |

### Acceptance-Criteria Coverage (US-NTF-003)

| AC | Covered By |
|----|-----------|
| AC-1 (matrix: row per category, channel toggle columns) | TC-NTF-003-03, -04, -11, -12 |
| AC-2 (disable email for Leave Updates -> in-app only, no email; persisted tenant_id+user_id) | TC-NTF-003-01, -05, -06, -07, -09, -10 |
| AC-3 (cannot disable all channels for mandatory category -> blocking message) | TC-NTF-003-02 |
| AC-4 (mandatory toggle greyed out with tooltip) | TC-NTF-003-02, -11 |
| AC-5 (cross-tenant user: per-membership independent preferences) | TC-NTF-003-10, TC-NTF-ISO-009, -010, -011, -012 |

### FR / NFR / BR Coverage (US-NTF-003)

| Requirement | Covered By |
|-------------|-----------|
| FR-1 (preference matrix per user, per tenant membership) | TC-NTF-003-09, TC-NTF-ISO-009 |
| FR-2 (notification categories) | TC-NTF-003-03 |
| FR-3 (channel toggles: In-App + Email) | TC-NTF-003-01, -05 |
| FR-4 (enforce mandatory categories) | TC-NTF-003-02, -03 |
| FR-5 (cascade System < Tenant < User; user overrides) | TC-NTF-003-03, -04 |
| FR-6 (preferences applied at dispatch time) | TC-NTF-003-01, -05, -07, -08, TC-NTF-ISO-012 |
| FR-7 ("Reset to Defaults" restores tenant defaults) | TC-NTF-003-04 |
| FR-8 (tenant_id + user_id set on all records) | TC-NTF-003-01, -09, -10, TC-NTF-ISO-010, -011, -012 |
| FR-9 (Quiet Hours setting in user's timezone) | TC-NTF-003-06, -07 |
| NFR-1 (page load <= 500ms P95) | TC-NTF-003-12 |
| NFR-2 (tenant isolation; RLS deferred -> EF filters) | TC-NTF-003-10, TC-NTF-ISO-009, -010, -011, -012 |
| NFR-3 (preference lookup cached in Redis, TTL 5 min) | TC-NTF-003-08, -12, TC-NTF-ISO-012 (Redis-conditional/deferred) |
| NFR-4 (responsive 360px-4K) | TC-NTF-003-11 |
| NFR-5 (WCAG 2.1 AA; toggles with ARIA labels) | TC-NTF-003-11 |
| BR-1 (new users inherit tenant defaults) | TC-NTF-003-03, -04 |
| BR-2 (mandatory categories cannot be disabled) | TC-NTF-003-02 |
| BR-3 (>= 1 channel must stay enabled for non-mandatory) | TC-NTF-003-05 |
| BR-4 (preferences per tenant membership) | TC-NTF-003-09, TC-NTF-ISO-009, -010, -011, -012 |
| BR-5 (Quiet Hours queues email, sends after end; in-app real-time) | TC-NTF-003-07 |
| BR-6 (changes take effect for future notifications) | TC-NTF-003-01, -08 |

---

## US-NTF-004 -- Audit Trail for All Data Changes

> US-NTF-004 (Audit Trail for All Data Changes) is a BACKEND/INFRASTRUCTURE story -- automatic audit capture via the EF Core `SaveChangesInterceptor` plus dedicated writers for read/auth/export events. There is NO new user-facing creation UI (the Audit Log Viewer is US-NTF-005, partly built in US-ADM-008); these TCs target the CAPTURE behavior, enrichment, immutability, and tenant isolation -- not UI. It adds 16 test cases: 12 functional/integration/security/performance (TC-NTF-004-01..12) + 4 multi-tenant isolation continuing the module-wide running ISO counter (TC-NTF-ISO-013..016, from US-NTF-003's 012). Functional suffix counter resets per story (TC-NTF-004-XX); the ISO counter is shared/running. All 5 acceptance criteria of US-NTF-004 are covered.
>
> PLATFORM ACCURACY / DEFERRED (carried from the US-NTF-001 family): (1) AC-5 / NFR-2 name PostgreSQL RLS as the audit isolation layer and the preconditions name an INSERT-only DB role (FR-6). This platform isolates via **EF Core global query filters (read) + the audit/Tenant interceptors (write stamping)**, NOT Postgres RLS, and the append-only DB-role grant is NOT yet provisioned -- both are deferred hardening. ISO tests (TC-NTF-ISO-013..016) assert the EF mechanism in force today; the "raw SQL without app.tenant_id -> zero rows" RLS expectation (TC-NTF-ISO-015 step 5) and the "app DB role cannot UPDATE/DELETE audit_log" grant (TC-NTF-004-08 step 4) are documented as CONDITIONAL/deferred. Cross-tenant audit-row ID access asserts **404, not 403** (existence not disclosed, TC-NTF-ISO-014). Immutability is asserted at the APPLICATION layer today (no endpoint/code path mutates audit rows). (2) BR-4 system-level audit (`system_audit_log` visible only to System Admins): if the current implementation uses a single audit table with a system/tenant discriminator rather than a separate table, TC-NTF-004-11 step 5 records that single-table reality and treats the dedicated table as a deferred refinement -- the tenant query path must still exclude system-level rows from a tenant admin. (3) NFR-1 (<=50ms/save overhead) and NFR-5 (bulk non-blocking) need a perf-representative environment (TC-NTF-004-09); on a dev box record indicative numbers and do NOT relax the 50ms threshold.
>
> STORY MISMATCH / SCOPE NOTES worth flagging to the caller: (a) AC-5 / NFR-2 name Postgres RLS and the preconditions name an INSERT-only DB role as ACTIVE controls -- neither exists today (EF query filter + interceptor stamping + "no mutating endpoint" are the controls in force); reword RLS + DB-role append-only as future hardening (consistent with prior modules). (b) The SaveChangesInterceptor captures only writes that flow through EF `SaveChanges`/`SaveChangesAsync`; raw SQL / Dapper writes BYPASS it and must be audited manually (S10 assumption) -- exercised + flagged in TC-NTF-004-12. (c) FR-9 (streaming export to ELK/Splunk) and NFR-6 (month/tenant-range partitioning, Phase 2) are deferred/Phase-2 -- noted in TC-NTF-004-12 step 6, not separately tested. (d) The Audit Log Viewer UI is US-NTF-005 -- not covered here.

### Coverage by Test Case (US-NTF-004)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category |
|-----------|-------|------|----------|--------------------|----------|
| TC-NTF-004-01 | INSERT audit -> "{Entity}.Create"; after-JSON populated, before null | Integration | Critical | AC-1, FR-1/2, BR-2 | Happy path |
| TC-NTF-004-02 | UPDATE audit -> before old / after new; ONLY changed fields captured; no-op = no row | Integration | Critical | AC-1, FR-1/2, BR-3 | Happy / boundary |
| TC-NTF-004-03 | Soft-delete (is_deleted false->true) -> "{Entity}.Delete" before/after on flag | Integration | High | AC-3, FR-1, BR-2 | Happy / boundary |
| TC-NTF-004-04 | Auth events (login success/failure, logout, password change, MFA) -> rows w/ IP/UA/status | Integration | Critical | AC-4, FR-3/7 | Happy / negative / security |
| TC-NTF-004-05 | PII read -> "{Entity}.ReadSensitive" naming accessed fields; values NOT stored | Security | Critical | AC-2, FR-4/7 | Happy / boundary / security |
| TC-NTF-004-06 | Data export (CSV/Excel/PDF) -> audit row w/ params + row count | Integration | High | FR-5, FR-7/8, AC-1 | Happy / boundary / security |
| TC-NTF-004-07 | Enrichment -> row carries IP, user agent, trace id, session tenant_id; spoof ignored | Integration | High | AC-1, FR-2/7/8 | Happy / negative / security |
| TC-NTF-004-08 | Append-only -> app cannot UPDATE/DELETE an audit row (DB-role grant CONDITIONAL/deferred) | Security | Critical | AC-1, FR-6, BR-1 | Negative / security |
| TC-NTF-004-09 | Performance -> payroll 500 emp: <=50ms/save overhead P95, non-blocking, no rows dropped | Performance | High | AC-1, NFR-1/5, NFR-3 | Boundary / performance |
| TC-NTF-004-10 | GDPR RTBF -> PII replaced with "REDACTED-{id}", audit structure preserved + self-audited | Security | High | BR-6, BR-1, AC-1 | Happy / boundary / security |
| TC-NTF-004-11 | System-level actions audited separately; tenant admin cannot see them (single-table CONDITIONAL) | Security | High | AC-5, BR-4 | Negative / security / isolation |
| TC-NTF-004-12 | Capture boundaries -> raw-SQL/Dapper bypass; BRIN time-range query; per-plan retention + purge | Integration | Medium | FR-1, NFR-3/4, BR-5 | Negative / boundary / performance |
| TC-NTF-ISO-013 | Tenant A admin sees ONLY Tenant A audit rows; Tenant B invisible | Security | Critical | AC-5, NFR-2 (EF) | Multi-tenant isolation |
| TC-NTF-ISO-014 | Cross-tenant audit-row ID access -> 404 (not 403); missing tenant context rejected | Security | Critical | AC-5, NFR-2, FR-8 | Multi-tenant isolation |
| TC-NTF-ISO-015 | EF filter blocks cross-tenant reads; interceptor stamps tenant_id on writes (RLS deferred) | Security | Critical | AC-5, NFR-2, FR-8 | Multi-tenant isolation |
| TC-NTF-ISO-016 | All capture paths (write/PII/auth/export/RTBF) stamp correct tenant_id; no concurrent bleed | Security | High | AC-5, AC-2/4, NFR-2/5, FR-3/4/5/8, BR-6 | Multi-tenant isolation |

### Acceptance-Criteria Coverage (US-NTF-004)

| AC | Covered By |
|----|-----------|
| AC-1 (auto audit on save: timestamp/actor/action/resource/before/after/IP/UA/trace; tenant_id from session) | TC-NTF-004-01, -02, -07, -08, -09, -10 |
| AC-2 (PII read -> "ReadSensitive" naming accessed fields + accessor + trace id) | TC-NTF-004-05, TC-NTF-ISO-016 |
| AC-3 (soft-delete -> "{Entity}.Delete" before is_deleted:false / after is_deleted:true) | TC-NTF-004-03 |
| AC-4 (auth event -> auth-specific action, actor, IP, UA, success/failure status) | TC-NTF-004-04, TC-NTF-ISO-016 |
| AC-5 (Tenant A admin sees only Tenant A audit rows; Tenant B invisible) | TC-NTF-004-11, TC-NTF-ISO-013, -014, -015, -016 |

### FR / NFR / BR Coverage (US-NTF-004)

| Requirement | Covered By |
|-------------|-----------|
| FR-1 (auto-capture INSERT/UPDATE/DELETE via SaveChangesInterceptor) | TC-NTF-004-01, -02, -03, -12 |
| FR-2 (record fields: actor/action/resource/before/after/IP/UA/trace/tenant) | TC-NTF-004-01, -02, -07 |
| FR-3 (audit auth events: login/logout/password/MFA/refresh/lockout) | TC-NTF-004-04, TC-NTF-ISO-016 |
| FR-4 (audit PII reads: bank account, national ID, salary) | TC-NTF-004-05, TC-NTF-ISO-016 |
| FR-5 (audit data exports incl. params + row count) | TC-NTF-004-06, TC-NTF-ISO-016 |
| FR-6 (audit_log append-only; app DB role lacks UPDATE/DELETE) | TC-NTF-004-08 (DB-role grant CONDITIONAL/deferred; app-layer immutability asserted today) |
| FR-7 (enrich w/ IP, user agent, trace id) | TC-NTF-004-04, -05, -06, -07 |
| FR-8 (tenant_id from authenticated session on every record) | TC-NTF-004-07, TC-NTF-ISO-014, -015, -016 |
| FR-9 (streaming export to ELK/Splunk) | Deferred/Phase-2 -- noted TC-NTF-004-12 step 6 (flag to caller; not separately tested) |
| NFR-1 (audit overhead <= 50ms/save P95) | TC-NTF-004-09 |
| NFR-2 (audit isolated by tenant; RLS deferred -> EF filters) | TC-NTF-004-11, TC-NTF-ISO-013, -014, -015, -016 |
| NFR-3 (BRIN index on timestamp for time-range queries) | TC-NTF-004-09, -12 |
| NFR-4 (configurable retention: 90d/365d/7y per plan) | TC-NTF-004-12 |
| NFR-5 (high-throughput bulk without blocking the business transaction) | TC-NTF-004-09, TC-NTF-ISO-016 |
| NFR-6 (partition by month/tenant-range, Phase 2) | Deferred/Phase-2 -- noted TC-NTF-004-12 step 6 (flag to caller) |
| BR-1 (audit records immutable; cannot be modified/deleted via the app) | TC-NTF-004-08, -10 |
| BR-2 (before null for INSERT; after null for DELETE) | TC-NTF-004-01, -03 |
| BR-3 (only changed fields captured in before/after) | TC-NTF-004-02 |
| BR-4 (system-level actions in separate system_audit_log, System-Admin-only) | TC-NTF-004-11 (single-table discriminator CONDITIONAL; flag to caller) |
| BR-5 (retention purge via Hangfire; archive to cold storage first) | TC-NTF-004-12 |
| BR-6 (GDPR RTBF anonymizes PII -> "REDACTED-{id}", preserves structure) | TC-NTF-004-10, TC-NTF-ISO-016 |

---

## US-NTF-005 -- Audit Log Viewer with Filters for Admins

> US-NTF-005 (Audit Log Viewer with Filters) adds the admin-facing audit log UI. The CORE viewer
> (paginated list, basic filters, before/after detail diff, export, tenant isolation, masking,
> read-only/authorization, immutability) was already built and tested under **US-ADM-008**
> (`test-cases/admin-console/TC-ADM-008-01..21`). To avoid duplication, this story's TCs focus on the
> US-NTF-005 **DELTAS** and **re-affirm** the headline ACs + isolation, referencing US-ADM-008 for the
> base behavior. It adds 16 test cases: 12 functional/integration/security/performance/accessibility
> (TC-NTF-005-01..12) + 4 multi-tenant isolation continuing the module-wide running ISO counter
> (TC-NTF-ISO-017..020, from US-NTF-004's 016). Functional suffix counter resets per story
> (TC-NTF-005-XX); the ISO counter is shared/running. All 5 acceptance criteria of US-NTF-005 are covered.
>
> NEW vs US-ADM-008 (the deltas exercised here): (a) meta-audit on view -- viewing the list writes an
> "AuditLog.View" record (FR-9/BR-5, TC-NTF-005-09); (b) multi-select action & resource-type filters
> with OR-within-group / AND-across-groups semantics + "Select All" (FR-2, TC-NTF-005-03); (c) actor
> autocomplete type-ahead by name/email, tenant-scoped (FR-2, TC-NTF-005-04, TC-NTF-ISO-018); (d)
> keyword search matching content INSIDE before/after JSONB (FR-2, TC-NTF-005-05); (e) URL-based,
> bookmarkable/shareable filter state (FR-3, TC-NTF-005-02, TC-NTF-ISO-020).
>
> PLATFORM ACCURACY / DEFERRED (carried from the US-NTF-001 / US-ADM-008 family): (1) AC-5 / NFR-3
> name PostgreSQL RLS as the isolation layer; this platform isolates via **EF Core global query filters
> (read) + the audit/Tenant interceptors (write stamping)**, NOT Postgres RLS -- RLS is deferred
> defense-in-depth. ISO tests (TC-NTF-ISO-017..020) assert the EF mechanism in force today; the
> "raw SQL without app.current_tenant_id -> zero rows" RLS expectation is CONDITIONAL/deferred
> (TC-NTF-ISO-017 step 7, TC-NTF-ISO-020 step 5); cross-tenant audit-row ID access asserts **404,
> not 403** (TC-NTF-ISO-019). (2) AC-4 / FR-5 / NFR-6 async export (Hangfire job + in-app notification
> + 15-min signed download URL) is **DEFERRED** -- synchronous, filter-honoring CSV/JSON-Lines export
> is the path in force today (TC-NTF-005-07; async path = US-ADM-008 TC-ADM-008-19 [DEFERRED]).
> (3) FR-6 **keyset/cursor pagination** is **DEFERRED** -- offset pagination at 50/page is in force
> today; "next" works and page_size is capped at 100 (TC-NTF-005-10). (4) NFR-1 (<2s first page) /
> NFR-2 (<=3s filtered, up to 10M rows) / NFR-7 (BRIN on timestamp + GIN on JSONB) need a
> perf-representative environment -- on a dev box record indicative numbers and do NOT relax the SLAs
> (TC-NTF-005-12; large-dataset first page = US-ADM-008 TC-ADM-008-21 [DEFERRED]).
>
> STORY MISMATCH / SCOPE NOTES worth flagging to the caller: (a) AC-5 / NFR-3 name Postgres RLS as an
> ACTIVE isolation layer -- only the app (ITenantContext) + EF (query filter / interceptor) layers
> exist today; reword RLS as future hardening (consistent with prior modules). (b) AC-4 describes the
> async Hangfire export + signed URL as the export mechanism -- only synchronous export is in force
> today; the async path is deferred. (c) FR-6 mandates keyset pagination "not OFFSET" -- offset is in
> force today; keyset is deferred. (d) AC-3 trace-id "link to the observability platform" assumes an
> observability target/URL is configured; the link rendering is asserted (TC-NTF-005-06), the external
> platform integration is environment-dependent.

### Coverage by Test Case (US-NTF-005)

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category |
|-----------|-------|------|----------|--------------------|----------|
| TC-NTF-005-01 | Paginated table newest-first, required columns, first page < 2s (re-affirm) | E2E | Critical | AC-1, FR-1/7, NFR-1 | Happy / boundary / performance |
| TC-NTF-005-02 | Combined date+action+actor AND semantics; result count; URL bookmarkable | E2E | Critical | AC-2, FR-2/3/7 | Happy / boundary |
| TC-NTF-005-03 | Multi-select action & resource type -- OR within group, AND across groups + Select All | Functional | High | AC-2, FR-2 | Happy / boundary |
| TC-NTF-005-04 | Actor autocomplete type-ahead returns tenant-scoped name/email matches | Integration | High | AC-2, FR-2 | Happy / negative / security |
| TC-NTF-005-05 | Keyword search matches content inside before/after JSONB | Integration | High | AC-2, FR-2, NFR-7 | Happy / negative / boundary |
| TC-NTF-005-06 | Detail panel diff highlights changed fields; full UA + trace-id observability link | Functional | High | AC-3, FR-4 | Happy path |
| TC-NTF-005-07 | Export honors filters (CSV/JSON Lines) -- sync today; async+signed URL DEFERRED | Integration | High | AC-4, FR-5, NFR-6, BR-4 | Happy / boundary / security |
| TC-NTF-005-08 | Permission: only Audit.View read; Auditor cannot export; no write/delete via UI | Security | Critical | AC-1, AC-4, FR-8/5, BR-1/2/6 | Negative / security |
| TC-NTF-005-09 | Meta-audit -- viewing the list creates an "AuditLog.View" record | Integration | Critical | FR-9, BR-5 (supports AC-1) | Happy / boundary / security |
| TC-NTF-005-10 | Pagination 50/page + next works; keyset (FR-6) DEFERRED (offset today) | Functional | High | AC-1, FR-6 | Happy / boundary / performance |
| TC-NTF-005-11 | Table+filters keyboard-navigable + ARIA; responsive card list at 360px | Accessibility | Medium | NFR-5, NFR-4, FR-1/2 | Accessibility / cross-browser |
| TC-NTF-005-12 | First page <2s + filtered <=3s P95 on large dataset (multi-select + JSONB keyword) | Performance | High | AC-1, AC-2, NFR-1/2/7 | Boundary / performance |
| TC-NTF-ISO-017 | Tenant A admin sees ONLY Tenant A rows across all filters/pages; meta-audit scoped | Security | Critical | AC-5, NFR-3 (EF), BR-5 | Multi-tenant isolation |
| TC-NTF-ISO-018 | Actor autocomplete + filter tenant-scoped; cross-tenant actor_user_id -> zero rows | Security | Critical | AC-5, AC-2, FR-2/3, NFR-3 | Multi-tenant isolation |
| TC-NTF-ISO-019 | Cross-tenant audit-row ID access -> 404 (not 403); missing tenant context rejected | Security | Critical | AC-5, AC-3, FR-4/5, NFR-3 | Multi-tenant isolation |
| TC-NTF-ISO-020 | EF filter constrains all viewer query paths; URL filter state cannot widen scope | Security | High | AC-5, FR-2/3/4/5/9, NFR-3 | Multi-tenant isolation |

### Acceptance-Criteria Coverage (US-NTF-005)

| AC | Covered By |
|----|-----------|
| AC-1 (paginated table, newest first, required columns, first page < 2s, only authorized admins) | TC-NTF-005-01, -08, -10, -12 |
| AC-2 (combined filters refresh; URL bookmarkable; result count shown) | TC-NTF-005-02, -03, -04, -05, -12 |
| AC-3 (detail panel: before/after diff highlighted, full UA, trace id + observability link) | TC-NTF-005-06, TC-NTF-ISO-019 |
| AC-4 (export filtered records; async Hangfire + signed URL DEFERRED -> sync export today) | TC-NTF-005-07, -08 |
| AC-5 (Tenant A admin sees only Tenant A rows; Tenant B invisible; RLS deferred -> EF filter) | TC-NTF-ISO-017, -018, -019, -020 |

### FR / NFR / BR Coverage (US-NTF-005)

| Requirement | Covered By |
|-------------|-----------|
| FR-1 (paginated/sortable table with required columns) | TC-NTF-005-01, -11 (base: US-ADM-008 TC-ADM-008-01) |
| FR-2 (date/actor-autocomplete/action-multi/resource-multi/keyword filters) | TC-NTF-005-02, -03, -04, -05, TC-NTF-ISO-018 |
| FR-3 (URL-based, bookmarkable/shareable filter state) | TC-NTF-005-02, -03, -04, TC-NTF-ISO-020 |
| FR-4 (detail view with before/after visual diff) | TC-NTF-005-06 (base: US-ADM-008 TC-ADM-008-09/-16) |
| FR-5 (export CSV / JSON Lines; async via Hangfire) | TC-NTF-005-07 (async DEFERRED -- US-ADM-008 TC-ADM-008-19) |
| FR-6 (50/page server-side; keyset not OFFSET) | TC-NTF-005-10 (keyset DEFERRED -- offset in force; base: US-ADM-008 TC-ADM-008-02) |
| FR-7 (summary bar: total count + retention period) | TC-NTF-005-01, -02 (base: US-ADM-008 TC-ADM-008-15) |
| FR-8 (restrict to Audit.Read/Audit.View: Tenant Admin/Owner/Auditor) | TC-NTF-005-08 (base: US-ADM-008 TC-ADM-008-12/-13) |
| FR-9 (meta-audit: log audit-log access itself) | TC-NTF-005-09, TC-NTF-ISO-020 |
| NFR-1 (first page <= 2s P95) | TC-NTF-005-01, -12 |
| NFR-2 (filtered <= 3s P95 up to 10M rows/tenant) | TC-NTF-005-05, -12 |
| NFR-3 (tenant isolation; RLS deferred -> EF filters) | TC-NTF-ISO-017, -018, -019, -020 |
| NFR-4 (responsive 360px-4K, card list on mobile) | TC-NTF-005-11 |
| NFR-5 (WCAG 2.1 AA: keyboard nav, screen-reader data cells) | TC-NTF-005-11 |
| NFR-6 (export signed URLs, 15-min expiry) | TC-NTF-005-07 (DEFERRED with the async export path) |
| NFR-7 (BRIN on timestamp + GIN on JSONB leveraged) | TC-NTF-005-05, -12 (infra-conditional) |
| BR-1 (only Audit.Read can access) | TC-NTF-005-08 |
| BR-2 (read-only viewer; no modify/delete via UI) | TC-NTF-005-06, -08 (base: US-ADM-008 TC-ADM-008-17) |
| BR-3 (respect retention window) | Base coverage: US-ADM-008 TC-ADM-008-14/-15 (retention purge + view-only retention) |
| BR-4 (export limited to 100,000 records) | TC-NTF-005-07 |
| BR-5 (viewing creates "AuditLog.View" meta-audit record) | TC-NTF-005-09, TC-NTF-ISO-017 |
| BR-6 (Auditor role read-only; cannot perform writes) | TC-NTF-005-08 (base: US-ADM-008 TC-ADM-008-12) |
