# Full-and-Final (F&F) Settlement — Implementation Plan (ISSUE-294)

> **Status:** 📋 Phase 1 in progress. Replaces the `LogOnlyPayrollFnFIntegration` stub with a real,
> **tenant-configurable, effective-dated** final-settlement engine. Direction set by the user
> (2026-07-14): F&F behaviour is a per-tenant policy; policy changes take effect on the **next pay
> cycle** and are **never retroactive** to an already-locked/computed settlement — mirroring how
> `StatutoryRule` effective-dating already works in this system.

## Why
`PayrollRunProcessor` selects only `Active`/`Probation` employees (`PayrollRunProcessor.cs:114-125`), so a
fully `Terminated` employee is excluded from the regular monthly run. Their final pay is supposed to flow
through `IPayrollFnFIntegration` (`OffboardingService.CompleteAsync:401-403`), which is currently a
`LogOnly` stub (`TODO(payroll integration)`). Terminated employees' final settlement is therefore not
actually processed. (ISSUE-294, MED — real money not paid end-to-end.)

## Design decisions
- **Persistence = a dedicated `FinalSettlement` entity (ARCHITECTURE, not a tenant knob).** F&F is a
  distinct one-time financial event; `PayrollSlip` requires a `PayrollRunId` + one-slip-per-run guards, so
  reusing it would corrupt the monthly-run invariants. The tenant-configurable surface is the *policy*, not
  the storage. PDF/reporting of the settlement is a later follow-up.
- **Behaviour = a per-tenant, effective-dated `TenantFnFPolicy`.** A settlement reads the policy whose
  `EffectiveFrom ≤ LastWorkingDay` (latest wins). Editing policy creates a new effective-dated row; a
  settlement already computed keeps the version it used (audit: `FinalSettlement.PolicyEffectiveFrom`).
- **Reuse, don't reinvent:** pro-rated final pay (`ProRataPaidDays` + `PayrollSlipCalculator`), statutory
  (`StatutoryDeductionResolver`), forfeitable-leave encashment (`LeaveEncashmentService`/
  `LeaveCarryForwardCalculator`) — all already exist and are consumed by the settlement engine.

## Phasing
### Phase 1 (safe, fully-specified — build now)
- **`TenantFnFPolicy`** (effective-dated): `IncludeProRatedFinalPay` (bool), `IncludeStatutory` (bool),
  `IncludeLeaveEncashment` (bool), `FinalPeriodOwnedBySettlement` (bool — the double-pay boundary), `IsActive`.
  Default policy (seeded / fallback when none configured): all three includes = true, `FinalPeriodOwnedBySettlement` = true.
- **`FinalSettlement` + `FinalSettlementLine`**: idempotent on `OffboardingInstanceId` (unique index);
  stores `EmployeeId`, `LastWorkingDay`, `CountryCode`, `FiscalYear`, component totals, `NetPayable`,
  `PolicyEffectiveFrom`, `ComputedAtUtc`, `Status`. Dormant `tenant_isolation` RLS policy in its migration
  (NEW-TENANT-TABLE rule).
- **`RealPayrollFnFIntegration.TriggerFinalSettlementAsync`**: idempotent (return existing ref if a
  settlement exists for the `offboardingInstanceId`); resolve tenant policy at LWD; resolve employee country
  (Location→Tenant precedence, same as the run); compute the enabled components; persist the settlement +
  lines; return the settlement ref.
- **Double-pay boundary:** when `FinalPeriodOwnedBySettlement` is true, the regular run excludes an employee
  whose termination (LWD) falls in the run period (a guard in `PayrollRunProcessor`), so the final period has
  exactly one owner. Also: `OffboardingService.CompleteAsync` flips status **without** an `EmploymentHistory`
  row — F&F uses the passed `LastWorkingDay` as the authoritative separation date (does not depend on that row).
- **Money-critical guards:** null-country → skip statutory + flag (mirror the run); never negative net;
  idempotent so a retried offboarding-complete doesn't double-create.
- **Tests:** InMemory full-chain (policy toggles change the components; idempotency; per-country; boundary
  exclusion in the run) + Testcontainers (unique-index idempotency + RLS).

### Phase 2 (deferred — needs a formula model + BA input; filed as follow-ups)
- **Gratuity** (days-per-year-of-service × basic, service-length tiers, caps — a formula engine like the tax
  slabs), **notice pay / pay-in-lieu**, **severance**, **loan/advance recovery**. Each is jurisdiction/legal
  policy with no formula/entity/config today; silently defaulting them would under-pay. `TenantFnFPolicy`
  gains their config once the formula model is designed with BA. The trigger seam may need widening to pass
  `OffboardingReason` (gratuity/notice eligibility can depend on voluntary-vs-involuntary).

## Out of scope for v1 (explicit, not silent)
Gratuity, notice pay, severance, loan recovery, settlement PDF/statement rendering, and an FE policy-config UI
(the policy is API-configurable in Phase 1; the FE surface is a follow-up). These are tracked follow-ups, not
omissions.
