---
name: onboarding-tc-conventions
description: Onboarding/Offboarding module test-case ID scheme, matrix structure, and platform deferrals (first ONB story established them)
metadata:
  type: project
---

Onboarding/Offboarding module TC conventions.

DISK-STATE CONFLICT (verified 2026-06-17): despite this note previously saying US-ONB-001 established the conventions and used TC-ONB-ISO-001..004, **no `test-cases/onboarding/` directory existed on disk** when US-ONB-002 was worked. US-ONB-001 test cases were never persisted/merged (or were lost). So **US-ONB-002 is in fact the FIRST Onboarding story with test cases on disk**, and it STARTED the ISO counter at TC-ONB-ISO-001 (not 005). If/when US-ONB-001 test cases are authored, they precede these and the ISO counter must be reconciled/renumbered. Flagged to caller in the US-ONB-002 TRACEABILITY note + TEST-MATRIX header.

ID scheme — ADOPTS the Recruitment/Payroll/Admin-Console scheme: functional `TC-ONB-{NNN}-XX` (per-story suffix, NOT a global running counter) + a separate running isolation counter `TC-ONB-ISO-NNN`. US-ONB-002 = TC-ONB-002-01..12 (12 functional/security/perf/a11y) + TC-ONB-ISO-001..004 (4 mandatory isolation).

3-matrix update rule (same as all prior modules): per-TC `.md` files in `test-cases/onboarding/` + module `TEST-MATRIX.md` + a new section appended to root `test-cases/TRACEABILITY-MATRIX.md` (Forward + Backward + AC-coverage tables + a trailing *Note(...)* paragraph). Root matrix is ~4.5k lines / >256KB — read with offset/limit, append via Edit on the last line.

Platform deferrals written as CONDITIONAL (never as gaps, never fabricated):
- AC-5/NFR-2 name PostgreSQL RLS — platform uses EF Core global query filters (read) + `TenantInterceptor` (write stamping); RLS deferred (same family as Auth/Leave/Payroll/Admin). ISO tests assert EF mechanism; "raw SQL without app.current_tenant_id -> 0 rows" is deferred (ISO-003 step 4).
- Cross-tenant ID injection asserts 404 NOT 403 (existence not disclosed) — ISO-002.
- Cache-key scoping (ISO-004): assert tenant-keyed, or flag `onboarding:templates:{tenant_id}` as target if no cache wired.
- Notification DELIVERY (US-ONB-002 AC-2/AC-5/FR-4/NFR-3): real SignalR (US-NTF-001) + email (US-NTF-002) deferred — assert outbox/notification-INTENT rows (same txn) + Hangfire dispatch ENQUEUE, NOT actual send.
- NFR-1: US-ONB-002 = assignment API <=1000ms P95 (needs perf-representative env). (US-ONB-001 create API was <=500ms — per-story.)

PROGRESS (on disk 2026-06-17): US-ONB-001..006 all present — **Onboarding module COMPLETE**. ISO counter now at **023** (002=ISO-001..004, 003 through ISO-011, 004=ISO-012..015, 005=ISO-016..019, 006=ISO-020..023). Module total = **95 TCs, 31/31 AC** (5+5+5+5+6+5). If a future ONB story appears, continue TC-ONB-{NNN}-XX + TC-ONB-ISO-024+.
US-ONB-006 (Exit Interview Recording): 5 ACs (AC-5 is the isolation AC). Self-service HR-notify (AC-3/FR-8) asserted as outbox INTENT + Hangfire enqueue, real SignalR/email deferred to US-NTF-001/002. NFR-1 form load <=500ms P95; NFR-3 analytics render <=2s for 1000 interviews (per-story). BR-1 one interview per offboarding; BR-2 immutable -> edit creates NEW version (original preserved); BR-3 self-service before LWD/while account active; FR-5 anonymization gated by `ExitInterview.ViewDetail` perm + NFR-6 PII-access audit flag. Analytics cache (if wired) = `onboarding:exit-analytics:{tenant_id}` (ISO-023). SCOPE/MISMATCH flagged: BR-5 retention has no endpoint here; BR-6 template config = Admin Console master data (assumed); FR-5 depends on `ExitInterview.ViewDetail` existing in RBAC catalogue; BR-2 assumes a version-history model (overwrite-in-place would be a deviation).
US-ONB-004 (Asset Issuance): NFR-1 = 600ms P95 (per-story). Acknowledgment upload key = `{tenantId}/onboarding/{employeeId}/assets/{assetId}/{filename}`; malware scan = SEAM/EICAR, live ClamAV deferred (same as ONB-003-05). BR-4 (return/disposal) + BR-5 (soft delete) = lifecycle transitions with NO endpoint in the create/issue story — only exercised as non-issuable INPUTS to the FR-3 "available" gate; flag the missing transitions to a later offboarding/asset-lifecycle story. BR-2 asset-type config = Admin Console master data (assumed via preconditions).
US-ONB-005 (Offboarding/Exit Clearance): FIRST story with **6 ACs** (others had 5). NFR-1 = 1000ms P95 initiation; NFR-3 = deactivation+revocation <=30s. KEY DEFERRAL: FR-7 session revocation specifies SignalR disconnect + **Redis JWT denylist** — denylist NOT wired; assert revocation via ACCOUNT DEACTIVATION (old JWT fails active-account check -> 401), denylist hit CONDITIONAL/deferred (TC-ONB-005-06). F&F settlement = Payroll's job (BR-4); offboarding only TRIGGERS the notification (assert tenant-stamped trigger, not the calc). The asset-return TRANSITION that US-ONB-004 deferred (BR-4 returned->available) IS exercised here as the real endpoint (TC-ONB-005-03). BR-1 status gate: initiate only for resignation_accepted/terminated/contract_ended. Offboarding lookup cache (if wired) = `onboarding:offboarding:{tenant_id}:{employee_id}` (ISO-019).

**Why:** Keeps Onboarding consistent with the established per-suffix scheme + the platform's real isolation story, so the suite stays honest and traceable.
**How to apply:** For the next ONB story, continue TC-ONB-{NNN}-XX and CONTINUE the running TC-ONB-ISO-NNN counter from 015 (next = ISO-016). Re-flag the RLS mismatch + notification-delivery deferral.

STORY MISMATCHES flagged to caller on US-ONB-002: (1) NFR-2 RLS claim — reword as future hardening (EF filters + TenantInterceptor in force). (2) FR-3 "Manager" -> employee `reporting_manager_id`: if unset, the Manager-role task has no resolvable owner — TC-ONB-002-05 step 6 probes this; recommend a clear unresolved-party warning if story leaves it undefined. (3) Notification delivery deferred to US-NTF-001/002 (intent-only assertions).
STORY MISMATCHES previously noted for US-ONB-001 (not on disk): AC-5/NFR-2 RLS; BR-5 soft-delete has no endpoint in create-only story; FR-3 FK validation depends on US-CHR-004/005.

Related: [[payroll-tc-conventions]] [[admin-console-tc-conventions]] [[recruitment-tc-conventions]]
