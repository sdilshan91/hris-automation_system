---
id: TC-CHR-158
user_story: US-CHR-006
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-158: Export org chart as PNG contains visible tree structure

## 1. Test Objective
Verify that exporting the org chart as PNG produces an image that faithfully represents the visible tree structure, including node cards, connector lines, and labels. This validates FR-7.

## 2. Related Requirements
- User Story: US-CHR-006
- Functional Requirements: FR-7

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant context.
- Org chart with a 3-level hierarchy is rendered and fully visible: "Corp" -> "Engineering", "Sales" -> "Backend", "Frontend".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Visible Nodes | Corp, Engineering, Sales, Backend, Frontend | 5 nodes across 3 levels |
| Export Format | PNG | Image export |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Organization Tree page | Org chart renders with all 5 nodes visible. |
| 2 | Expand all visible nodes so the full tree is displayed | "Corp" -> "Engineering" -> "Backend", "Frontend"; "Corp" -> "Sales". All connector lines visible. |
| 3 | Click the "Export" button in the toolbar | A dropdown or dialog appears with export format options (PNG, PDF). |
| 4 | Select "PNG" | The browser initiates a file download. |
| 5 | Open the downloaded PNG image | The image is valid (not corrupt, proper PNG headers). |
| 6 | Verify the image contains all 5 department node cards | "Corp", "Engineering", "Sales", "Backend", and "Frontend" labels are readable in the image. |
| 7 | Verify connector lines are present | SVG paths connecting parent to child nodes are rendered in the exported image. |
| 8 | Verify the image dimensions are reasonable | Image width and height are sufficient to contain all nodes without truncation. |
| 9 | Verify manager names and employee counts are visible | Node card details (manager name, employee count badge) are legible in the exported PNG. |

## 6. Postconditions
- A PNG file has been downloaded to the user's machine.
- No server-side data was modified.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
