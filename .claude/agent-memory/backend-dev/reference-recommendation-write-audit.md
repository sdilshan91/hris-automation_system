---
name: reference-recommendation-write-audit
description: ISSUE-149a — how recommendation/performance WRITE audits work; why staged Log() beats LogAndSaveAsync mid-write, and why comp figures never enter audit_log
metadata:
  type: reference
---

`PayrollAuditAction` (`HRM.Domain/Payroll/PayrollAuditAction.cs`) is the shared action catalog for
**non-payroll modules too** — Recommendation and Employee entries already live there. Do not start a
second catalog for a new module; add `{Resource}.{Verb}` constants to this one.

**`Log()` vs `LogAndSaveAsync()` — pick by path, not by taste.**
- **Write paths** stage with `_auditLogger.Log(...)` immediately **before** the service's own
  `SaveChangesAsync`, so the audit row commits atomically with the business change (interface XML doc
  says this explicitly; `PerformanceCalibrationService` is the reference implementation).
- `LogAndSaveAsync` runs its **own** `SaveChangesAsync` on the same context. Called mid-write it commits
  the half-built entity graph early and bypasses the caller's concurrency handling (in
  `RecommendationService` the private `SaveAsync` translates `DbUpdateConcurrencyException` into a 409).
  It is for **read**-audits and post-commit job steps only.

**Compensation figures must never enter `audit_log`.** Recommendation `BonusAmount/BonusPercent/
IncrementAmount/IncrementPercent/CurrentCompensation` are encrypted at rest via value converters
(`RecommendationConfiguration`) *and* gated behind `Payroll.ViewCompensation`; the reveal audit stores
field NAMES only, and `GetWorkspaceAsync` nulls `CurrentCompensation` outright. Copying the numbers into
`audit_log.after` would republish them in plaintext to a different reader set and defeat both controls.
The convention used instead: `compensationFields` (which fields carry a figure) + `changedFields` (which
figures MOVED on an override). Same posture as `Employee.NationalId.ViewSensitive`. See
[[reference-payroll-audit-bug080]].

`AuditInterceptor` is **not** a fallback: it only stamps `CreatedAt`/`CreatedBy`, it never adds
`AuditLogs` rows. If a service does not call the logger, that write is simply unaudited.
