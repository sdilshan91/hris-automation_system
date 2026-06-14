---
id: TC-ATT-071
user_story: US-ATT-006
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-071: Weekly overtime cap -- accumulating 21h in a week against a 20h cap raises an HR alert (boundary; notification DEFERRED)

## 1. Test Objective
Verify BR-5/FR-8 (weekly cap): as an employee accumulates overtime across a week, the system tracks the running weekly total against the tenant's maximum weekly overtime and raises an HR alert when the limit is approached/exceeded. Worked example: 21h of overtime accumulated in one week against a 20h cap triggers the alert. The alert DISPATCH is DEFERRED on the Notification System (US-NTF); this TC verifies the flag/seam now.

## 2. Related Requirements
- User Story: US-ATT-006
- Functional Requirements: FR-8 (cap weekly; alert HR if exceeded)
- Business Rules: BR-5 (max weekly overtime tenant-configurable, default 20h; alert HR when approaching the limit)

## 3. Preconditions
- Tenant "acme", max_weekly_overtime = 1200 min (20h), threshold 30 min.
- Employee "Asha" with several days of overtime already recorded in the current week summing to just under 20h, then a clock-out that pushes the weekly total to 21h.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| max_weekly_overtime | 1200 min (20h) | BR-5 default |
| prior weekly OT | 1140 min (19h) | from earlier days this week |
| today's OT | 120 min (2h) | pushes weekly total to 21h |
| expected weekly total | 1260 min (21h) | over the 20h cap |
| expected | HR-alert flag/seam raised | FR-8 (dispatch DEFERRED on US-NTF) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Clock out the day that pushes the weekly OT total to 21h | 200; the overtime_record is created; the running weekly total is computed across the tenant's week definition. |
| 2 | Verify the weekly-cap evaluation | The system detects the weekly total (1260 min) exceeds the 20h cap and raises the HR-alert SEAM (a flag on the data / a queued/logged alert intent), tenant-scoped, identifying the employee and the week. |
| 3 | Verify the alert DISPATCH | DEFERRED -- the in-app/email HR notification is owned by US-NTF (not built). Assert the dispatch seam fires (correct recipient = HR, tenant-scoped, references the employee + weekly total) and DEFER end-to-end delivery/badge assertions. |
| 4 | Boundary -- weekly total exactly at 20h | The "approaching/at limit" behaviour follows the tenant's configured approach-threshold; assert at-cap vs over-cap consistently with the documented rule (alert on exceed; "approaching" alert per config). |
| 5 | Change max_weekly_overtime to 1500 (25h) and re-run | No alert at 21h -- the weekly cap is tenant-configurable (BR-5). |

## 6. Postconditions
- The weekly overtime total is tracked and an HR-alert seam fires when the configured weekly cap is exceeded; the actual notification delivery is deferred on US-NTF.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **HR-alert notification DEFERRED on US-NTF.** Consistent with the module-wide handling of notification seams (TC-ATT-032 manager-notification, TC-ATT-037/038 employee-notification, and leave-management's notification deferrals). The seam (recipient/payload/tenant-scope) is verified now; in-app delivery + badge assertions activate when US-NTF lands. **Reported to caller.**
- **Week definition:** the tenant's week boundary (e.g. Mon-Sun vs a payroll week) governs the running total. Assert against the documented week definition; if undocumented, flag to the BA.
- Whether the weekly cap also CAPS the recorded minutes (like the daily cap, TC-ATT-070) or only alerts is a story ambiguity -- FR-8 says "cap ... and alert"; BR-5 says only "alert ... when approaching." This TC asserts the alert; if the backend also caps weekly, add an assertion mirroring TC-ATT-070. **Reported to caller.**
