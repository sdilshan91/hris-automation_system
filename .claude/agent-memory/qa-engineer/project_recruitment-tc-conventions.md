---
name: project-recruitment-tc-conventions
description: Numbering and matrix conventions for the Recruitment module IEEE 829 test cases (TC-REC-001-XX per-story + TC-REC-ISO-NNN sequences), established by US-REC-001
metadata:
  type: project
---

Recruitment test cases (`test-cases/recruitment/`) were established by US-REC-001 (first story; created the dir + TEST-MATRIX + the root Recruitment section). They follow the same three-matrix + deferred-as-conditional discipline as Attendance/Leave (see [[project-attendance-tc-conventions]], [[project-leave-tc-conventions]]) but with a DIFFERENT functional ID scheme:

- **Functional ID scheme is per-story-suffixed, NOT a single running counter.** US-REC-001 used `TC-REC-001-01 .. TC-REC-001-12` (zero-padded two-digit suffix scoped to the story). This DIFFERS from Attendance/Leave's single running `TC-ATT-NNN`/`TC-LV-NNN` counter. The next Recruitment story should use `TC-REC-002-01..NN` (do NOT continue a global counter). Reason: cleaner per-story grouping for a fresh module; chosen at module bootstrap and accepted.
- **ISO ID scheme is a separate running counter:** `TC-REC-ISO-NNN` (NOT story-suffixed). US-REC-001 used TC-REC-ISO-001..004 (the standard 4: cross-tenant read, no/invalid/mismatched tenant-context rejection, cross-tenant write block + body-injected tenant_id, tenant-scoped caches/slugs/public-URLs). Continue this counter across stories; reuse ISO-001..004 for same-table operations (Attendance precedent) and add ONE new ISO TC only for a genuinely distinct mutation/table.
- **Three artifacts per story:** per-TC `.md` files, `test-cases/recruitment/TEST-MATRIX.md`, and the root `test-cases/TRACEABILITY-MATRIX.md` (forward + backward tables, per-story Detailed Requirements Traceability, per-story Coverage Summary, the Cross-Module table totals row, and the closing-note paragraph).
- **Tenant isolation mechanism:** EF Core global query filters + TenantInterceptor, NOT Postgres RLS. US-REC-001 AC-4/NFR-2 say "PostgreSQL RLS policy on the `vacancy` table" -- ISO TCs describe the EF mechanism and note RLS as an extension point. Same caveat as Attendance/Leave.
- **Recurring deferrals written CONDITIONAL (not gaps):** vacancy list Redis cache (NFR-1, key `tenant:{tenantId}:vacancies:...`); public careers page toggle on tenant module config S35.2.9 (FR-4/BR-5); audit assertions on the Audit logging module (FR-7); FR-6 bulk status changes deferred to a later REC story; applicant lifecycle (AC-5/BR-3) owned by later REC stories.

**Why:** Reviewers/orchestrator rely on consistent IDs + the three-matrix structure for backward traceability; the per-story suffix scheme is the Recruitment-specific deviation that must be carried forward.

**How to apply:** Before writing the next Recruitment story's TCs, use `TC-REC-{NNN}-XX` for that story's functional/security/perf/a11y TCs and continue the separate `TC-REC-ISO-NNN` counter (glob existing `TC-REC-ISO-*` for the highest). No vault `docs/vault/modules/recruitment.md` note existed at bootstrap.

MODULE COMPLETE as of US-REC-010 (2026-06-15): all 10 stories done. Final counts: 153 TCs (134 functional/integration/security/perf/a11y + 19 dedicated ISO), 48/48 ACs, highest ISO = TC-REC-ISO-019. US-REC-010 (convert applicant->employee) = TC-REC-010-01..13 + TC-REC-ISO-019. Notable: BR-3 subscription limit is testable today against `Tenant.MaxEmployees` (nullable int on the Tenant entity, null=unlimited; placeholder until a real Subscription/Plan entity) -- written as a real test, NOT deferred. Welcome email (FR-9/NFR-5) + auto-close notifications (FR-7/BR-5) + onboarding trigger (FR-8) written CONDITIONAL on S25/Hangfire/Onboarding module. No new a11y TC for REC-010 -- the convert form reuses the Core HR employee-creation form a11y surface. Cross-Module TOTAL after REC: 51 stories / 1046 TCs / 262 AC / 171 multi-tenant.
