---
id: US-TRN-002
module: Training & Benefits
priority: Should Have
persona: Tenant Admin / HR Officer
status: ready
created: 2026-07-06
updated: 2026-07-09
sprint: backlog
acceptance_criteria_count: 8
---

# US-TRN-002: Benefits Plan Administration

> Part of the Training & Benefits epic ([[US-TRN-EPIC]], COMPLETION-PLAN Theme M). **Greenfield** — no Benefit
> entity/service/controller exists; only `Benefits.View.Own` / `Benefits.View.All` / `Benefits.Manage`
> (`PermissionCatalog.cs`) and a `PlanModules` entry are pre-declared. Foundation for eligibility/enrollment
> ([[US-TRN-003]]).

## 1. Description
**As a** Tenant Admin / HR Officer,
**I want** to define and administer the tenant's benefit plans (type, coverage details, employer/employee
cost, effective period, status),
**So that** the organization's benefit offerings are codified and available for employee enrollment
([[US-TRN-003]]).

## 2. Preconditions
- The tenant is `active`/`trial` and the Benefits module is enabled for its plan (`PlanModules`).
- The acting user holds `Benefits.Manage` (create/edit plans) or `Benefits.View.All`/`View.Own` (read).
- US-NTF-006 exists (for downstream enrollment notifications; plan admin itself need not notify).

## 3. Acceptance Criteria (IEEE 830 §3.2 — Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An admin with `Benefits.Manage` opens Benefits admin | They create a benefit plan (name, type, coverage details, employer/employee cost, effective dates) | A tenant-scoped `BenefitPlan` row is created (`Status = Draft`), `TenantId` auto-stamped; required fields validated (non-blank name, valid `Type`, `EffectiveTo >= EffectiveFrom` when set, non-negative costs); the action is audited. |
| AC-2 | A `Draft` plan exists | The admin activates it | `Status → Active`; only `Active` plans within their effective window are offered for new enrollments ([[US-TRN-003]]). |
| AC-3 | An `Active` plan with existing enrollments | The admin deactivates it (`Status → Inactive`) | The plan is no longer available for **new** enrollments; **existing** enrollments are unaffected (they remain until separately terminated in US-TRN-003). |
| AC-4 | An admin edits a plan's cost or coverage | They save | The change is persisted and audited to `audit_log` with before/after values; edits do not retroactively alter historical enrollment records. |
| AC-5 | An admin lists plans | The page loads | Only the current tenant's plans are returned, showing name, type, cost, effective window, status; no cross-tenant plans appear. |
| AC-6 | An admin attempts to delete a plan with ≥1 enrollment | They click delete | Hard delete is blocked; the plan may only be `Archived` (soft) — preserving enrollment history/audit integrity. |
| AC-7 | A user without `Benefits.Manage` attempts to create/edit a plan | They call the write endpoint | The action is rejected (403); read-only users may still list/view plans per their `View.*` scope. |
| AC-8 | Two tenants administer benefits | Any plan action runs | Plans are tenant-isolated via the EF global query filter (RLS-eligible); Tenant A can neither read nor mutate Tenant B's plans. |

## 4. Functional Requirements (IEEE 830 §3.2)
- FR-1: `BenefitPlan : BaseEntity` — `Name` (string, required), `Type` (`BenefitType`: `Health`/`Dental`/
  `Vision`/`Life`/`Retirement`/`Disability`/`Other`), `Description` (string?), `CoverageDetails` (string?),
  `EmployerCost` (decimal?), `EmployeeCost` (decimal?), `Currency` (string, tenant default), `EffectiveFrom`
  (DateOnly/DateTime), `EffectiveTo` (DateOnly?/DateTime?, null = open-ended), `Status` (`BenefitPlanStatus`:
  `Draft`/`Active`/`Inactive`/`Archived`).
- FR-2: Plan CRUD (create/edit/list/get) + status transitions (`Draft→Active`, `Active→Inactive`,
  `*→Archived`, `Inactive→Active`) restricted to `Benefits.Manage`.
- FR-3: Only `Active` plans within `[EffectiveFrom, EffectiveTo]` are eligible to be offered for enrollment
  (consumed by US-TRN-003).
- FR-4: Deactivation/archival does not cascade to or mutate existing enrollments.
- FR-5: A plan with ≥1 enrollment cannot be hard-deleted (archive only).
- FR-6: All plan data tenant-scoped (`TenantId` + EF query filter); RLS-eligible.
- FR-7: All plan admin actions audited to `audit_log` with before/after state.

## 5. Non-Functional Requirements (IEEE 830 §3.3)
- NFR-1: Plan list SHALL return within 500ms for up to 200 plans.
- NFR-2: All plan data SHALL be tenant-isolated (EF query filters + RLS once enabled).
- NFR-3: Cost fields SHALL use `decimal` (no floating-point currency) and store a currency code.
- NFR-4: All plan admin actions SHALL be audited.

## 6. Business Rules
- BR-1: Only `Benefits.Manage` may create/edit/activate/archive plans.
- BR-2: Only `Active` plans inside their effective window are offered for new enrollments.
- BR-3: Deactivating/archiving a plan never alters existing enrollments.
- BR-4: A plan with enrollments is archivable, not deletable.
- BR-5: All benefit-plan data is tenant-scoped; no cross-tenant visibility.

## 7. Data Requirements
- **New table:** `benefit_plan` (tenant-scoped `BaseEntity`, snake_case, RLS-eligible). New enums
  `BenefitType`, `BenefitPlanStatus`.
- **Reads:** none beyond tenant context for admin CRUD.
- **Writes:** `benefit_plan`; `audit_log`.
- **Migrations:** `dotnet ef migrations add` only.

## 8. API Surface (proposed)
- `GET /api/v1/tenant/benefits/plans` · `GET .../plans/{id}` · `POST .../plans` · `PUT .../plans/{id}` ·
  `POST .../plans/{id}/status` (activate/deactivate/archive) — `Benefits.Manage` for writes, `View.*` reads.

## 9. Dependencies
- [[US-TRN-EPIC]].
- [[US-TRN-003]] (enrollment consumes plans — 003 depends on 002, not the reverse).
- US-ADM-006 (company settings — tenant default currency).

## 10. Assumptions & Constraints
- **v1:** plan model + lifecycle/status + employer/employee cost + effective dating + audit + tenant
  isolation.
- **Future:** plan-cost payroll integration (deductions), coverage-level premium tiers, plan-year / renewal
  cycles, vendor/carrier metadata, benefit budget tracking.

## 11. Test Hints
- Create → activate → deactivate a plan; verify status governs new-enrollment availability but not existing
  enrollments (needs a US-TRN-003 enrollment to fully assert).
- Edit cost/coverage → verify audited before/after.
- Attempt delete of a plan with enrollments → blocked; archive succeeds.
- Non-`Manage` user write → 403; `View.*` read still works.
- Cross-tenant: Tenant A cannot read/mutate Tenant B's plans.
