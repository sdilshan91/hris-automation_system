---
id: US-ADM-012
module: Admin Console — Platform / Tenant Governance
priority: Must Have
persona: System Admin / Tenant Admin
status: draft
created: 2026-07-06
sprint: backlog
acceptance_criteria_count: 5
---

# US-ADM-012: Plan / Module Governance Enforcement (Runtime Gating + Usage Limits)  [EPIC STUB]

> **STUB** — goal + AC skeleton + dependencies only; full detail to be authored before build.
> **Reconciliation story (COMPLETION-PLAN Theme H).** US-ADM-009 lets an admin *configure* plans/modules,
> but nothing enforces them at runtime: a disabled module's API is not 403'd (no FE route guard), and
> usage limits (storage / API / email / custom-field cap) are config-only. This is a billing/entitlement
> integrity gap. Related open findings: BUG-114 (storage quota unenforced), CustomField cap unenforced.

## 1. Description
**As a** System Admin (entitlements) and Tenant Admin (predictable limits),
**I want** the platform to enforce the tenant's subscribed plan at runtime — gating disabled modules and
enforcing usage limits — not merely store the configuration,
**So that** entitlements are real, over-limit usage is prevented/flagged, and disabled features are inaccessible.

## 2. Preconditions
- Plan/module configuration exists per tenant (US-ADM-009) with module enablement flags and numeric limits.

## 3. Acceptance Criteria (SKELETON — expand before build)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A tenant's plan has a module disabled | A user calls that module's API | The request is rejected with 403 (disabled-module) — enforced server-side, not just hidden in the UI. |
| AC-2 | A module is disabled | The SPA renders navigation/routes | An FE route guard blocks navigation to that module and hides its nav entry. |
| AC-3 | A tenant is at its storage/API/email/custom-field limit | An action would exceed the limit | The action is blocked (or flagged per limit type) with a clear "limit reached — upgrade" message; covers BUG-114 storage quota + custom-field cap. |
| AC-4 | Usage accrues over time | Usage is queried | Per-tenant usage counters (storage bytes, API calls, emails sent, custom-field count) are tracked and readable (feeds US-ADM-002 monitoring + US-PLT-004). |
| AC-5 | Two tenants on different plans | Enforcement runs | Each tenant is gated by its own plan; no cross-tenant entitlement bleed. |

## 4–10. Requirements (TO AUTHOR)
- FR/BR/NFR/data/UI to be written: entitlement middleware/policy, per-module authorization, usage-counter store, limit-check hooks on the relevant write paths, upgrade UX, System-Admin plan mapping.

## 9. Dependencies
- US-ADM-009 (plan/module config), US-ADM-002 (monitoring surfaces usage), US-PLT-004 (usage counters/observability), US-NTF-006 (email-limit counting).

## 11. Test Hints
- Disabled-module API returns 403; FE guard blocks route; storage/custom-field limit blocks the over-limit write; usage counters increment per tenant.
