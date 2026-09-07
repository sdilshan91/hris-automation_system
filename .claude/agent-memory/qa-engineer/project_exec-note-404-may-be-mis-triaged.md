---
name: project-exec-note-404-may-be-mis-triaged
description: Several blocked Performance TCs carry exec_notes blaming a 404 on missing seed data when the probed route does not exist at all — re-verify the route before trusting a blocked-TC diagnosis
metadata:
  type: project
---

A `blocked` verdict whose `exec_note` explains a 404 as "no seeded data" / "user not employee-linked"
may actually be a **route 404**. Verify the probed path against the live controllers before accepting
the diagnosis or re-running the harness.

**Why:** found while fixing ISSUE-100 (2026-09-07). Four Performance TCs record a 404 against a path
that exists under no prefix, and attribute it to data:
- `TC-PRF-001-11:9` — `GET /performance/goals/team` (k6): no such route; real = `cycles/{cycleId}/team-dashboard`.
- `TC-PRF-009-15:9` — `GET /performance/goal-progress/my-goals`: there is no `goal-progress` segment;
  real = `/api/v1/tenant/performance/my-goals` (the FE service even comments this).
- `TC-PRF-002-15:9` — `GET /performance/self-assessments/active`: no `active` route; real =
  `self-assessments/cycles/{cycleId}/me`.
- `TC-PRF-007-11:9` — `GET /performance/dashboard/overview`: path segment is real, only the `/tenant/`
  prefix is missing.
So a seeding effort undertaken on the strength of those notes would have been wasted, and the TCs
would still not measure anything.

**How to apply:** before re-running any blocked perf/a11y TC, resolve its target path against the
controller route table. Do NOT edit the exec_note (it is a run record) — file the correction as a
finding. Pairs with [[feedback-route-drift-is-not-a-prefix-fix]].
