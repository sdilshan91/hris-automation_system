---
name: project-payroll-statutory-contract-traps
description: Two money-path traps in the payroll statutory FE↔BE contract found during the D1 wire-type migration — the list endpoint carries no tax bands, and an omitted countryCode makes the test-calculation resolve nothing
metadata:
  type: project
---

The payroll **statutory** surface (US-PAY-006) has two contract traps that look like FE bugs but
originate in what the API does and does not carry. Both were found on 2026-09-01 during the D1
wire-type migration and flagged as `OUT-OF-LANE` findings.

1. **`GET /api/v1/payroll/statutory-rules` returns LIST ITEMS, not full rules.** The response is
   `PagedResultOfPayrollStatutoryRuleListItemDto`; a list item has `slabCount` but **no `taxSlabs`,
   no `socialSecurity`, no `updatedAt`**. The full shape only comes from `GET /statutory-rules/{id}`
   (and the create/update/clone responses). Any editor that hydrates its form from the list will
   hydrate empty.
2. **The statutory resolver fails CLOSED on country.** `StatutoryDeductionResolver.ResolveAsync`
   returns an all-zero result when `countryCode` is null — deliberately, so no arbitrary country's
   tax rules are ever applied. So any caller (including the FR-5 test-calculation preview) that omits
   `countryCode` gets zeros back regardless of how the tenant's slabs are configured.

**Why:** both are silent — no 404, no 400, no console error. They surface as "the tax screen shows
nothing / shows zeros", which reads as an FE rendering bug and gets debugged in the wrong layer.

**How to apply:** when touching statutory config, payslip preview, or anything that resolves
deductions, check *which* DTO the endpoint actually returns and whether a country is being threaded
through, before assuming the view-model is at fault. Verify against `contracts/openapi/hrm-v1.json`
rather than the hand-written `*.models.ts` interface. Related: [[feedback-payroll-defaulting]].
