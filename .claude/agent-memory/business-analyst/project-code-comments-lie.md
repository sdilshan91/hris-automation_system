---
name: project-code-comments-lie
description: In this repo, in-code comments routinely claim a field is deferred/null/empty on the line above a real computed value — never author a requirement from a comment.
metadata:
  type: project
---

When authoring IEEE 830 requirements **from shipped code** in this repo, trust the code and never the
comment beside it. Stale comments that describe a control as absent — while the next line implements
it — are a recurring pattern here, not a one-off.

**Why:** confirmed instances as of 2026-09-04 include `PlatformMonitoringService.cs:156-160`
("STILL NULL, deliberately" directly above a computed assignment), `MonitoringDtos.cs:81/83/192`
("always null" / "no probe history"), `monitoring.models.ts:76-79` ("gauges DEFERRED" when all four are
real), `appsettings.json:162` (describes pre-ISSUE-345 exporter behaviour), and
`CustomFieldService.cs:26-31` (a dead `TODO(subscription)` for a plan lookup that shipped). Filed
collectively as ISSUE-461. The repo's own TEST-FINDINGS notes three earlier cases where a comment
reading as "done" kept a real gap invisible for weeks — `RealNotificationDispatcher.cs:32` seeded a
phantom epic, and `TenantProvisioningService.cs:31-34` kept the US-ADM-011 workflow engine dormant for
five weeks.

The inverse also occurs: `STATUS.md` claimed the per-tenant API-call counter was deferred for five
weeks after it shipped (commit `b9906626`, 2026-07-31).

**How to apply:** for every requirement sourced from code, cite the *implementing* line, not the
doc-comment. When a comment and its code disagree, write the requirement from the code and flag the
comment as an out-of-lane finding — do not edit `src/` to fix it. Treat `STATUS.md`,
`TEST-STATUS.md` and `TEST-FINDINGS.md` as unverified claims, never evidence.

Related: [[feedback-main-deliverable-first]]
