---
name: project-attendance-tc-conventions
description: Numbering and matrix conventions for the Attendance module IEEE 829 test cases (TC-ATT-* + TC-ATT-ISO-* dual sequences), established by US-ATT-001
metadata:
  type: project
---

Attendance test cases (`test-cases/attendance/`) follow the same conventions as Leave Management (see [[project-leave-tc-conventions]]), established by US-ATT-001 (first story; created the dir + TEST-MATRIX):

- **Two parallel ID sequences:** functional/security/perf/a11y TCs use a single running `TC-ATT-NNN` counter across all stories (do NOT restart per story); dedicated multi-tenant isolation TCs use a separate `TC-ATT-ISO-NNN` counter. Each story gets exactly 4 ISO TCs (cross-tenant read visibility, no/invalid-tenant-context rejection, cross-tenant write/data-layer block, tenant-scoped cache keys). US-ATT-001 used TC-ATT-001..012 + TC-ATT-ISO-001..004.
- **Three artifacts per story:** per-TC `.md` files, `test-cases/attendance/TEST-MATRIX.md`, and the root `test-cases/TRACEABILITY-MATRIX.md` (forward + backward tables, per-story Detailed Requirements Traceability, per-story Coverage Summary, the Cross-Module table totals, and the closing note paragraph).
- **Tenant isolation mechanism:** this codebase enforces isolation via EF Core global query filters + TenantInterceptor, NOT Postgres RLS. US-ATT-001 NFR-2/S10 say "RLS" — ISO TCs describe the EF mechanism and note RLS as an extension point. Same caveat as leave-management per vault.
- **Redis cache (FR-6):** not assumed wired. Write cache-dependent steps as CONDITIONAL with a DB-fallback verification path (mirrors leave-management deferred-Redis handling, see [[project-leave-deferred-infra]]).

**Why:** Reviewers and the orchestrator rely on consistent IDs and the three-matrix structure for backward traceability; breaking the sequence or skipping a matrix breaks links.

**How to apply:** Before writing the next Attendance story's TCs, glob existing `TC-ATT-*` files for the highest functional and highest ISO numbers and continue both sequences. BR-6 selfie-photo (require_photo) was reported to the caller as having no AC under US-ATT-001 — revisit if a photo-capture story appears.

**ISO-TC reuse for derived operations (US-ATT-002 precedent):** A story that operates on the SAME table US-ATT-001 already covered (clock-out mutates the same `attendance_log`) does NOT need 4 fresh ISO TCs. US-ATT-002 added only ONE new ISO TC — TC-ATT-ISO-005 (Tenant A employee cannot clock out Tenant B's open record, the distinct write mutation) — and explicitly REUSED TC-ATT-ISO-001..004 for table-level read/missing-context/cache isolation, referencing them in both the TC body and the matrices. The mandatory "every module gets isolation tests" gate is satisfied by the existing 4 plus the operation-specific one. US-ATT-002 = TC-ATT-013..024 + TC-ATT-ISO-005. The auto-clock-out Hangfire job TC (TC-ATT-021) also carries a tenant-scoping step, so the job's isolation is covered there rather than in a separate ISO TC.
