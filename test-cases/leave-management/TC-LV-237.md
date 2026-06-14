---
id: TC-LV-237
user_story: US-LV-012
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-237: Absenteeism flag — 4 unplanned leaves vs threshold 3 → flagged; threshold is tenant-configurable (AC-3, BR-4, Test Hint)

## 1. Test Objective
Verify the AC-3 Test Hint and BR-4: an employee with 4 unplanned leaves in a month, against the default threshold of 3, is flagged; and that raising the tenant-configurable threshold re-evaluates the flag (boundary at exactly-threshold).

## 2. Related Requirements
- User Story: US-LV-012
- Acceptance Criteria: AC-3
- Business Rules: BR-4 (default 3+ unplanned/month, tenant-configurable)

## 3. Preconditions
- Tenant "acme"; employee "Mark Otieno" with exactly 4 unplanned leaves in the report month; tenant absenteeism threshold = 3 (default).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Unplanned count | 4 | for Mark, in-month |
| Threshold | 3 (default) | tenant-configurable |
| Boundary count | 3 | exactly-at-threshold case |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run the Absenteeism Report for the month with threshold = 3 | Mark (4 unplanned) is FLAGGED as over threshold. |
| 2 | Boundary: an employee with exactly 3 unplanned | "3+" is over-or-equal → flagged per BR-4 wording ("3+ unplanned"); an employee with 2 is NOT flagged. |
| 3 | Raise the tenant threshold to 5 and re-run | Mark (4) is NO LONGER flagged; flagging follows the tenant-configured threshold, not a hard-coded value. |
| 4 | Confirm threshold scope | Changing tenant A's threshold does not affect tenant B's flagging (per-tenant config). |

## 6. Postconditions
- Over-threshold absentees are flagged using the tenant-configurable threshold (4>3 flagged; threshold change re-evaluates).

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
