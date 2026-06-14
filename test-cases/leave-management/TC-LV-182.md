---
id: TC-LV-182
user_story: US-LV-009
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-182: Date-range parameters (from/to) and boundary handling (FR-1)

## 1. Test Objective
Verify the calendar API honors the `from`/`to` date-range parameters, includes leaves that partially overlap the window boundaries, and handles invalid/empty/inverted ranges safely.

## 2. Related Requirements
- User Story: US-LV-009
- Functional Requirements: FR-1, FR-4
- Data Requirements: Section 7 (index leave_request(tenant_id, employee_id, status, start_date))

## 3. Preconditions
- Tenant "acme"; Manager "Maya"; a direct report has a leave 2026-05-29..2026-06-02 (spans the month boundary).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Window | from=2026-06-01&to=2026-06-30 | June |
| Overlapping leave | 2026-05-29..06-02 | partial overlap at start |
| Inverted | from=2026-06-30&to=2026-06-01 | invalid range |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Request June with the overlapping leave | The leave appears, clipped to the visible window (06-01..06-02 portion shown), since it overlaps the range. |
| 2 | Request a single-day window (from=to=2026-06-11) | Only leaves overlapping that day are returned. |
| 3 | Send an inverted range (from > to) | The API returns 400 (or an empty, well-formed result) -- not a 500 or an unbounded scan. |
| 4 | Omit `from`/`to` or send a > 1-year span | The API applies a sane default range or rejects an excessive span (no unbounded query). |

## 6. Postconditions
- Date-range filtering is correct at boundaries; invalid ranges are handled safely.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
