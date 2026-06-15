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

## Known flaky areas
*(parts of the system that need extra negative tests)*
