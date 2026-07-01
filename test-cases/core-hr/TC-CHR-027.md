---
id: TC-CHR-027
user_story: US-CHR-004
module: Core HR
priority: high
type: performance
status: blocked
created: 2026-06-11
exec_note: "P3b k6 2026-06-30: NOT MEASURABLE BY k6 — kept blocked. Target is FE page-load FCP<=1.5s / TTI<=2.5s (browser render of the Departments page), not a server API latency. k6 only covers server-side. Needs a Chrome DevTools FE perf trace (LCP/TTI), not k6. (For reference, the departments LIST API read measured 57–70ms p95 @50VU on 5k perf tenant.)"

# TC-CHR-027: Department page load within 2.5 seconds

## 1. Test Objective
Verify that the Department management page (list view and tree view) loads within 2.5 seconds end-to-end (including API call, rendering, and tree construction) as per standard performance requirements.

## 2. Related Requirements
- User Story: US-CHR-004
- Non-Functional Requirements: NFR-1
- Functional Requirements: FR-8

## 3. Preconditions
- Tenant "acme" exists with 200 departments (simulating a medium-sized organization).
- A user with Tenant Admin role is authenticated.
- Browser: Chrome (latest stable) on a standard machine.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Department Count | 200 | Moderate dataset |
| Network | No throttling | Standard conditions |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Departments management page and measure time from navigation start to First Contentful Paint (FCP) | FCP <= 1.5 seconds. |
| 2 | Measure time from navigation start to full page interactive (Time to Interactive, TTI) | TTI <= 2.5 seconds. |
| 3 | Toggle to tree view and measure rendering time | Tree renders (all root nodes visible) within 1 second after toggle click. |
| 4 | Expand a root node with 20+ children and measure expansion time | Children render within 500ms. |
| 5 | Repeat with 500 departments (NFR-4 upper bound) | Page still loads within 2.5 seconds. |

## 6. Postconditions
- Performance metrics are documented.
- Page remains usable with up to 500 departments.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

> **Execution 2026-06-30 (FE, acme):** STILL BLOCKED — performance/SLA TC. The Departments page renders ~51 cards promptly in-browser, but this TC requires instrumented page-load timing (P95 / 500-dept scale seed) that the single shared Playwright browser cannot measure reliably. Not a functional defect. Needs perf harness + seeded scale data.
> **Re-exec 2026-06-30 (deep FE-render pass, CDP):** STILL BLOCKED. Steps 1–2 (FCP≤1.5s / TTI≤2.5s) are **cold-load navigation** metrics — a true cold load needs a full navigation/reload which logs you out (**BUG-097**); not cleanly measurable until BUG-097 is fixed. Steps 3–5 (tree-view toggle render, node-expand, 500-dept) need the **200/500-dept scale seed** acme lacks (acme has ~51 depts). For reference, a CDP soft-nav (route → Departments) render trace recorded **CLS 0.00, no jank, sub-frame render of all 51 cards** — i.e. the component-render is fast, but that soft-nav render is a different metric than this TC's cold-load FCP/TTI-at-scale. No pass arm available.
