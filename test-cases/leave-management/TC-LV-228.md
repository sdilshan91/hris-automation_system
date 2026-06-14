---
id: TC-LV-228
user_story: US-LV-011
module: Leave Management
priority: high
type: performance
status: draft
created: 2026-06-14
---

# TC-LV-228: Auto-LOP absenteeism job processes 5,000 employees within 3 minutes (NFR-1 — CONDITIONAL on Attendance source)

## 1. Test Objective
Verify NFR-1: the `ProcessAbsenteeismJob` completes a full reconciliation pass for 5,000 employees within 3 minutes. The attendance source DEPENDS on the Attendance module; the job's throughput/batching is measured against the 5,000-employee target using the available iteration path (no-op/stubbed attendance source), with the attendance-driven entry creation recorded CONDITIONAL.

## 2. Related Requirements
- User Story: US-LV-011
- Non-Functional Requirements: NFR-1
- Functional Requirements: FR-2
- Dependency: US-ATTENDANCE-* — CONDITIONAL
- Cross-ref: US-LV-002 LeaveAccrualJob batching (5,000 employees / 60s) as the scaling pattern

## 3. Preconditions
- Tenant "acme" seeded with 5,000 active employees.
- Hangfire available; the absenteeism job registered with batched iteration (e.g. 500/page, per the module convention).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee count | 5,000 | scale target |
| SLA | <= 3 minutes | NFR-1 |
| Batch size | 500/page | convention |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Trigger the absenteeism job for the 5,000-employee tenant | The job iterates all employees in batches and completes in <= 3 minutes (wall-clock), staying within memory/connection limits. |
| 2 | Inspect batching | The job pages employees (no single 5,000-row materialization); the per-batch cost is roughly linear. |
| 3 | (CONDITIONAL on Attendance) Re-run with an attendance source producing N absences | The job creates the LOP entries within the same time envelope; entry-creation throughput recorded CONDITIONAL on the Attendance source. |
| 4 | Record P95 over repeated runs | The 3-minute target holds across runs; results documented. |

## 6. Postconditions
- The auto-LOP job meets the 5,000-employee / 3-minute target; attendance-driven entry creation is conditionally measured.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
