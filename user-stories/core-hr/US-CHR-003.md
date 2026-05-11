---
id: US-CHR-003
module: Core HR
priority: Must Have
persona: HR Officer / Manager
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-CHR-003: Employee Directory with Search and Filters

## 1. Description
**As an** HR Officer or Manager,
**I want to** browse, search, and filter the employee directory,
**So that** I can quickly find employees by name, department, job title, status, or other attributes.

## 2. Preconditions
- The user is authenticated with a valid tenant context.
- At least one employee record exists in the tenant.
- The user has a role that grants access to the employee directory (HR Officer: full directory; Manager: direct/indirect reports; Employee: basic directory with limited fields).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An HR Officer navigates to the Employee Directory page | The page loads | A paginated list/grid of employee cards is displayed showing avatar, name, employee_no, department, job title, and status badge, sorted by name ascending by default. |
| AC-2 | The user types a search query (e.g., "John" or "EMP-0042") in the search bar | After a 300ms debounce | The directory filters in real-time to show employees whose name, email, employee_no, or phone matches the query (case-insensitive, partial match). |
| AC-3 | The user applies filters for department = "Engineering" and status = "active" | They click "Apply Filters" | Only active employees in the Engineering department are displayed; filter chips appear showing the active filters; the URL query parameters update for shareability. |
| AC-4 | The directory contains 500 employees | The user scrolls or navigates pages | Results are paginated (default 20 per page) with page controls; total count is displayed; the API uses keyset or offset pagination with `tenant_id` in the query. |
| AC-5 | An HR Officer clicks the "Export" button | They select CSV or Excel format | The visible/filtered employee list is exported as a downloadable file with columns matching the displayed fields, scoped to the current tenant only. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide a full-text search across employee name, email, employee_no, and phone fields.
- FR-2: The system SHALL support filter controls for: department (multi-select), job title (multi-select), status (multi-select: active, probation, suspended, terminated), employment type (multi-select), location, and date of joining range.
- FR-3: The system SHALL support two view modes: card/grid view and table/list view, togglable by the user.
- FR-4: The system SHALL support sorting by: name (A-Z, Z-A), employee_no, date of joining, department.
- FR-5: The system SHALL paginate results with configurable page sizes (10, 20, 50).
- FR-6: The system SHALL persist filter/search state in URL query parameters for bookmarking and sharing.
- FR-7: The system SHALL scope all queries by `tenant_id` automatically.
- FR-8: The system SHALL support CSV and Excel (.xlsx via ClosedXML) export of the filtered dataset.
- FR-9: The system SHALL respect role-based visibility: Managers see only their team; Employees see a basic directory (name, photo, department, job title, phone/email).

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Directory page SHALL load within 2.5 seconds (P95) for up to 5,000 employees.
- NFR-2: Search results SHALL update within 500 ms of the user stopping typing (300ms debounce + API response).
- NFR-3: All queries SHALL be tenant-isolated via RLS and EF Core global filters.
- NFR-4: The directory SHALL be fully responsive; on mobile (< 768px), the card view is the default, switching to a compact list.
- NFR-5: Export of 10,000 rows SHALL complete within 5 minutes (async background job for large datasets).
- NFR-6: The directory SHALL meet WCAG 2.1 AA, including keyboard navigation for filters and pagination.

## 6. Business Rules
- BR-1: Soft-deleted employees (`is_deleted = true`) are excluded from the directory by default; HR Officers can toggle "Show Archived" to include them.
- BR-2: Managers see only employees in their reporting chain (direct and indirect reports).
- BR-3: Employees see a simplified directory (no salary, no sensitive personal data) for colleague lookup.
- BR-4: Export respects the same role-based field visibility as the UI.
- BR-5: Search uses PostgreSQL full-text search (`tsvector`) for performance at scale.

## 7. Data Requirements
**API endpoint:** `GET /api/v1/employees?search=&department=&status=&page=&pageSize=&sort=`

**Response fields per employee:**
| Field | Displayed To |
|-------|-------------|
| employee_id | All (internal, not shown) |
| employee_no | All |
| first_name, last_name | All |
| email | HR Officer, Manager |
| phone | HR Officer, Manager |
| department_name | All |
| job_title_name | All |
| status | All |
| date_of_joining | HR Officer, Manager |
| profile_photo_url | All |
| location | All |

**Pagination response:** `{ data: [...], total: number, page: number, pageSize: number }`

## 8. UI/UX Notes (Notion-like, cards-based)
- Top section: Search bar (full-width, prominent, with search icon) + filter button that opens a slide-out filter panel or inline filter chips.
- View toggle buttons (grid/list) in the top-right corner, next to the Export button.
- **Card/grid view:** Employee cards in a responsive grid (4 columns on desktop, 2 on tablet, 1 on mobile). Each card shows circular avatar, name, title, department tag, and status badge. Cards have `rounded-xl shadow-sm hover:shadow-md` with a subtle lift on hover (translateY -2px, 150ms transition).
- **List/table view:** Clean table with sticky header, alternating row shading, row hover highlight.
- Filter chips below the search bar showing active filters with an "x" to remove each.
- Pagination bar at the bottom: page numbers, prev/next arrows, "Showing 1-20 of 342 employees" text.
- Empty state: illustration + "No employees found. Try adjusting your search or filters." message.
- Skeleton loading: shimmer cards/rows while data is fetching.

## 9. Dependencies
- US-CHR-001: Employees must exist to be listed.
- US-CHR-004: Department data for filter dropdowns.
- US-CHR-005: Job title data for filter dropdowns.
- US-CHR-007: Location data for filter dropdowns.
- Search Strategy (Technical Doc S30): PostgreSQL full-text search configuration.

## 10. Assumptions & Constraints
- Full-text search uses PostgreSQL `tsvector` columns maintained via triggers or generated columns.
- For very large tenants (>10k employees), the export is processed as an async background job (Hangfire) and the user is notified when ready.
- Only free/open-source libraries are used (ClosedXML for Excel export).
- URL-based filter state enables deep-linking and browser back/forward navigation.

## 11. Test Hints
- **Search:** Create 50 employees; search by partial name, email, and employee_no; verify correct results.
- **Filter combination:** Filter by department + status; verify only matching records returned.
- **Pagination:** Create 55 employees; set page size to 20; verify 3 pages with correct counts.
- **Tenant isolation:** Query directory from Tenant A; verify zero records from Tenant B appear.
- **Role-based visibility:** Login as Employee; verify sensitive fields (salary, personal phone) are not in the API response.
- **Export:** Filter to 10 employees; export CSV; verify file contains exactly 10 rows with correct columns.
- **Responsive:** Verify grid reflows from 4 columns to 1 column as viewport shrinks.
- **Empty state:** Delete all employees (soft); verify empty state message appears.
- **Sort:** Sort by date of joining descending; verify order is correct.
