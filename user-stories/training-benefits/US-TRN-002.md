---
id: US-TRN-002
module: Training & Benefits
priority: Should Have
persona: Tenant Admin / HR Officer
status: draft
created: 2026-07-06
sprint: backlog
acceptance_criteria_count: 4
---

# US-TRN-002: Benefits Plan Administration  [STUB — flesh out before build]

> **STUB** — goal + AC skeleton + dependencies only. Part of the Training & Benefits epic (US-TRN-EPIC),
> COMPLETION-PLAN Theme M (zero prior coverage).

## 1. Description
**As a** Tenant Admin / HR Officer,
**I want** to define and administer the tenant's benefit plans (type, coverage, cost, active period),
**So that** the organization's benefit offerings are codified and available for employee enrollment (US-TRN-003).

## 2. Preconditions
- The tenant is active.

## 3. Acceptance Criteria (SKELETON — expand before build)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An admin manages benefits | They create/edit a benefit plan | A tenant-scoped plan record is created (type, coverage details, employer/employee cost, effective dates). |
| AC-2 | A plan exists | It is activated/deactivated | Plan status governs whether it is available for new enrollments; existing enrollments are unaffected by later deactivation. |
| AC-3 | An admin edits a plan | They save | The change is audited to the tenant `audit_log`. |
| AC-4 | Two tenants administer benefits | Any action runs | Plans are tenant-isolated; no cross-tenant visibility. |

## 4–10. Requirements (TO AUTHOR)
- FR/BR/NFR/data/UI: benefit-plan model, plan lifecycle/status, cost structure, effective dating, audit, tenant isolation.

## 9. Dependencies
- US-TRN-EPIC, US-TRN-003 (enrollment consumes plans), US-ADM-006 (company settings context).

## 11. Test Hints
- Create/edit/deactivate a plan; verify audit; verify tenant isolation.
