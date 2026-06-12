---
id: TC-CHR-164
user_story: US-CHR-006
module: Core HR
priority: critical
type: performance
status: draft
created: 2026-06-12
---

# TC-CHR-164: Initial top-2-level tree render completes within 2.5 seconds P95

## 1. Test Objective
Verify that the initial org tree render (top 2 levels) completes within 2.5 seconds at the 95th percentile. This validates NFR-1.

## 2. Related Requirements
- User Story: US-CHR-006
- Non-Functional Requirements: NFR-1
- Acceptance Criteria: AC-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant context.
- Org chart has 50 departments across the top 2 levels (e.g., 5 root departments, each with 9 children).
- Test environment is a standard modern browser on a mid-range device or simulated 4G network throttling.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Top 2 Level Nodes | 50 departments | 5 root + 45 children |
| Performance Target | <= 2.5 seconds P95 | NFR-1 |
| Measurement | Time from navigation to interactive tree render | First Contentful Paint to Last Node Rendered |
| Test Runs | 20 iterations | For P95 calculation |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Clear browser cache and navigate to the Organization Tree page | Page load begins. |
| 2 | Start a performance timer at navigation start | Timer starts. |
| 3 | Wait until all top-2-level nodes are rendered and interactive | All 50 department nodes are visible, connector lines are drawn, and nodes are clickable. |
| 4 | Stop the performance timer | Total time recorded. |
| 5 | Repeat steps 1-4 for 20 iterations | Collect 20 timing measurements. |
| 6 | Calculate the P95 value from the 20 measurements | Sort measurements; the 95th percentile value (19th out of 20 sorted) must be <= 2.5 seconds. |
| 7 | Verify that no browser freeze or unresponsive dialog appeared during any iteration | The UI remained responsive throughout all 20 loads. |

## 6. Postconditions
- Performance data is recorded for analysis.
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
