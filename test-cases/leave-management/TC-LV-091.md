---
id: TC-LV-091
user_story: US-LV-005
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-091: Optional approval comment is persisted in leave_approval_history; approval succeeds without a comment

## 1. Test Objective
Verify that on approval the optional comment is stored in `leave_approval_history` when supplied, and that approval still succeeds when no comment is supplied (BR-2: approval comment is optional, rejection reason is mandatory).

## 2. Related Requirements
- User Story: US-LV-005
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-5
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" is active; Manager "Robert Lee" authenticated with `Leave.Approve.Team`.
- Two pending requests from direct reports exist (Request A, Request B), each `Pending`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Request A | Pending, comment supplied | "Approved with conditions" |
| Request B | Pending, no comment | Empty/omitted comment body |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Approve Request A with a non-empty comment | 200; `leave_approval_history` row has `action = Approved` and `comment` = the supplied text. |
| 2 | Approve Request B with the comment omitted/empty | 200; approval succeeds (comment optional, BR-2); `leave_approval_history` row has `action = Approved` and `comment` null/empty. |
| 3 | Verify approval-level tracking | Each history row records `approval_level` (1 for single-level). |
| 4 | Confirm both requests are Approved | Both transition to `status = Approved` with `used` ledger entries created. |

## 6. Postconditions
- Both requests Approved; approval-history rows persisted with/without comment.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
