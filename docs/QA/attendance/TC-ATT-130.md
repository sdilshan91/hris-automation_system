---
id: TC-ATT-130
user_story: US-ATT-010
module: Attendance
priority: high
type: functional
status: pass
created: 2026-06-15
---

# TC-ATT-130: Live attendance board -- per-employee status (Clocked In / Not Clocked In / On Leave / Holiday); SignalR real-time DEFERRED on US-NTF, 30s polling fallback verified

## 1. Test Objective
Verify the live attendance board (AC-2, FR-2): `GET /api/v1/attendance/dashboard/live-board` returns a real-time list of all employees the caller may view, each with their current status -- Clocked In (with clock-in time), Not Clocked In, On Leave, or Holiday -- correctly classified from today's attendance / leave / holiday state, and that when an employee clocks in the board reflects the new status on the next refresh. The SignalR real-time PUSH (FR-2/NFR-2) is DEFERRED on the Notification/real-time infrastructure (US-NTF); the polling fallback (every 30s, per §10) is verified now.

## 2. Related Requirements
- User Story: US-ATT-010
- Acceptance Criteria: AC-2 (real-time list of all employees with current status: Clocked In, Not Clocked In, On Leave, Holiday)
- Functional Requirements: FR-2 (live board updates in real-time via SignalR on clock-in/out)
- Non-Functional: NFR-2 (board reflects clock-in/out within 3s via SignalR -- DEFERRED, polling fallback)
- Business Rules: BR-3 (board shows only employees the caller may view -- all for Attendance.Read.All)
- Assumptions: §10 (SignalR; on WebSocket failure, UI falls back to 30s polling)
- API: GET /api/v1/attendance/dashboard/live-board

## 3. Preconditions
- Tenant "acme"; HR Officer "Priya" authenticated with `Attendance.Read.All`.
- Today: employee Asha clocked in at 09:05; Ben has not clocked in; Carol is on full-day approved leave; Dan is at a location where today is a public holiday.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Asha | clocked in 09:05 | status CLOCKED_IN |
| Ben | no record | status NOT_CLOCKED_IN |
| Carol | full-day leave | status ON_LEAVE |
| Dan | holiday location | status HOLIDAY |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Priya, `GET /api/v1/attendance/dashboard/live-board` | 200 OK; one row per viewable employee with name/avatar reference, status, and (for clocked-in) the clock-in time. |
| 2 | Verify Asha | status = CLOCKED_IN, clock_in_time = 09:05 (employee-local display; UTC stored). |
| 3 | Verify Ben | status = NOT_CLOCKED_IN, no clock-in time. |
| 4 | Verify Carol | status = ON_LEAVE (full-day approved leave today). |
| 5 | Verify Dan | status = HOLIDAY (location public holiday today). |
| 6 | Ben clocks in at 10:00, re-request the live board | Ben's status flips to CLOCKED_IN with clock_in_time = 10:00 -- the board reflects the new clock-in on refresh (polling fallback path). |
| 7 | SignalR real-time push (FR-2/NFR-2) | DEFERRED on US-NTF / real-time infra: the live-update SEAM (clock-in/out raises an event the board consumes, target = the tenant's HR dashboard group, tenant-scoped) is documented; end-to-end "second browser session updates within 3s" is verified once SignalR lands. The 30s polling fallback (§10) is verified now (Step 6). **Reported to caller.** |
| 8 | Board scope (BR-3) | Only employees the caller may view are listed (all-tenant for Attendance.Read.All; manager team-scope verified in TC-ATT-137). |

## 6. Postconditions
- The live board accurately classifies each viewable employee's current-day status and refreshes on clock-in/out via the polling fallback.

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
- **SignalR real-time push (FR-2 / NFR-2) DEFERRED:** the real-time/SignalR infrastructure is not built; consistent with how the module's notification/real-time dispatch is treated as a verified SEAM with delivery deferred on US-NTF (mirrors US-ATT-003 TC-ATT-032 / US-ATT-008 TC-ATT-109). The polling fallback (30s, §10) is verified live; the <3s real-time SLA (NFR-2) is re-checked when SignalR lands. **Reported to caller.**
- HOLIDAY status depends on the US-LV-007 holiday-source integration (location -> today-is-holiday); CONDITIONAL on that integration. The CLOCKED_IN / NOT_CLOCKED_IN / ON_LEAVE classifications are verified independently. **Reported to caller.**
- Live-board responsive card layout + row-highlight animation a11y in TC-ATT-141; tenant isolation in TC-ATT-ISO-013; live-board <3s perf (SignalR) in TC-ATT-139.
