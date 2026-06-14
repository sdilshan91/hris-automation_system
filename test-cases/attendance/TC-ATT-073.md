---
id: TC-ATT-073
user_story: US-ATT-006
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-073: Manager overtime approval queue -- lists pending overtime for the manager's team with employee, date, hours, reason (happy path)

## 1. Test Objective
Verify AC-3/FR-5: a manager navigating to the overtime approvals queue (`GET /api/v1/attendance/overtime/pending`) sees all PENDING overtime records for their direct-report team, each showing employee name, date, overtime hours, and reason; records outside the team and already-decided records are excluded; tenant-scoped.

## 2. Related Requirements
- User Story: US-ATT-006
- Acceptance Criteria: AC-3
- Functional Requirements: FR-5 (route overtime records for manager approval via the Approval Workflow Engine)

## 3. Preconditions
- Tenant "acme". Manager "Ben" authenticated with the overtime-approve permission; two direct reports ("Asha", "Carl") and one non-report ("Dana", different manager).
- PENDING overtime records exist for Asha and Carl; a PENDING record exists for Dana; an already-APPROVED record exists for Asha.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Manager | Ben | direct reports Asha, Carl |
| Out-of-team | Dana | reports to a different manager |
| Queue fields | employee name, date, overtime hours, reason, status | AC-3 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Ben, `GET /api/v1/attendance/overtime/pending` | Returns the PENDING overtime for Asha and Carl only, each with employee name, date, overtime hours, and reason; tenant_id = acme. |
| 2 | Confirm out-of-team exclusion | Dana's PENDING record is NOT in Ben's queue (FR-5 scopes to direct reports). |
| 3 | Confirm decided exclusion | Asha's already-APPROVED record is NOT in the pending queue (only PENDING shown). |
| 4 | Confirm the unified-hub separation | The queue is the overtime tab/filter of the same approval hub as regularization (§8 UI note); switching between the regularization queue (US-ATT-004) and overtime queue shows the correct, separate sets. |
| 5 | Manager with no pending team overtime | Empty queue, no error. |
| 6 | UNAPPROVED records (pre-approval policy) | Appear in the appropriate HR/UNAPPROVED view, not as normal manager-PENDING items (consistent with TC-ATT-072). |

## 6. Postconditions
- The manager sees only their team's PENDING overtime with the required columns; no cross-team or decided records leak.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **Approval Workflow Engine (FR-5):** the configurable N-level routing engine (US-ADM-007) is not built; the default single-level routing to the direct manager is verified live. Multi-level routing is DEFERRED, consistent with US-ATT-004 TC-ATT-044 and US-LV-005 TC-LV-097. **Reported to caller.**
- Team membership is via the reporting structure (ReportsToEmployeeId, US-CHR-011), the same source US-ATT-004's queue uses.
