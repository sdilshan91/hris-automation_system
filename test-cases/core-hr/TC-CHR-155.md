---
id: TC-CHR-155
user_story: US-CHR-006
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-155: Lazy loading -- only top 2 levels load initially; expanding a node triggers API call for children

## 1. Test Objective
Verify that for a hierarchy with 4+ levels, only the top 2 levels are loaded initially. Expanding a collapsed node triggers an API call to fetch its children on demand, confirming lazy loading behavior. This validates AC-5, FR-6, NFR-1.

## 2. Related Requirements
- User Story: US-CHR-006
- Acceptance Criteria: AC-5
- Functional Requirements: FR-6, FR-2
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant context.
- 5-level department hierarchy: "Corp" (L1) -> "Division A" (L2) -> "Team Alpha" (L3) -> "Squad 1" (L4) -> "Cell X" (L5).
- "Division A" has `children_count: 1` in the initial response.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Level 1 | Corp | Root department |
| Level 2 | Division A | children_count: 1 |
| Level 3 | Team Alpha | Not loaded initially |
| Level 4 | Squad 1 | Not loaded initially |
| Level 5 | Cell X | Not loaded initially |
| Initial API | GET /api/v1/org-tree?view=department&depth=2 | Top 2 levels only |
| Expand API | GET /api/v1/org-tree?view=department&parentId={divisionA-id}&depth=1 | On expand |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Organization Tree page | Org chart renders. |
| 2 | Verify initial API call includes `depth=2` | `GET /api/v1/org-tree?view=department&depth=2` returns "Corp" (L1) and "Division A" (L2). |
| 3 | Verify "Corp" and "Division A" are visible | Both nodes are rendered with connector lines. |
| 4 | Verify "Division A" shows an expand indicator | An expand icon/chevron is displayed on "Division A" indicating it has children (`children_count > 0`). |
| 5 | Verify "Team Alpha" (L3) is NOT rendered | No node for "Team Alpha" exists in the DOM. |
| 6 | Click the expand indicator on "Division A" | A loading spinner appears briefly on the node. |
| 7 | Verify API call for children | `GET /api/v1/org-tree?view=department&parentId={divisionA-id}&depth=1` is sent. Response status 200 OK. |
| 8 | Verify "Team Alpha" appears as a child of "Division A" | "Team Alpha" node is rendered below "Division A" with a connector line. Smooth expand animation (200ms ease) plays. |
| 9 | Verify "Team Alpha" also has an expand indicator | Since "Team Alpha" has children (`children_count > 0`), an expand icon is shown. |
| 10 | Click expand on "Team Alpha" | API call fetches "Squad 1"; it appears with expand indicator. |
| 11 | Click expand on "Squad 1" | API call fetches "Cell X"; it appears. "Cell X" has no expand indicator (`children_count: 0`). |
| 12 | Collapse "Division A" | All descendants (Team Alpha, Squad 1, Cell X) are hidden with collapse animation. |
| 13 | Re-expand "Division A" | Previously loaded children reappear without a new API call (cached client-side). |

## 6. Postconditions
- No data was modified.
- Lazy-loaded children are cached client-side for the session.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
