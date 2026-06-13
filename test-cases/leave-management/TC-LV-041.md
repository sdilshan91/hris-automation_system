---
id: TC-LV-041
user_story: US-LV-002
module: Leave Management
priority: critical
type: performance
status: draft
created: 2026-06-13
---

# TC-LV-041: Entitlement recalculation for 5,000 employees completes within 60 seconds

## 1. Test Objective
Verify that the Hangfire background job for entitlement recalculation can process 5,000 employees within 60 seconds, as specified in NFR-1. This tests the scalability and performance of the rule evaluation engine and ledger write operations under load.

## 2. Related Requirements
- User Story: US-LV-002
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- 5,000 active employees exist in the "acme" tenant across multiple departments and job levels.
- At least 10 entitlement rules exist (mix of default, department, level, combined).
- Hangfire infrastructure is running with sufficient worker threads.
- Database connection pool is appropriately sized.

## 4. Test Data
| Parameter | Value | Notes |
|-----------|-------|-------|
| Employee Count | 5,000 | Active employees in single tenant |
| Rule Count | 10+ | Various specificity levels |
| Target Duration | 60 seconds | NFR-1 SLA |
| Leave Types | 5 | Annual, Sick, Casual, Study, Bereavement |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Seed "acme" tenant with 5,000 active employees across 10 departments, 5 job levels | Employees created (may use bulk seed script). |
| 2 | Create 10+ entitlement rules covering all leave types with varying specificity | Rules saved. |
| 3 | Record the start timestamp | Timestamp captured. |
| 4 | Trigger the Hangfire entitlement recalculation job for the entire "acme" tenant | Job starts. |
| 5 | Monitor the Hangfire dashboard for job progress | Job progresses through all 5,000 employees. |
| 6 | Record the end timestamp when job completes | Timestamp captured. |
| 7 | Calculate elapsed time: end - start | Elapsed time <= 60 seconds. |
| 8 | Verify all 5,000 employees have updated `leave_ledger` entries | COUNT of employees with new accrual entries = 5,000. |
| 9 | Verify no errors or partial failures in the job output | Job status = Succeeded with zero failures. |
| 10 | Spot-check 10 random employees for correct entitlement values | All 10 have correct balances matching their most specific rule. |
| 11 | Verify database connection pool was not exhausted during the job | No connection timeout errors in logs. |

## 6. Postconditions
- All 5,000 employees have correct entitlement balances.
- The job completed within the 60-second SLA.
- No database connection or memory issues occurred.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
