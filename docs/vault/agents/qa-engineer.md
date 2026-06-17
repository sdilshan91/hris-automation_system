---
type: agent-note
agent: qa-engineer
---

# @qa-engineer

Persistent notes for the qa-engineer agent.

Refer to the agent definition in [.claude/agents/team/qa-engineer.md](../../../.claude/agents/team/qa-engineer.md).

## Test design patterns
*(IEEE 829 templates the agent prefers, equivalence classes commonly missed)*

## Cross-module test scenarios
*(integration scenarios that span modules — auth + leave, payroll + attendance, etc.)*

- **Recruitment convert-to-employee (US-REC-010)** is the module's main cross-module seam: one atomic transaction writes across Recruitment (`applicant` link + `vacancy.filled_count`), Core HR (`employee` + auto employee number per tenant pattern), and Authentication (`User` + `UserTenant` + default Employee role, when "auto-create user accounts on hire" is enabled). Test atomicity by injecting a failure in the Auth step (duplicate user email) and asserting NO orphan employee/account/increment (TC-REC-010-09). Subscription gating uses `Tenant.MaxEmployees` (nullable; null=unlimited) — a real limit field today, not a stub (TC-REC-010-10). Welcome email + auto-close notifications are async via Hangfire/Notification System S25 (assert the enqueue, delivery CONDITIONAL). Onboarding checklist (FR-8) is a trigger seam only — owned by the Onboarding module.

- **Payroll-report sensitive-access audit (US-RPT-003)** is a Reports↔Payroll↔Audit seam: revealing/exporting FULL bank account numbers must write an audit row with the EXACT action string `"PayrollReport.ViewSensitive"` (NFR-3, via US-NTF-004 audit trail) and is gated by a NEW `Payroll.ViewSensitive` permission. Caveats verified in code: (1) `PermissionCatalog.cs` does NOT yet define `Payroll.ViewSensitive` — only Payroll.View/.View.Own/.Run/.Approve/.Configure/.Export; every `PayrollReportsController` endpoint (list/generate/analytics/bank-advice preview/export) is gated on `Payroll.Export` today — flag the permission-granularity gap. (2) Masking is split across endpoints today: `/reports/bank-advice/preview` masks (last 4); `/reports/{reportType}/export` (BankAdvice) emits FULL accounts — the full path must be re-gated behind the new permission for FR-6/NFR-3. Assert each successful full-access is audited (not just the first), and the action string verbatim (not `Payroll.ViewSensitive`, not lowercased).

## Known flaky areas
*(parts of the system that need extra negative tests)*
