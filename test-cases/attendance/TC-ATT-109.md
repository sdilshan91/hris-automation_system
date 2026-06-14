---
id: TC-ATT-109
user_story: US-ATT-008
module: Attendance
priority: high
type: integration
status: draft
created: 2026-06-14
---

# TC-ATT-109: Late-arrival notification on each late clock-in includes the month-to-date late count (FR-5; dispatch SEAM verified, delivery DEFERRED on US-NTF)

## 1. Test Objective
Verify FR-5/NFR-4: when an employee is marked late, the system sends them an in-app notification that includes the number of late arrivals in the current month. The dispatch SEAM (recipient = the late employee, tenant-scoped, payload includes the month-to-date late count, gated by `late_policy.notification_on_late`) is verified now; end-to-end in-app delivery + the 1-minute SLA (NFR-4) are DEFERRED on the Notification System (US-NTF).

## 2. Related Requirements
- User Story: US-ATT-008
- Functional Requirements: FR-5 (notify employee when marked late, including monthly late count)
- Non-Functional: NFR-4 (delivered within 1 minute of clock-in -- DEFERRED on US-NTF)
- Data: late_policy.notification_on_late (S7)
- Dependency: Notification System (US-NTF)

## 3. Preconditions
- Tenant "acme"; late_policy with `notification_on_late = true`, is_active = true.
- Employee "Asha" on a 09:00 SINGLE shift, 15-min grace, with 1 late already this month.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| notification_on_late | true | gates the notification |
| existing lates this month | 1 | prior count |
| this clock-in | 09:22 (past grace) | the 2nd late |
| expected payload count | 2 | month-to-date late count after this late |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Asha, clock in at 09:22 (past grace), with notification_on_late = true | The attendance_log is flagged is_late; a late-notification is queued/logged with recipient = Asha (tenant acme), payload including the month-to-date late count = 2 (FR-5). |
| 2 | Inspect the notification seam | The recipient is the late employee only (not the manager), tenant-scoped, and the payload references the attendance date + monthly count. |
| 3 | Set notification_on_late = false and record another late | NO late notification is dispatched (the flag gates it) -- the is_late flag is still set on the log. |
| 4 | Verify delivery + SLA DEFERRED | End-to-end in-app delivery, badge increment, and the NFR-4 1-minute SLA are DEFERRED on US-NTF; only the dispatch seam + gating are asserted now. |

## 6. Postconditions
- A tenant-scoped late-notification seam for Asha (when enabled) referencing the month-to-date late count; delivery DEFERRED on US-NTF.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **Notification dispatch + NFR-4 SLA DEFERRED on US-NTF (FR-5).** Consistent with US-ATT-003 TC-ATT-032, US-ATT-004 TC-ATT-037/038, US-ATT-006 TC-ATT-071. The seam (recipient/payload/tenant-scope/gating flag) is verified now. **Reported to caller.**
- AC-4's "notifies the employee" on reaching a deduction threshold is a related but distinct notification (deduction-threshold vs per-late); the per-late notification is covered here, the deduction-threshold notification seam in TC-ATT-107 Step 5.
