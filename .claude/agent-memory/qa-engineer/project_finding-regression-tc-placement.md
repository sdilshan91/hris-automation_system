---
name: finding-regression-tc-placement
description: Where post-hoc TCs authored to close a finding's traceability gap go — root matrix "Patched-story regression coverage" table, not the module TEST-MATRIX
metadata:
  type: project
---

A TC authored **after** a finding was fixed (to close the Critical-Rule-#4 traceability gap on a
BUG/ISSUE being closed) is filed differently from a story-build TC:

- **File:** module folder, next free id in that module's own scheme (core-hr = running `TC-CHR-{NNN}`;
  admin-console = per-story suffix `TC-ADM-{NNN}-XX`). See [[admin-console-tc-conventions]].
- **Root `docs/QA/TRACEABILITY-MATRIX.md`:** append a row to the dedicated
  **"### Patched-story regression coverage (bind to finding IDs)"** table (7 columns — the extra one is
  `Finding`). That table sits at the end of the Core-HR section, just before `## Platform Module`.
- **Module `TEST-MATRIX.md`: do NOT update.** Precedent — TC-CHR-330/335/336/337/339, TC-CHR-001-101,
  TC-CHR-005-48 all have root-matrix rows and no module-matrix row (core-hr TEST-MATRIX stops at TC-CHR-324).
- **`status:`** is `automated` (never `pass`) when the arms were already green before the doc existed;
  add `automated:` = the date the doc bound them. Write steps as an **"Automated by"** column naming the
  exact test method, and mark unautomated rows "Code-verified only" with an explicit coverage note.

**Why:** these TCs document evidence that already exists; inventing a manual execution record or a
module-matrix coverage line would be the fabrication the ledger rules exist to prevent.

**How to apply:** whenever the orchestrator asks for a TC to make a closing finding traceable.

Anchors used 2026-09-02: BUG-307 → `TC-ADM-009-19` (US-ADM-009 AC-3/AC-5/FR-4/BR-3 — plan-limit runtime
resolution is the best-fitting story; US-ADM-012 is a stub epic, cite it only as secondary).
ISSUE-364 → `TC-CHR-340` (US-CHR-004; the manager/employee-count columns live in **§8 UI/UX Notes + FR-8**,
not in any AC — AC-5 only owns the *display* half, the deactivate block is server-side).
