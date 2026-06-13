---
id: TC-LV-127
user_story: US-LV-006
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-127: Mobile 360px -- balance cards stack, remain readable, and progress bars scale

## 1. Test Objective
Verify that on a 360px-wide mobile viewport the dashboard balance cards stack vertically (one per row), remain readable, and the progress bars scale correctly without overflow or truncation (AC-4, NFR-2, Section 8).

## 2. Related Requirements
- User Story: US-LV-006
- Acceptance Criteria: AC-4
- Non-Functional Requirements: NFR-2
- UI/UX Notes (Section 8): 1 card per row on mobile

## 3. Preconditions
- Tenant "acme" active; employee "Nina Patel" authenticated with multiple active leave types.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewport | 360px wide | Mobile |
| Cards | >= 3 | Annual, Sick, Casual |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the dashboard at a 360px viewport | All balance cards stack vertically, one per row; no horizontal scroll. |
| 2 | Inspect each card | Numeric values (entitlement/used/pending/balance) and the type name remain legible and untruncated; the progress bar scales to the card width. |
| 3 | Open the Upcoming Leaves and year-selector | The timeline list and the pill-group remain usable and tappable (touch targets adequate). |
| 4 | Tap a card to open the ledger | The ledger/transaction view is readable and horizontally contained at 360px (e.g., responsive table or stacked rows). |

## 6. Postconditions
- Dashboard is fully usable and readable at 360px with correctly scaled progress bars.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
