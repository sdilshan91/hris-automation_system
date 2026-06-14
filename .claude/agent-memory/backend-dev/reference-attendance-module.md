---
name: reference-attendance-module
description: Where attendance domain rules live and key US-ATT-001 scaffold decisions
metadata:
  type: reference
---

Attendance module domain rules, the enforcement/error-code contract, and deviations from the
story's aspirational tech (no RLS, no Redis) are documented in the shared vault at
`docs/vault/modules/attendance.md`. Read it before working any `US-ATT-*` story.

Key scaffold facts from US-ATT-001:
- Entities `AttendanceLog` + `AttendanceSettings` (both `BaseEntity`), one settings row per tenant
  created lazily with enforcement off.
- "One OPEN punch per employee" (clock_out IS NULL) is the duplicate rule, backed by a partial
  unique index `ix_attendance_log_open_unique` — NOT a calendar-day rule.
- Clock-in is gated by the existing `Attendance.CheckIn` permission (the story's
  `Attendance.Clock.Self` does not exist in `PermissionCatalog`).
- Related: [[feedback-integration-tests-inmemory]].
