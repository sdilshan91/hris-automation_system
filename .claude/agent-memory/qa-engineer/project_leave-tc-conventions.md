---
name: project-leave-tc-conventions
description: Numbering and matrix conventions for Leave Management IEEE 829 test cases (TC-LV-* sequence, ISO sequence, three matrices to update)
metadata:
  type: project
---

Leave Management test cases (`test-cases/leave-management/`) follow these conventions, established by US-LV-001/002 and continued in US-LV-003:

- **Two parallel ID sequences:** functional/security/perf/a11y TCs use a single running `TC-LV-NNN` counter across all stories (do NOT restart per story); dedicated multi-tenant isolation TCs use a separate `TC-LV-ISO-NNN` counter. Each story gets exactly 4 ISO TCs (cross-tenant data visibility, no-tenant-context rejection, EF query-filter/RLS block, tenant-scoped cache keys/paths).
- **Three artifacts to update per story:** the per-TC `.md` files, `test-cases/leave-management/TEST-MATRIX.md`, and the root `test-cases/TRACEABILITY-MATRIX.md` (forward + backward tables, a per-story "Detailed Requirements Traceability" table, a per-story "Coverage Summary", and the bottom Cross-Module totals + closing note).
- **Tenant isolation enforcement** in this codebase is EF Core global query filters + TenantInterceptor, NOT Postgres RLS. The story text says "RLS" but ISO TCs should describe the EF-filter mechanism (per vault `modules/leave-management.md`).

**Why:** Reviewers and the orchestrator rely on consistent IDs and the three-matrix structure for traceability; breaking the sequence or skipping a matrix breaks backward links.

**How to apply:** Before writing a new Leave story's TCs, glob the existing `TC-LV-*` files to find the highest functional and highest ISO number, then continue both. See [[project-leave-deferred-infra]].
