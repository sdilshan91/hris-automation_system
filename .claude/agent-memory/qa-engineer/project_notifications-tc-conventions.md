---
name: notifications-tc-conventions
description: TC ID scheme, isolation counter, and platform-deferral rules for the Notifications & Audit module test cases
metadata:
  type: project
---

Notifications & Audit module test-case conventions (FIRST story US-NTF-001 established the module).

ID scheme (ADOPTS the Recruitment/Payroll/Admin Console/Onboarding family): per-story functional
suffix `TC-NTF-{NNN}-XX` (resets per story) + a module-wide RUNNING isolation counter
`TC-NTF-ISO-NNN` starting at 001. US-NTF-001 = TC-NTF-001-01..12 + TC-NTF-ISO-001..004;
US-NTF-002 = TC-NTF-002-01..12 + TC-NTF-ISO-005..008; US-NTF-003 = TC-NTF-003-01..12 +
TC-NTF-ISO-009..012; US-NTF-004 = TC-NTF-004-01..12 + TC-NTF-ISO-013..016; US-NTF-005 = TC-NTF-005-01..12 +
TC-NTF-ISO-017..020. **ISO counter is now at 020 — next NTF story continues at TC-NTF-ISO-021.**

US-NTF-005 (Audit Log Viewer with Filters) is a DELTA story: the CORE viewer (list, basic filters,
detail diff, sync export, masking, authz/read-only, immutability, retention, isolation) was already
built+tested under **US-ADM-008** (test-cases/admin-console/TC-ADM-008-01..21). To avoid duplication,
NTF-005 TCs focus on the deltas + re-affirm headline ACs/isolation, REFERENCING (not duplicating)
US-ADM-008. Deltas: meta-audit on view "AuditLog.View" (FR-9/BR-5, -09); multi-select action/resource
filters OR-within/AND-across + Select All (FR-2, -03); actor autocomplete tenant-scoped (FR-2, -04 +
ISO-018); keyword search inside before/after JSONB (FR-2, -05); URL-based bookmarkable filter state
(FR-3, -02 + ISO-020). NTF-005 DEFERRALS (conditional, not gaps): async export Hangfire+15min signed
URL (AC-4/FR-5/NFR-6 — sync export in force today, async = US-ADM-008 TC-ADM-008-19); keyset/cursor
pagination (FR-6 — offset in force, 50/page+next works, cap 100); RLS (AC-5/NFR-3 — EF filter in force,
404-not-403 on cross-tenant ID). NFR-1/2/7 perf need representative env (dev = indicative, never relax).

US-NTF-004 (Audit Trail for All Data Changes) is BACKEND/INFRA — automatic EF SaveChangesInterceptor
capture; NO new user-facing creation UI (Audit Log Viewer is US-NTF-005, partly built in US-ADM-008),
so TCs target capture/enrichment/immutability/isolation, not UI. EXTRA platform deferrals beyond the
NTF family RLS rule: (a) the INSERT-only DB-role grant (FR-6 append-only) is NOT provisioned — assert
app-layer immutability today (no mutating endpoint/code path), mark the DB-role UPDATE/DELETE-denied
grant CONDITIONAL/deferred. (b) BR-4 system_audit_log separate table may be a single-table
system/tenant discriminator today — assert tenant query still excludes system rows, dedicated table
deferred. (c) FR-9 ELK/Splunk streaming + NFR-6 partitioning are Phase-2/deferred. (d) S10
constraint: raw SQL/Dapper writes bypass the interceptor (manual audit needed) — exercised in -12.
Cross-tenant audit-row ID access asserts 404 not 403 (ISO-014).

US-NTF-003 (Notification Preferences per User) scope notes: NFR-3 Redis preference cache
(`notif:prefs:{tenant_id}:{user_id}`, TTL 5min) is deferred — wrote cache TCs CONDITIONAL (assert
key+invalidation if wired, else always-fresh tenant-scoped DB lookup). Quiet Hours email queuing
(FR-9/BR-5) runs via outbox/Hangfire worker; in-app bypasses quiet hours. Flagged to caller: FR-4/AC-4
mandatory-category AUTHORING is Admin Console (S35.2.11), out of scope here — only consumed; SMS
(FR-3 Phase 2) out of scope; per-user endpoint operates on CURRENT user only (cross-user IDOR -> 404).

Always update three artifacts: each per-TC file + `test-cases/notifications/TEST-MATRIX.md` +
the root `test-cases/TRACEABILITY-MATRIX.md` (Notifications section). Root matrix uses CRLF and is
>256KB — append via a temp file + `cat >>`, not heredoc (apostrophes/backticks break heredoc here).

**Why:** keeps Notifications consistent with every prior module so coverage rollups and traceability
stay uniform. **How to apply:** reuse this exact scheme for US-NTF-002+ (continue ISO counter; reset
functional suffix per story).

Platform-deferral rules to carry forward (same family as [[admin-console-tc-conventions]],
[[onboarding-tc-conventions]]):
- NFR-2 names PostgreSQL RLS but platform isolates via EF Core global query filters (read) +
  TenantInterceptor (write stamping). RLS deferred. Assert EF mechanism today; "raw SQL w/o
  app.current_tenant_id -> zero rows" is CONDITIONAL; cross-tenant REST ID injection asserts
  404 NOT 403.
- Redis is a HARD dependency here (SignalR backplane FR-10, multi-instance fan-out, NFR-3 5k
  concurrency). Perf + reconnection TCs need a perf/multi-instance env; on dev box record
  indicative numbers, never relax the 2s NFR-1 SLA.
- Unread-count cache is conditional: assert key shape `notifications:unread:{tenant_id}:{user_id}`
  if wired, else the equivalent always-tenant-filtered computation.

STORY SCOPE NOTES flagged to caller for US-NTF-001 (out of scope for this real-time-delivery story):
BR-2 (archive >90d via Hangfire) + BR-3 (purge >1000/user) = retention/lifecycle, separate story;
BR-4 (system notifications via Notification Dispatcher, not direct SignalR) = producer-side, needs a
Dispatcher story; toggleable sound notification is UI/UX-notes-only (not an AC/FR), treated optional.
SignalR group naming `t:{tenantId}:user:{userId}` / `t:{tenantId}:role:{role}` must be server-derived
from JWT claims, never client-trusted (BR-5 rejects cross-tenant group names at the hub).
