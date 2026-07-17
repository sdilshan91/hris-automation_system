---
id: TC-PAY-017
user_story: US-PAY-011
module: Payroll
priority: medium
type: integration
status: automated
created: 2026-07-17
---

# TC-PAY-017: Unmapped payroll-run status string reads as the Unknown sentinel, not a 500 (ENH-021)

## 1. Test Objective
Verify that a `payroll_run.status` value outside the `PayrollRunStatus` enum does **not** 500 the run list/guard query on materialization. Through the tolerant converter it maps to the read-only `Unknown` sentinel (and is logged). Regression for ENH-021.

## 2. Related Requirements
- User Story: US-PAY-011
- Finding: ENH-021 (PR #348)
- Root cause: EF strict enum→string converter throws during materialization (EF Core 6, dotnet/efcore#24084)

## 3. Preconditions
- Real PostgreSQL (Testcontainers `postgres:17-alpine`), migrations applied.
- This class is Postgres-only: InMemory reads through the same converter, so an out-of-enum string can only be planted via raw SQL against a real DB.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Seeded `status` | `Finalized` | valid, via EF |
| Corrupted `status` | `'Draft'` | raw-SQL `UPDATE`, outside the enum |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Seed a `PayrollRun`, then raw-SQL `UPDATE payroll_run SET status='Draft'`. | Row persisted with an out-of-enum status string. |
| 2 | Read the run list (`WHERE pay_year == 2026`) from a fresh context. | No throw (strict converter would 500 here). |
| 3 | Assert the corrupted run's `Status`. | `PayrollRunStatus.Unknown`. |

## 6. Postconditions
- No 500; the `PayrollRunService` status `switch` `default:` arm treats `Unknown` as a clean 409 (`run_not_rerunnable`), never a wrong branch.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test (corrupt data)
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite, real Postgres/Testcontainers):**
  - `TolerantEnumReadPostgresTests.PayrollRunRead_ToleratesUnknownStatus_OnPostgres_Enh021`.
- **Mutation-verified:** reverting `PayrollRunConfiguration.Status` to strict `.HasConversion<string>()` fails the arm with `Cannot convert string value 'Draft' … 'PayrollRunStatus' enum`.
- Backing suite trait: `[Trait("TC", "TC-PAY-017")]`.
