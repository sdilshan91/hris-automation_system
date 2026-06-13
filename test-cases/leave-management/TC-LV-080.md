---
id: TC-LV-080
user_story: US-LV-004
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-080: Inline balance pill matches the employee's current balance with correct color thresholds

## 1. Test Objective
Verify that the inline balance pill shown for each pending request reflects the employee's current real-time balance for that leave type (from the LeaveLedger running total; Redis cache is deferred) and is color-coded by remaining proportion: green > 50%, yellow 20--50%, red < 20% of entitlement. (Test Hint: verify the displayed balance matches the employee's actual cached balance -- DB-fallback path here.)

## 2. Related Requirements
- User Story: US-LV-004
- Acceptance Criteria: AC-1
- Functional Requirements: FR-2
- Non-Functional Requirements: NFR-2 (Redis-cached balance -- DEFERRED; DB-fallback verified)
- Business Rules: BR-4
- UI/UX Notes: Section 8 (balance pill color thresholds)

## 3. Preconditions
- Tenant "acme" is active.
- Manager "Robert Lee" is authenticated with `Leave.Approve.Team`.
- Three direct reports have known entitlements and current balances producing each color band.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Jane (Annual) | entitlement 14, remaining 11 (79%) | Green (> 50%) |
| Alan (Annual) | entitlement 14, remaining 5 (36%) | Yellow (20--50%) |
| Priya (Annual) | entitlement 14, remaining 2 (14%) | Red (< 20%) |
| Balance source | LeaveLedger running total | Redis cache deferred |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the pending queue | Each request shows an inline balance pill for the requested leave type. |
| 2 | Verify Jane's pill | Shows 11.00 days and is green (remaining > 50% of entitlement). |
| 3 | Verify Alan's pill | Shows 5.00 days and is yellow (20--50%). |
| 4 | Verify Priya's pill | Shows 2.00 days and is red (< 20%). |
| 5 | Verify the value source | The displayed balance equals the employee's current balance computed from the LeaveLedger running total (NFR-2 Redis cache is DEFERRED; the DB-fallback value is authoritative here). |
| 6 | Verify real-time (not snapshot) | The balance is the current balance, not the balance at request time (BR-4) -- adjusting the ledger and reloading updates the pill. |
| 7 | Verify non-color cue | The pill also carries a numeric value/label so the band is not conveyed by color alone (accessibility). |

## 6. Postconditions
- No data mutated.
- Inline balance pills are accurate and correctly color-banded with a non-color cue.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
