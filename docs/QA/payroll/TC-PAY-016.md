---
id: TC-PAY-016
user_story: US-PAY-006
module: Payroll
priority: high
type: integration
status: automated
created: 2026-07-17
---

# TC-PAY-016: Cumulative-PAYE run true-up persists correct withheld deltas on real Postgres (DF-2 / ISSUE-300)

## 1. Test Objective
Verify the full end-to-end cumulative income-tax RUN on real PostgreSQL: the processor runs the payroll month-by-month and **persists** the YTD true-up withheld deltas onto the slips, read back from a fresh Npgsql context. Closes the last InMemory-only gap on the cumulative money path (DF-2, residual of ISSUE-300). The `IsCumulative` flag must genuinely gate behaviour on the real provider.

## 2. Related Requirements
- User Story: US-PAY-006 (income-tax / PAYE)
- Finding: ISSUE-300 residual → DF-2 (PR #347)
- Business Rule: cumulative PAYE true-up = `tax(cumulative-FY-taxable) − tax-already-withheld-YTD`

## 3. Preconditions
- Real PostgreSQL (Testcontainers `postgres:17-alpine`), migrations applied.
- LK IncomeTax `StatutoryRule` with ANNUAL bands (0–3M @0%, 3M–6M @6%, 6M+ @12%), `IsCumulative = true`.
- Employee at 1,000,000/month; tenant default country LK; attendance locked fully-present per month.
- Seed note: on real Postgres the Employee → departments/job_titles FKs are enforced, so a real Department + JobTitle are seeded (InMemory ignored them).

## 4. Test Data
| Month | Cumulative taxable | Expected `IncomeTaxWithheld` |
|-------|--------------------|------------------------------|
| Apr | 1,000,000 | 0 (≤ 3M) |
| May | 2,000,000 | 0 |
| Jun | 3,000,000 | 0 (0% boundary) |
| Jul | 4,000,000 | 60,000 (1M @ 6%; prior 0) |
| Aug | 5,000,000 | 60,000 (120,000 − 60,000 prior) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run payroll Apr→Aug via `InitiatePayrollRunCommand` → `PayrollRunProcessor.ProcessAsync`. | Each run succeeds. |
| 2 | Read each slip back from a fresh Npgsql context. | `IncomeTaxWithheld` = `0,0,0,60000,60000`. |
| 3 | Assert `TaxableIncome` on every slip. | 1,000,000 each month (persisted regardless of withheld). |
| 4 | Assert the July slip deduction. | `TotalDeductions == 60000`, `NetSalary == 940000`. |
| 5 | Repeat with `IsCumulative = false` (same annual bands). | 0 withheld every month (flag gates behaviour; July diverges 60,000 vs 0). |

## 6. Postconditions
- Slips persist the cumulative withheld deltas; no residue.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test (band crossings)
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite, real Postgres/Testcontainers):**
  - `YtdCumulativeRunPostgresTests.Cumulative_WithholdsYtdTrueUp_AcrossMonths_OnPostgres_Df2` — the Apr→Aug true-up read back from real slips.
  - `YtdCumulativeRunPostgresTests.NonCumulative_TaxesEachMonthIndependently_OnPostgres_Df2` — the flag-gate contrast (kills an `if(IsCumulative)→if(true)`).
- Complements the InMemory arithmetic arms (`YtdCumulativeTaxIntegrationTests`) and the column/accumulation-read arms (`YtdCumulativeTaxPostgresTests`).
- Backing suite trait: `[Trait("TC", "TC-PAY-016")]`.
