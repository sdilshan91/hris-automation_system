---
id: TC-LV-088
user_story: US-LV-004
module: Leave Management
priority: medium
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-088: Multi-level approval -- queue shows requests at the manager's approval level (forward-looking)

## 1. Test Objective
Verify that when multi-level approval is enabled for a tenant, the pending queue shows requests at the current manager's approval level (not skip-level reports prematurely). When multi-level approval is not configured, the queue defaults to direct reports only.

## 2. Related Requirements
- User Story: US-LV-004
- Business Rules: BR-1, BR-2
- Dependencies: multi-level approval workflow configuration (leave-approval story) -- forward-looking

## 3. Preconditions
- Tenant "acme" is active.
- Manager "Robert Lee" is authenticated with `Leave.Approve.Team`.
- Scenario A (default): multi-level approval is NOT configured.
- Scenario B (forward-looking): multi-level approval IS configured; Robert is the level-1 approver for his direct reports, and a level-2 approver exists above him.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Direct reports | Jane, Alan | Level-1 under Robert |
| Skip-level report | Mara (reports to Jane) | Level-2 candidate |
| Multi-level config | Off (A) / On (B) | Per tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Scenario A: load the pending queue | Only Robert's direct reports' requests appear (Jane, Alan); skip-level reports (Mara) are NOT shown (BR-1). |
| 2 | Scenario B: a request from Jane requires level-1 then level-2 approval | At level-1 stage, the request appears in Robert's queue (his approval level). |
| 3 | Scenario B: after Robert approves at level-1 | The request moves to the level-2 approver's queue and leaves Robert's pending queue (BR-2). |
| 4 | Scenario B: a request already at level-2 | Does NOT appear in Robert's level-1 queue. |
| 5 | If multi-level approval workflow is NOT yet implemented | Scenario B is marked DEFERRED/forward-looking (dependent on the leave-approval workflow story); Scenario A (direct-reports default) is verified now. |

## 6. Postconditions
- No data mutated.
- Queue reflects the manager's approval level; defaults to direct reports when multi-level approval is off.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
