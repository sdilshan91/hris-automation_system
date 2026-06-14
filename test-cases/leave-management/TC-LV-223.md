---
id: TC-LV-223
user_story: US-LV-011
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-223: Audit trail + employee notification for ALL LOP assignments (auto, manual, compulsory) (NFR-4 / BR-6)

## 1. Test Objective
Verify NFR-4 and BR-6: every LOP assignment — system-generated (auto), HR-assigned (manual), and compulsory (shutdown) — is recorded in the audit trail (actor, action, before/after) and triggers an employee notification.

## 2. Related Requirements
- User Story: US-LV-011
- Non-Functional Requirements: NFR-4
- Business Rules: BR-6
- Cross-ref: AuditInterceptor (vault); notifications module (dispatch DEFERRED)

## 3. Preconditions
- Tenant "acme"; LOP type exists; employees "Mark" and "Ben".
- HR Officer "Asha" authenticated with `Leave.Manage`/`HR.Officer`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Manual LOP | Mark, 2 days | hr_assigned |
| Compulsory LOP | Ben, 1 day shortfall | compulsory |
| Auto LOP | Mark, 1 day | system_generated (CONDITIONAL Attendance) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Asha assigns manual LOP to Mark | An audit record captures actor = Asha, action = LOP assigned, target = Mark, the LOP details (before/after); a notification is queued to Mark. |
| 2 | Asha bulk-assigns compulsory shutdown affecting Ben (LOP shortfall) | Audit record for the compulsory/LOP assignment with actor = Asha; Ben is notified. |
| 3 | (CONDITIONAL Attendance) The absenteeism job auto-generates an LOP for Mark | The auto-assignment is audited with a system/job actor; Mark is notified. Mark this row CONDITIONAL on the Attendance-driven trigger. |
| 4 | Verify notification dispatch seam | Notifications are queued via the best-effort/non-blocking seam; actual delivery dispatch is DEFERRED on the notifications module (the queued/log-only seam is verified, not a silent gap). |

## 6. Postconditions
- All three LOP assignment paths produce an audit record and an employee notification (dispatch DEFERRED on notifications module).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
