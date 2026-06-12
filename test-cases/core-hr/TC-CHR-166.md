---
id: TC-CHR-166
user_story: US-CHR-006
module: Core HR
priority: high
type: accessibility
status: draft
created: 2026-06-12
---

# TC-CHR-166: WCAG 2.1 AA keyboard arrow-key navigation and screen reader announces node label and level

## 1. Test Objective
Verify that the org tree meets WCAG 2.1 AA accessibility standards: tree nodes are navigable via keyboard arrow keys, and a screen reader announces the node label and hierarchy level for each focused node. This validates NFR-5.

## 2. Related Requirements
- User Story: US-CHR-006
- Non-Functional Requirements: NFR-5
- Functional Requirements: FR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant context.
- Org chart rendered with: "Corp" (root, L1) -> "Engineering" (L2), "Sales" (L2) -> "Backend" (L3).
- Screen reader enabled (e.g., NVDA on Windows, VoiceOver on macOS).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Root Node | Corp | Level 1 |
| Children | Engineering, Sales | Level 2 |
| Grandchild | Backend (child of Engineering) | Level 3 |
| Screen Reader | NVDA or VoiceOver | Accessibility testing |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Organization Tree page using keyboard (Tab) | Focus reaches the org tree container; the tree widget is announced by the screen reader as a tree or similar landmark. |
| 2 | Verify the tree has `role="tree"` or equivalent ARIA role | The tree container element has `role="tree"`. Each node has `role="treeitem"`. |
| 3 | Press Down Arrow key | Focus moves to the first root node "Corp". Screen reader announces "Corp, level 1" (or equivalent with department name and hierarchy level). |
| 4 | Press Right Arrow key on "Corp" | "Corp" node expands to reveal children. Screen reader announces "expanded". |
| 5 | Press Down Arrow key | Focus moves to "Engineering" (first child). Screen reader announces "Engineering, level 2". |
| 6 | Press Down Arrow key again | Focus moves to "Sales" (sibling). Screen reader announces "Sales, level 2". |
| 7 | Press Up Arrow key | Focus returns to "Engineering". |
| 8 | Press Right Arrow key on "Engineering" | "Engineering" expands to reveal "Backend". Screen reader announces "expanded". |
| 9 | Press Down Arrow key | Focus moves to "Backend". Screen reader announces "Backend, level 3". |
| 10 | Press Left Arrow key on "Backend" | Focus moves to parent "Engineering". |
| 11 | Press Left Arrow key on "Engineering" | "Engineering" collapses. Screen reader announces "collapsed". |
| 12 | Press Enter or Space on a focused node | The detail panel opens for that node (per AC-2). |
| 13 | Verify color contrast of node cards | Node text meets WCAG AA contrast ratio (>= 4.5:1 for normal text, >= 3:1 for large text) against the card background. |
| 14 | Verify focus indicator visibility | A visible focus ring/outline is displayed on the currently focused node, distinguishable from the default state. |

## 6. Postconditions
- No data was modified.
- Keyboard navigation state is consistent.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [ ] Cross-browser test
