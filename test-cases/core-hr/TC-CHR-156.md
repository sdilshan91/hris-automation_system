---
id: TC-CHR-156
user_story: US-CHR-006
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-156: Expand and collapse tree nodes with smooth animation

## 1. Test Objective
Verify that tree nodes support expand/collapse interactions with smooth 200ms ease animation, and that the expand/collapse state is consistent across multiple interactions. This validates FR-2 and UI/UX requirements.

## 2. Related Requirements
- User Story: US-CHR-006
- Functional Requirements: FR-2
- UI/UX Notes: 200ms ease animation

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant context.
- Department hierarchy: "Corp" (root) with two children "Dept A" and "Dept B". "Dept A" has child "Team 1". "Dept B" has `children_count: 0`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Root | Corp | Has 2 children |
| Child 1 | Dept A | Has child "Team 1" |
| Child 2 | Dept B | Leaf node, no children |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Organization Tree page | "Corp", "Dept A", and "Dept B" are visible (top 2 levels). |
| 2 | Click collapse on "Corp" node | "Dept A" and "Dept B" slide up/fade with a smooth animation (~200ms). After animation, only "Corp" is visible with a collapsed indicator. |
| 3 | Click expand on "Corp" node | "Dept A" and "Dept B" reappear with a smooth expand animation (~200ms ease). |
| 4 | Verify "Dept A" shows expand indicator | Expand chevron/icon is present since it has children. |
| 5 | Verify "Dept B" does NOT show expand indicator | No expand control because `children_count: 0`. |
| 6 | Rapidly click expand/collapse on "Corp" three times | Animations queue correctly without visual glitches or layout jumps; final state matches the expected toggle state (expanded or collapsed based on odd/even clicks). |

## 6. Postconditions
- No data was modified.
- Node states are visually consistent after rapid interaction.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
