---
id: US-CHR-006
module: Core HR
priority: Should Have
persona: HR Officer / Manager / Tenant Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-CHR-006: Organization Tree / Hierarchy Visualization

## 1. Description
**As an** HR Officer, Manager, or Tenant Admin,
**I want to** view the organization hierarchy as an interactive tree or chart,
**So that** I can understand reporting structures, department relationships, and the overall organizational shape at a glance.

## 2. Preconditions
- The user is authenticated with a valid tenant context.
- At least one department exists with employees assigned (see US-CHR-004, US-CHR-001).
- Department hierarchy (parent-child) and/or manager assignments are configured.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An HR Officer navigates to the Organization Tree page | The page loads | An interactive org chart is rendered showing the department hierarchy with department names, manager avatars/names, and employee counts per node. Root departments are at the top; child departments branch downward. |
| AC-2 | The user clicks on a department node in the tree | A detail panel or expanded view appears | It shows the department manager, list of direct employees, sub-departments, and a link to the department management page. |
| AC-3 | The user toggles to "Reporting Structure" view | The view switches | The tree reorganizes to show manager-to-direct-report relationships (people-centric rather than department-centric), with each manager node expandable to show their reports. |
| AC-4 | The user searches for an employee in the org tree | They type a name in the search bar | The tree highlights and auto-scrolls to the matching node, with the path from root to that node expanded and all other branches collapsed. |
| AC-5 | The organization has more than 100 nodes | The tree renders | The system uses lazy loading for deep branches (expand on click), maintains smooth 60fps pan/zoom interactions, and does not freeze the browser. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL render an interactive org chart supporting two views: Department Hierarchy and Reporting Structure (manager chain).
- FR-2: The system SHALL support expand/collapse of tree nodes.
- FR-3: The system SHALL support pan and zoom interactions (mouse drag + scroll wheel on desktop; pinch-zoom on mobile).
- FR-4: The system SHALL provide search with auto-scroll and highlight within the tree.
- FR-5: The system SHALL display employee count per department node.
- FR-6: The system SHALL lazy-load child nodes for deep hierarchies (API call on expand).
- FR-7: The system SHALL allow exporting the org chart as PNG or PDF.
- FR-8: All data SHALL be scoped to the current tenant via `tenant_id`.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Initial tree render (top 2 levels) SHALL complete within 2.5 seconds (P95).
- NFR-2: Pan/zoom interactions SHALL maintain 60fps on modern browsers.
- NFR-3: All org tree data SHALL be tenant-isolated via RLS and EF Core global query filters.
- NFR-4: The org chart SHALL be responsive: on desktop, a zoomable canvas; on mobile (< 768px), a collapsible vertical tree/accordion layout.
- NFR-5: The page SHALL meet WCAG 2.1 AA: tree nodes accessible via keyboard (arrow keys), screen reader announces node label and level.

## 6. Business Rules
- BR-1: The org tree reflects the current state of department hierarchy and manager assignments; historical snapshots are not displayed here.
- BR-2: Departments with no parent are root nodes.
- BR-3: Employees without a manager assignment appear under their department node but not under any manager node in reporting view.
- BR-4: Only active departments and active employees are shown by default; a toggle can show inactive.
- BR-5: The tree data is read-only on this page; modifications are made through department and employee management pages.

## 7. Data Requirements
**API endpoint:** `GET /api/v1/org-tree?view=department|reporting&parentId=&depth=`

**Node data:**
| Field | Type |
|-------|------|
| node_id | uuid (department_id or employee_id) |
| node_type | "department" or "employee" |
| name | string |
| title | string (job title for employee nodes) |
| avatar_url | string (for employee nodes) |
| employee_count | number (for department nodes) |
| children_count | number |
| parent_id | uuid |
| is_expanded | boolean (client-side state) |

## 8. UI/UX Notes (Notion-like, cards-based)
- Org chart rendered using a free open-source library (e.g., d3-org-chart, ngx-org-chart, or custom d3.js implementation).
- Each node is a mini-card (`rounded-lg shadow-sm bg-white border border-gray-100`) showing: avatar (32px circle), name, title/department, employee count badge.
- Connector lines: smooth curved SVG paths in a subtle gray (`stroke: #e5e7eb`).
- View toggle buttons (Department / Reporting) at the top as segmented control with smooth indicator animation.
- Search bar with typeahead at the top-right.
- Zoom controls: `+` / `-` buttons plus a "Fit to screen" button in a floating toolbar.
- On mobile: render as a collapsible vertical list (accordion style) rather than a horizontal tree. Each level indented with a left border line.
- Export button (PNG/PDF) in the toolbar.
- Smooth expand/collapse animation for nodes (200ms ease).

## 9. Dependencies
- US-CHR-001: Employee records populate the tree nodes.
- US-CHR-004: Department hierarchy provides the structural backbone.
- US-CHR-011: Manager assignments define reporting structure view.

## 10. Assumptions & Constraints
- The org tree library must be free and open-source (per project constraints).
- For very large organizations (>1000 nodes), server-side pagination with lazy loading is essential; the client never loads the full tree at once.
- The tree does not support drag-and-drop reorganization (changes are made through the respective management pages).
- SVG/Canvas rendering is preferred over DOM-heavy approaches for performance.

## 11. Test Hints
- **Render hierarchy:** Create a 3-level department hierarchy with employees; verify the tree renders correctly with correct parent-child connections.
- **Search and highlight:** Search for an employee at the deepest level; verify the tree auto-expands and scrolls to the node.
- **Lazy loading:** Create a hierarchy with 4+ levels; verify only top 2 levels load initially; expanding a node triggers an API call for children.
- **Tenant isolation:** Verify org tree API returns only departments/employees from the current tenant.
- **Reporting view:** Assign managers; switch to reporting view; verify manager-report relationships are displayed correctly.
- **Performance:** Load an org tree with 200 nodes; verify smooth pan/zoom at 60fps.
- **Mobile:** View the org tree at 360px width; verify it falls back to an accordion/vertical list.
- **Export:** Export as PNG; verify the image contains the visible tree structure.
