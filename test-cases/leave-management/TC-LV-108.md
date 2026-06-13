---
id: TC-LV-108
user_story: US-LV-005
module: Leave Management
priority: medium
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-108: Cross-browser compatibility for the approve/reject flow (Chrome, Edge, Firefox, Safari)

## 1. Test Objective
Verify that the approve/reject flow -- detail panel, expanding comment/reason textareas, the insufficient-balance confirmation modal, the slide-out animation, and the success toast -- works consistently across Chrome, Edge, Firefox, and Safari at desktop and mobile widths.

## 2. Related Requirements
- User Story: US-LV-005
- UI/UX Notes (Section 8)
- Non-Functional Requirements: NFR-1 (responsiveness implied via UI behavior)

## 3. Preconditions
- Tenant "acme" is active; Manager authenticated with `Leave.Approve.Team`.
- A pending request detail panel is reachable.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Browsers | Chrome, Edge, Firefox, Safari | Latest stable |
| Widths | 360px, 1280px | Mobile + desktop |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | In each browser, open a pending request and click Approve | The optional comment textarea expands; approval submits and the request slides out of the queue with the toast shown -- identical behavior across browsers. |
| 2 | In each browser, click Reject and enter a reason | The mandatory reason textarea appears; rejection submits correctly. |
| 3 | Trigger the insufficient-balance confirmation modal (negative-allowed type) | The modal renders and confirm/cancel work in every browser. |
| 4 | Repeat at 360px in each browser | Full-width bottom buttons and animations render correctly with no layout breakage. |

## 6. Postconditions
- Approve/reject flow behaves consistently across all four target browsers and both widths.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
