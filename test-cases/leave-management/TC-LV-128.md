---
id: TC-LV-128
user_story: US-LV-006
module: Leave Management
priority: high
type: accessibility
status: draft
created: 2026-06-14
---

# TC-LV-128: WCAG 2.1 AA -- progress bars have aria-labels and color is not the sole indicator

## 1. Test Objective
Verify that the dashboard meets WCAG 2.1 AA: progress bars expose accessible names/values (aria-label / role=progressbar with aria-valuenow/min/max), text values are always visible so color is never the sole indicator, and the dashboard is keyboard- and screen-reader-navigable across target browsers (NFR-4, Section 8).

## 2. Related Requirements
- User Story: US-LV-006
- Non-Functional Requirements: NFR-4
- UI/UX Notes (Section 8)

## 3. Preconditions
- Tenant "acme" active; employee "Nina Patel" authenticated with multiple active leave types and ledger data.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Standard | WCAG 2.1 AA | -- |
| Browsers | Chrome, Edge, Firefox, Safari | Cross-browser |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Inspect each card's progress indicator | Exposes `role="progressbar"` (or equivalent) with an accessible name and aria-valuenow/valuemin/valuemax (or an aria-label conveying "X of Y days used"). |
| 2 | Verify text-not-color | Numeric entitlement/used/pending/balance and ledger type labels are always shown as text; meaning is not conveyed by color alone; contrast meets AA (>= 4.5:1 for text). |
| 3 | Navigate the dashboard, year selector, and card -> ledger with keyboard only | All interactive elements are reachable/operable via Tab/Enter/Space/arrows with a visible focus ring; the ledger opens and closes via keyboard. |
| 4 | Run an automated a11y audit (axe) and a screen-reader pass in each target browser | No critical violations; cards, progress bars, badges, and the empty state are announced meaningfully across Chrome, Edge, Firefox, and Safari. |

## 6. Postconditions
- Dashboard satisfies WCAG 2.1 AA with accessible progress bars and text-based (non-color-only) indicators across browsers.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [x] Cross-browser test
