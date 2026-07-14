---
id: US-PAY-013
module: Payroll
priority: Must Have
persona: HR Officer / Tenant Admin / Payroll Manager
status: done
created: 2026-07-14
sprint: shipped
acceptance_criteria_count: 7
phase1_pr: "#303"
phase2: deferred
---

# US-PAY-013: Full & Final (F&F) Settlement

> **## Status:** Phase 1 **shipped in PR #303** (`status: done`). This story documents an
> already-built, merged feature to close a traceability gap (Critical Rule #4); the acceptance
> criteria below match the shipped Phase 1 code exactly. **Phase 2 is deferred** — see
> [Phase 2 — Out of Scope / Deferred](#phase-2--out-of-scope--deferred) and
> `docs/DEV/FNF-SETTLEMENT-PLAN.md`. This is the Payroll-side settlement calculation that
> **US-ONB-005 (BR-4 / FR-6) explicitly defers to the Payroll module**; US-ONB-005 only *triggers* it.

## 1. Description
**As an** HR Officer, Tenant Admin, or Payroll Manager,
**I want to** have a departing employee's final settlement computed automatically when their offboarding is completed, governed by a tenant-configurable, effective-dated F&F policy,
**So that** separated employees are paid their final dues correctly and consistently, without a manual, error-prone process.

## 2. Preconditions
- Payroll module is enabled for the tenant.
- The employee has an offboarding instance being completed (US-ONB-005).
- The employee has a current (assigned) salary structure.
- Statutory rules and leave configuration exist for the tenant/country as applicable (F&F degrades safely — skip + flag — when they do not; see AC-5).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A Tenant Admin manages payroll settings | They configure an F&F policy — which components apply (pro-rated final pay / statutory / forfeitable-leave encashment) and whether the settlement owns the final pay period | The policy is persisted as an effective-dated record (`TenantFnFPolicy`) via the policy API (`FnFPolicyController`), each edit creating a new effective-dated row rather than overwriting history. |
| AC-2 | An F&F policy has an effective date | A policy change is saved and a settlement is later computed | The change takes effect only for settlements whose Last Working Day is **on/after** the policy's effective date (next-cycle); a settlement already computed KEEPS the policy version it used (`PolicyEffectiveFrom`) and is never retroactively changed. When no policy is configured, a safe all-components-on default governs. |
| AC-3 | HR completes an offboarding for a departing employee (US-ONB-005 FR-6) | The offboarding is marked complete | The system computes and persists a Final Settlement (`FinalSettlement` + `FinalSettlementLine`) = pro-rated final-month pay + statutory deductions + forfeitable-leave encashment, per the effective policy, using the employee's Last Working Day as the authoritative separation date. |
| AC-4 | A Final Settlement already exists for an offboarding instance | The settlement is re-triggered for that same offboarding instance (including under a concurrent retry) | The existing settlement is returned; exactly one settlement exists per offboarding instance and it is never double-created (idempotency enforced by a unique index on `OffboardingInstanceId`). |
| AC-5 | A settlement is being computed | Statutory is resolved for the employee | Statutory is computed under the employee's resolved tax country (Location → Tenant default → single-country fallback → none); if the country is unresolvable or has no configured rules, statutory is SKIPPED and flagged (never taxed under the wrong country); the net payable is floored at 0 (never negative); structure statutory/deduction lines do not double-count against the settlement gross. |
| AC-6 | The effective policy makes the settlement own the final pay period (`FinalPeriodOwnedBySettlement` = true) | The regular monthly payroll run is processed for that period | The run EXCLUDES that employee's final period (guard in `PayrollRunProcessor`), so the final month is paid exactly once (no double-pay). |
| AC-7 | F&F policy and settlement data belong to Tenant A | A user from Tenant B queries the F&F policy or settlement data | Only Tenant B's records are returned; EF Core global query filters and a dormant `tenant_isolation` RLS policy on all settlement tables enforce tenant-scoped isolation. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide a tenant-configurable, effective-dated `TenantFnFPolicy` entity with a read/write API (`FnFPolicyController`) exposing the component toggles (`IncludeProRatedFinalPay`, `IncludeStatutory`, `IncludeLeaveEncashment`) and the final-period ownership flag (`FinalPeriodOwnedBySettlement`).
- FR-2: The system SHALL persist a `FinalSettlement` header plus `FinalSettlementLine` detail rows, storing `EmployeeId`, `LastWorkingDay`, `CountryCode`, `FiscalYear`, component totals, `NetPayable`, `PolicyEffectiveFrom`, `ComputedAtUtc`, and `Status`.
- FR-3: The system SHALL make settlement creation idempotent on the offboarding instance via a unique index on `OffboardingInstanceId` (exactly one settlement per offboarding).
- FR-4: The system SHALL provide the real `IPayrollFnFIntegration` implementation (`RealPayrollFnFIntegration.TriggerFinalSettlementAsync`, replacing the `LogOnlyPayrollFnFIntegration` stub), triggered by offboarding completion (`OffboardingService.CompleteAsync`).
- FR-5: The system SHALL reuse the payroll run's engines — pro-ration (`ProRataPaidDays` / `PayrollSlipCalculator`), `StatutoryDeductionResolver`, and forfeitable-leave encashment (`LeaveEncashmentService` / `LeaveCarryForwardCalculator`) — so settlement figures agree with a regular run.
- FR-6: The system SHALL resolve the effective F&F policy for a settlement as the policy row whose `EffectiveFrom` ≤ Last Working Day (latest wins); when none exists a safe default (all components on, final period owned by settlement) applies.
- FR-7: The system SHALL enforce the double-pay boundary in `PayrollRunProcessor` — when the effective policy owns the final period, the run excludes the terminated employee whose Last Working Day falls in the run period.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Money-critical correctness — the settlement SHALL fail closed (skip statutory + flag on unresolvable/unconfigured country), be idempotent, floor net payable at 0, and never double-count structure statutory/deduction lines.
- NFR-2: The settlement SHALL reuse the payroll run's calculation building blocks as the single source of truth, so preview, run, and settlement figures agree.
- NFR-3: F&F policy and settlement data SHALL be tenant-isolated via EF Core global query filters and a dormant RLS `tenant_isolation` policy on every settlement table (added in the table's migration per the NEW-TENANT-TABLE rule).
- NFR-4: Single-employee settlement compute SHALL reuse the run's batched patterns so per-settlement performance is comparable to a single employee within a run.
- NFR-5: A settlement SHALL be an immutable, point-in-time financial event once computed; the policy version it used (`PolicyEffectiveFrom`) is captured for audit.

## 6. Business Rules
- BR-1: F&F policy is effective-dated and next-cycle only — a policy change applies to settlements with a Last Working Day on/after its effective date and is NEVER retroactive to an already-computed settlement.
- BR-2: The settlement reuses the payroll run's engines (pro-ration, statutory resolver, leave encashment) so settlement numbers agree with a regular run.
- BR-3: When the employee's tax country is unresolvable, or the resolved country has no configured statutory rules, statutory is SKIPPED and flagged (never taxed under the wrong country).
- BR-4: Net payable is floored at 0 (a settlement is never negative).
- BR-5: Exactly one settlement exists per offboarding instance; re-triggering is idempotent (returns the existing settlement), including under a concurrent retry.
- BR-6: Structure statutory/deduction lines are dropped from the settlement gross so they are not double-counted.
- BR-7: The F&F settlement is a distinct financial event stored separately from monthly payroll slips (its own `FinalSettlement`/`FinalSettlementLine` tables, not `PayrollSlip`).
- BR-8: When the effective policy owns the final period, the regular payroll run excludes that employee's final period so the final month is paid exactly once.

## 7. Data Requirements

**TenantFnFPolicy (effective-dated policy):**
| Field | Type | Notes |
|-------|------|-------|
| tenant_id | uuid | RLS/query-filter enforced |
| effective_from | date | Latest ≤ LWD wins |
| include_pro_rated_final_pay | bool | Component toggle |
| include_statutory | bool | Component toggle |
| include_leave_encashment | bool | Forfeitable-leave encashment toggle |
| final_period_owned_by_settlement | bool | The double-pay boundary flag |
| is_active | bool | — |

**FinalSettlement + FinalSettlementLine:**
| Field | Type | Notes |
|-------|------|-------|
| offboarding_instance_id | uuid | UNIQUE (idempotency) |
| employee_id | uuid | — |
| last_working_day | date | Authoritative separation date |
| country_code | varchar | Resolved tax country (nullable → statutory skipped + flagged) |
| fiscal_year | — | Empty when statutory skipped/flagged |
| component totals | money | Pro-rated pay / statutory / leave encashment |
| net_payable | money | Floored at 0 |
| policy_effective_from | date | Policy version captured for audit |
| computed_at_utc | timestamptz | — |
| status | — | Settlement status |
| lines (FinalSettlementLine) | rows | Per-component detail |

**Input:** offboarding completion (US-ONB-005) supplies the offboarding instance + Last Working Day. **Output:** a persisted `FinalSettlement` with its lines and a settlement reference returned to the caller.

## 8. UI/UX Notes
- Phase 1 is **API-configurable only** — the F&F policy is managed via `FnFPolicyController`; there is **no FE policy-configuration UI yet** (deferred to Phase 2).
- The settlement is triggered as part of the existing offboarding-completion flow (US-ONB-005 confirmation modal already lists "F&F trigger" as an action taken on completion).
- Settlement PDF / statement rendering is a Phase 2 follow-up (not built in Phase 1).

## 9. Dependencies
- **US-ONB-005** — Offboarding / Exit Checklist: the trigger counterpart. Its FR-6 / BR-4 defer the F&F settlement *calculation* to this story; it only fires the notification/trigger.
- **US-PAY-006** — Statutory deductions configuration: supplies the statutory rules and `StatutoryDeductionResolver` reused here.
- **US-PAY-010** — Attendance/leave integration into payroll: supplies the forfeitable-leave encashment path reused here.
- **US-PLT-002** — Tenant isolation / RLS: the dormant `tenant_isolation` policy pattern applied to the settlement tables.

## 10. Assumptions & Constraints
- The Last Working Day passed by offboarding completion is the authoritative separation date; F&F does not depend on an `EmploymentHistory` row being written.
- The tenant policy is resolved at Last Working Day and the settlement records the policy version it used, so later policy edits cannot alter a completed settlement.
- Reuse of the run's engines is a hard constraint (single source of truth) so preview/run/settlement stay consistent.
- Only free/open-source libraries are used; PostgreSQL with RLS as defense-in-depth for tenant isolation.

## Phase 2 — Out of Scope / Deferred
The following are **explicitly deferred** (each needs BA input and a formula model — silently defaulting them would under-pay a separated employee). These become future acceptance criteria once designed; reference `docs/DEV/FNF-SETTLEMENT-PLAN.md`:
- **Gratuity** — days-per-year-of-service × basic, service-length tiers/caps (a formula engine like the tax slabs). No formula/entity/config exists today.
- **Notice pay / pay-in-lieu** — jurisdiction/legal policy; may depend on voluntary-vs-involuntary separation (trigger seam may need to pass `OffboardingReason`).
- **Severance** — jurisdiction/legal policy; no formula today.
- **Loan / advance recovery** — netting outstanding loans/advances against the settlement.
- **Settlement PDF / statement rendering** — no document output in Phase 1.
- **FE policy-configuration UI** — Phase 1 is API-configurable only.

## 11. Test Hints
- **Policy config (AC-1):** Configure an F&F policy via the API; verify it persists as an effective-dated row and an edit creates a new row (history preserved).
- **Effective-dating (AC-2):** Save a policy change; compute a settlement with LWD before the effective date (old policy applies) and on/after (new policy applies); verify an already-computed settlement is untouched by a later edit.
- **Auto-compute (AC-3):** Complete an offboarding; verify a `FinalSettlement` is persisted = pro-rated pay + statutory + forfeitable-leave encashment, using LWD as the separation date.
- **Idempotency (AC-4):** Re-trigger the settlement for the same offboarding instance (and simulate a concurrent retry); verify exactly one settlement exists and the same reference is returned.
- **Money-safety (AC-5):** Compute for an employee with an unresolvable country → statutory skipped + flagged (empty fiscal year); verify net is never negative and structure statutory/deduction lines are not double-counted.
- **No double-pay (AC-6):** With `FinalPeriodOwnedBySettlement` = true, run the regular payroll for the final period; verify the terminated employee is excluded so the final month is paid exactly once.
- **Tenant isolation (AC-7):** Create policy + settlement in Tenant A; query from Tenant B; expect no results (EF filter + RLS).
