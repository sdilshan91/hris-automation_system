---
name: overtime-weekly-progress-helper
description: US-ATT-006 weekly overtime bar uses a pure helper + a FE-default cap; no policy endpoint exists yet
metadata:
  type: project
---

The weekly-overtime progress bar (US-ATT-006 §8) is derived client-side, not from the API.

**Why:** the pinned overtime contract has no tenant-policy endpoint exposing the weekly cap
(BR-5 default 20h). So `WEEKLY_OVERTIME_CAP_MINUTES = 20*60` is a hardcoded FE display default
in attendance.models.ts, and `weeklyOvertimeMinutes(records, ref)` sums APPROVED+PENDING
minutes for the ISO week (Mon–Sun) containing `ref`, excluding REJECTED/UNAPPROVED and using
`approvedMinutes` when set else `overtimeMinutes`.

**How to apply:** if a tenant overtime-policy endpoint is ever added, replace the constant with
the real per-tenant cap rather than adding a second source of truth. The helper is unit-tested
indirectly via my-overtime.component.spec; keep it pure (no Date.now inside — pass `reference`).

Also: overtime employee detail was built as a dedicated `/attendance/overtime` list
(MyOvertimeComponent), NOT embedded in the clock-in daily card. The story allowed either; the
list kept the change surgical (no edit to clock-in.component). See [[right-drawer-form-pattern]]
for the sibling approval-hub interaction model that overtime-approvals mirrors.
