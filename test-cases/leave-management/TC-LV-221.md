---
id: TC-LV-221
user_story: US-LV-011
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-221: HR can override a System-Generated LOP entry — convert to another leave type or remove it (BR-3)

## 1. Test Objective
Verify BR-3: a system-generated LOP entry can be overridden by HR. When an employee later provides a valid reason, HR can either (a) convert the LOP entry to a different leave type (deducting that type's balance) or (b) remove the LOP entry entirely, restoring the day to a non-LOP, non-deducted state.

## 2. Related Requirements
- User Story: US-LV-011
- Business Rules: BR-3
- Acceptance Criteria: AC-2 (overriding a System-Generated entry)
- Test Hint §11 (override)

## 3. Preconditions
- Tenant "acme"; employee "Mark Otieno" has one System-Generated LOP entry for a day.
- HR Officer "Asha" authenticated with `Leave.Manage`/`HR.Officer`.
- A "Casual Leave" type with available balance exists for Mark.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| LOP entry | 1 day, System-Generated | to override |
| Convert to | Casual Leave | balance available |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Asha converts the LOP entry to Casual Leave (with reason) | The LOP entry is replaced/converted: the request's type becomes Casual Leave, `is_lop` is cleared, and 1 day is deducted from Mark's Casual balance (a `used` ledger entry). |
| 2 | Re-query lop-summary for the period | The LOP day count drops by 1 (the converted day is no longer LOP) — so payroll will not deduct for it. |
| 3 | (Alternate) On a second System-Generated LOP entry, choose "remove" | The LOP entry is removed; no balance is deducted from any type; lop-summary count drops accordingly. |
| 4 | Verify audit | Both the convert and remove actions are audit-logged with actor = Asha and before/after state (NFR-4). |

## 6. Postconditions
- A System-Generated LOP entry can be converted to another leave type (balance deducted) or removed (no deduction); lop-summary updates so payroll reflects the override.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
