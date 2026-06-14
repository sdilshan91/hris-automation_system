---
id: TC-ATT-096
user_story: US-ATT-007
module: Attendance
priority: high
type: integration
status: draft
created: 2026-06-14
---

# TC-ATT-096: Hangfire summary jobs -- daily previous-day computation and monthly 1st-of-month aggregation, tenant-scoped (FR-1/FR-2)

## 1. Test Objective
Verify FR-1/FR-2: a Hangfire recurring job runs daily (e.g. 1:00 AM tenant timezone) to compute and cache the attendance summary for the previous day, and a monthly aggregation job runs on the 1st of each month for the previous month; both write the attendance_monthly_summary rows tenant-scoped (the job uses the resolved tenant context per S10) and are idempotent on re-run.

## 2. Related Requirements
- User Story: US-ATT-007
- Functional Requirements: FR-1 (daily previous-day job), FR-2 (monthly previous-month aggregation job)
- Assumptions/Constraints: S10 (Hangfire job uses tenant-scoped queries; materialized/cached summary)

## 3. Preconditions
- Tenants "acme" and "globex" both active with attendance data for the previous day and previous month.
- Hangfire is running; the daily and monthly recurring jobs are registered.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| daily job | runs ~1:00 AM | FR-1 |
| monthly job | runs on the 1st | FR-2 |
| previous month | 2026-04 (if run on 2026-05-01) | aggregation target |
| tenants | acme, globex | per-tenant scoping |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Trigger the daily summary job | For each tenant, the previous day's attendance summary is computed and cached/persisted tenant-scoped; acme rows reference only acme employees, globex only globex. |
| 2 | Trigger the monthly aggregation job (simulate the 1st) | The previous month's attendance_monthly_summary rows are produced per tenant with all FR-3 columns and generated_at stamped. |
| 3 | Verify tenant scoping inside the batch | The job iterates per tenant using the tenant context; no cross-tenant rows are written (no acme employee in globex summary, and vice versa). |
| 4 | Re-run the monthly job for the same period | Idempotent -- it refreshes/upserts the existing rows rather than creating duplicates for the same (tenant, employee, year_month). |
| 5 | Verify the daily job feeds the monthly view | The monthly aggregation reconciles with the accumulated daily summaries (consistent totals). |

## 6. Postconditions
- Daily and monthly Hangfire jobs produce correct, tenant-scoped, idempotent summary rows; the monthly view reconciles with the daily computations.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- The daily job's "previous day" and the 1:00 AM schedule reference the tenant timezone per FR-1; the platform's tenant-timezone infra is DEFERRED module-wide (UTC day boundaries used). If tenant-local scheduling is required, that is the same deferred concern. **Reported to caller.**
- The Redis caching of the daily summary (FR-8) is covered as CONDITIONAL in TC-ATT-098 (DB/materialized-table path verified now).
- This job's tenant-scoping is the batch-processing complement to TC-ATT-ISO-010 (request-path isolation).
