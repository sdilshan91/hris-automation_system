---
id: TC-CHR-165
user_story: US-CHR-006
module: Core HR
priority: critical
type: performance
status: draft
created: 2026-06-12
---

# TC-CHR-165: 200-node tree maintains smooth pan/zoom at approximately 60fps

## 1. Test Objective
Verify that a tree with 200 visible nodes maintains smooth pan and zoom interactions at approximately 60 frames per second without jank, dropped frames, or browser freezing. This validates NFR-2 and AC-5.

## 2. Related Requirements
- User Story: US-CHR-006
- Acceptance Criteria: AC-5
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant context.
- Org chart with 200 nodes fully expanded and visible (departments + employees across multiple levels).
- Modern browser on a mid-range machine (e.g., Intel i5, 8GB RAM, integrated GPU).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Total Visible Nodes | 200 | All expanded |
| Performance Target | ~60fps during pan/zoom | NFR-2 |
| Browser | Chrome latest | DevTools Performance panel available |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the org tree and expand nodes until 200 are visible | Tree renders with 200 node cards and connector lines. |
| 2 | Open Chrome DevTools Performance panel and start recording | Recording begins. |
| 3 | Perform continuous mouse-drag panning across the canvas for 5 seconds | The canvas pans smoothly without visible jank or stuttering. |
| 4 | Perform continuous scroll-wheel zooming (in and out) for 5 seconds | The zoom animation is smooth without dropped frames. |
| 5 | Stop the Performance recording | Timeline is captured. |
| 6 | Analyze the DevTools Performance timeline for frame rate | Majority of frames are at or near 16.67ms (60fps). No frames exceed 50ms (severe jank). P95 frame time is <= 20ms. |
| 7 | Verify no "Long Task" warnings exceeding 100ms during pan/zoom | No long tasks in the DevTools timeline during the interaction periods. |
| 8 | Verify browser did not display an "Unresponsive page" dialog | No freezes occurred. |

## 6. Postconditions
- Performance recording data is available for analysis.
- No data was modified.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
