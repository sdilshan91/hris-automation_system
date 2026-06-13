---
id: TC-LV-126
user_story: US-LV-006
module: Leave Management
priority: high
type: performance
status: draft
created: 2026-06-14
---

# TC-LV-126: Dashboard achieves Largest Contentful Paint (LCP) under 2.5 seconds

## 1. Test Objective
Verify that the Leave Balance Dashboard page meets the LCP < 2.5s target, using loading skeletons while data fetches so the largest contentful element paints within budget (NFR-2).

## 2. Related Requirements
- User Story: US-LV-006
- Non-Functional Requirements: NFR-2
- UI/UX Notes (Section 8): loading skeleton animations

## 3. Preconditions
- Tenant "acme" active; employee "Nina Patel" authenticated with a representative number of active leave types.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Target | LCP < 2.5s | NFR-2 |
| Network | typical broadband + mid-tier device profile | Lighthouse/RUM |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the dashboard with a cold cache and measure LCP (Lighthouse / web-vitals) | LCP is under 2.5 seconds. |
| 2 | Observe the loading phase | Skeleton placeholders render immediately for the card grid while `my-balance`/`my-upcoming` resolve. |
| 3 | Verify the largest element paints within budget | The largest contentful element (card grid / hero) renders within the LCP budget without layout shift spikes. |
| 4 | Repeat on a warm load | LCP remains within budget and is typically faster. |

## 6. Postconditions
- Dashboard meets the LCP performance budget with a skeleton-first load.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
