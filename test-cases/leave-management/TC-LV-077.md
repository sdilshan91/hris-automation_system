---
id: TC-LV-077
user_story: US-LV-004
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-077: Clicking a request opens the detail panel with full details, attachments, balance, history, and team-calendar

## 1. Test Objective
Verify that clicking a pending request opens a detail panel showing the full request: employee photo, leave type with color tag, date range, total days, reason, downloadable attachments, current balance, a leave history summary (last 3 leaves), and a team-calendar snippet of who else is off during the requested period. Subsections that depend on unbuilt modules are noted, not silently passed.

## 2. Related Requirements
- User Story: US-LV-004
- Acceptance Criteria: AC-4
- Functional Requirements: FR-2, FR-5
- Dependencies: leave-history summary and team-calendar snippet (US-LV-009 team calendar)

## 3. Preconditions
- Tenant "acme" is active.
- Manager "Robert Lee" is authenticated with `Leave.Approve.Team`.
- Direct report "Jane Smith" has a pending Annual Leave request 2026-07-06..07-08 with one PDF attachment.
- Jane has at least 3 prior approved leaves (for the history summary).
- Another team member is approved off overlapping the requested period (for the team-calendar snippet).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Request | Jane Smith, Annual Leave, 2026-07-06..07-08, 3 days | Pending |
| Attachment | medical-note.pdf | Downloadable |
| Current balance (Annual) | 11.00 days | Inline |
| Prior leaves | 3 most recent | History summary |
| Overlapping teammate | Alan Park, 2026-07-07..07-09 (approved) | Team calendar |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click Jane's pending request row | A detail panel slides in from the right (Notion page-peek style). |
| 2 | Verify core details | Panel shows employee photo, leave type with color tag, date range 2026-07-06..07-08, total days 3, and the reason text (FR-2). |
| 3 | Verify attachments | The `medical-note.pdf` attachment is listed and downloadable via a tenant-scoped signed/authorized link; download succeeds for the manager. |
| 4 | Verify current balance | The panel shows Jane's current Annual Leave balance (11.00), consistent with the inline pill. |
| 5 | Verify leave history summary (last 3 leaves) | If the history endpoint is available, the 3 most recent leaves are listed. If leave-history aggregation is not yet implemented, this subsection is marked DEFERRED (dependent on leave-history/US-LV-009) -- documented, NOT silently passed. |
| 6 | Verify team-calendar snippet | If the team calendar (US-LV-009) is available, the snippet shows Alan Park off during the overlapping period. If US-LV-009 is not implemented, this subsection is marked DEFERRED (dependent on US-LV-009) while the conflict-count from FR-5 (see TC-LV-078) still renders. |
| 7 | Close the panel | The panel dismisses; the queue remains in its prior scroll/filter state. |

## 6. Postconditions
- No data mutated.
- The detail panel renders all built subsections; history/team-calendar subsections are explicitly flagged deferred if their source modules are not yet implemented.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
