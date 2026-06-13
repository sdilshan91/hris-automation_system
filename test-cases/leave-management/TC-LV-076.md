---
id: TC-LV-076
user_story: US-LV-004
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-076: Overdue highlight boundary -- a 31-day-old pending request is flagged overdue, a 29-day-old is not

## 1. Test Objective
Verify that pending requests older than 30 days (without action) are flagged as overdue with a visual highlight, and that a request younger than 30 days is not, exercising the 30-day boundary. (Test Hint: create a request 31 days old; verify it is flagged overdue.)

## 2. Related Requirements
- User Story: US-LV-004
- Acceptance Criteria: AC-1
- Business Rules: BR-3
- UI/UX Notes: Section 8 (overdue red left-border highlight)

## 3. Preconditions
- Tenant "acme" is active.
- Manager "Robert Lee" is authenticated with `Leave.Approve.Team`.
- "Today" is fixed at 2026-06-13 for deterministic age calculation.
- Robert's team has three pending requests with controlled `requestedAt` ages.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Request P31 | requestedAt = 2026-05-13 (31 days old) | Overdue |
| Request P30 | requestedAt = 2026-05-14 (30 days old) | Boundary -- exactly 30 days, not yet overdue |
| Request P29 | requestedAt = 2026-05-15 (29 days old) | Not overdue |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the pending queue | All three requests appear. |
| 2 | Inspect Request P31 (31 days old) | It is flagged overdue: a subtle red left-border highlight and/or an "Overdue" indicator is present (BR-3, Section 8). |
| 3 | Inspect Request P29 (29 days old) | It is NOT flagged overdue; no overdue indicator. |
| 4 | Inspect Request P30 (exactly 30 days old) | Treated as the boundary -- "older than 30 days" means strictly > 30, so P30 is NOT flagged overdue. |
| 5 | Verify the overdue cue is not color-only | The overdue state is conveyed by an icon/label in addition to the red border (accessibility -- non-color cue). |
| 6 | Verify the flag is derived, not stored mutably | Overdue status is computed from `requestedAt` vs current date, so it changes as time passes without a write. |

## 6. Postconditions
- No data mutated.
- Overdue highlighting correctly distinguishes >30-day requests at the 29/30/31-day boundary.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
