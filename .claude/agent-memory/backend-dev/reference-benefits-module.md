---
name: reference-benefits-module
description: US-TRN-002 BenefitPlan slice — single-entity CRUD, status state machine, tenant-default-currency, TRN-003 seams
metadata:
  type: reference
---

# Benefits module (US-TRN-002 Benefit Plan Administration)

Greenfield single-entity CRUD, mirrors the **Training (US-TRN-001)** and **LeaveTypes** slices. Built on
`feat/us-trn-002-benefit-plans`.

- **Entity** `BenefitPlan : BaseEntity` (`HRM.Domain/Entities/`); enums `BenefitType`,
  `BenefitPlanStatus {Draft,Active,Inactive,Archived}` (`HRM.Domain/Enums/`). Table `benefit_plans`, migration
  `20260710200351_Benefits_Plans` + appended dormant `tenant_isolation` RLS DO-block (NEW-TENANT-TABLE RULE).
  `EnrollmentOpensAt`/`EnrollmentClosesAt` nullable cols included NOW so US-TRN-003 needs no 2nd migration.
- **Status state machine** (`BenefitPlanService.IsLegalTransition`, internal-for-test): Draft→Active/Archived;
  Active→Inactive/Archived; Inactive→Active/Archived; Archived terminal. Illegal → 409 `invalid_status_transition`.
- **Tenant default currency (US-ADM-006):** blank `Currency` on create resolves to `Tenant.Currency`
  (`_db.Tenants.Where(Id==TenantId).Select(t=>t.Currency)`) — that column IS the tenant default, maintained by
  `TenantSettingsService` (US-ADM-006). `ITenantContext` does NOT expose currency. Falls back to `"USD"` + a
  logged warning only if the tenant row/currency is blank (won't happen — `Tenant.Currency` defaults "USD").
- **No delete endpoint** — story exposes only GET/POST/PUT/status; archival = status→Archived (BR-4/AC-6). The
  US-TRN-003 enrollment-count hard-delete guard is a documented seam (a `NOTE` in `BenefitPlanService`); wire it
  when `BenefitEnrollment` lands.
- Routes `api/v1/tenant/benefits/plans` (+ `/{id}`, `/{id}/status`); reads `Benefits.View.*`/Manage, writes
  `Benefits.Manage` via `[RequirePermission]`. Contract is FE-shared/pinned in the spec — don't drift DTO/routes.
- Tests: `BenefitPlanValidatorTests` (unit) + `BenefitPlanPostgresTests` (Testcontainers: create/Draft+audit,
  validation, transitions, currency-default, before/after update audit, tenant isolation). TC id `TC-TRN-002`.
