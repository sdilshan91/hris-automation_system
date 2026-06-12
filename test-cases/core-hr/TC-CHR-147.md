---
id: TC-CHR-147
user_story: US-CHR-003
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-147: Responsive grid reflow from 4 columns to 1 column (NFR-4)

## 1. Test Objective
Verify that the Employee Directory card grid is fully responsive, reflowing from 4 columns on desktop (>= 1200px) to 2 columns on tablet (768px-1199px) to 1 column on mobile (< 768px). On mobile, card view is the default. This validates NFR-4.

## 2. Related Requirements
- User Story: US-CHR-003
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- 25 employees exist.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Desktop width | 1920px | 4 columns |
| Tablet width | 768px | 2 columns |
| Mobile width | 360px | 1 column |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Employee Directory at 1920px viewport width | Card grid displays in 4 columns. All cards visible without horizontal scrolling. |
| 2 | Resize viewport to 1200px | Grid remains at 4 columns (minimum desktop width). |
| 3 | Resize viewport to 1024px | Grid reflows to 2 columns (tablet breakpoint). |
| 4 | Resize viewport to 768px | Grid displays 2 columns. Card view is the default. |
| 5 | Resize viewport to 360px | Grid reflows to 1 column. Each card takes full width. No horizontal overflow. |
| 6 | Verify search bar at 360px | Search bar is full-width; filter button stacks below or becomes a compact icon. |
| 7 | Verify pagination at 360px | Pagination controls are usable; page numbers may collapse to "< 1 ... 3 >" format. |
| 8 | Verify view toggle at mobile | On mobile (< 768px), card view is the default; table/list view is accessible but compact. |
| 9 | Verify no content truncation | Employee names, departments, and status badges are fully readable at all breakpoints. |

## 6. Postconditions
- No data was modified.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
