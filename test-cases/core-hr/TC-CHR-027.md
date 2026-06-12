---
id: TC-CHR-027
user_story: US-CHR-004
module: Core HR
priority: high
type: performance
status: draft
created: 2026-06-11
---

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
