---
id: US-NTF-005
module: Notifications & Audit
priority: Must Have
persona: Tenant Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-NTF-005: Audit Log Viewer with Filters for Admins

## 1. Description
**As a** Tenant Admin (or Auditor),
**I want to** view, search, and filter the tenant's audit log through a dedicated UI with advanced filtering by date range, actor, action type, resource type, and keyword,
**So that** I can investigate specific changes, monitor user activity, and provide evidence for compliance audits without needing database access.

## 2. Preconditions
- The user is authenticated with the Tenant Admin or Auditor role within their tenant.
- Audit records exist in the `audit_log` table for the tenant (US-NTF-004).
- The Audit Log Viewer feature is accessible from the Tenant Admin Console (Security & Audit section).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A Tenant Admin navigates to Security & Audit > Audit Log | They view the audit log page | A paginated table is displayed showing recent audit entries (newest first) with columns: Timestamp, Actor, Action, Resource Type, Resource ID, and IP Address. The table loads within 2 seconds for the first page. |
| AC-2 | The admin applies filters: date range = "last 7 days", action = "Employee.Update", actor = "John Doe" | They click "Apply Filters" | The table refreshes to show only matching records. The URL is updated with query parameters (bookmarkable). The result count is displayed. |
| AC-3 | The admin clicks on a specific audit entry row | The detail panel opens | A side panel or modal shows the full audit record including before/after JSONB payloads rendered as a diff view (highlighting changed fields in green/red), the full user agent string, and the trace ID with a link to the observability platform. |
| AC-4 | The admin clicks "Export" with current filters applied | The export is triggered | An async job (Hangfire) generates a CSV or JSON Lines file containing the filtered audit records. The admin receives an in-app notification when the export is ready, with a download link (signed URL, valid for 15 minutes). |
| AC-5 | A Tenant A admin views the audit log | Audit records for Tenant A and B exist in the database | Only Tenant A records are displayed; RLS policies and EF Core filters ensure Tenant B data is invisible. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL display audit log entries in a paginated, sortable table with columns: Timestamp, Actor (name + employee_no), Action, Resource Type, Resource ID, IP Address.
- FR-2: The system SHALL provide filter controls: date range picker, actor search (autocomplete by name/email), action type dropdown (multi-select), resource type dropdown (multi-select), keyword search (full-text across `before`/`after` JSONB).
- FR-3: The system SHALL support URL-based filter state (query parameters) for bookmarkable and shareable filtered views.
- FR-4: The system SHALL display a detail view for each audit entry showing the complete record with a visual diff of before/after JSONB changes.
- FR-5: The system SHALL support export of filtered audit records to CSV and JSON Lines formats, generated asynchronously via Hangfire.
- FR-6: The system SHALL paginate results (50 per page) with server-side pagination using keyset pagination (not OFFSET) for performance on large datasets.
- FR-7: The system SHALL display a summary bar showing total record count for the current filter and the tenant's audit log retention period.
- FR-8: The system SHALL restrict access to users with `Audit.Read` permission (Tenant Admin, Auditor roles).
- FR-9: The system SHALL log audit log access itself as an audit event (meta-audit) to track who viewed the audit trail.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Initial page load (first 50 records, no filters) SHALL complete within 2 seconds (P95).
- NFR-2: Filtered query response time SHALL be <= 3 seconds (P95) for datasets up to 10 million audit records per tenant.
- NFR-3: All audit data access SHALL be isolated by tenant via PostgreSQL RLS and EF Core global query filters.
- NFR-4: The audit log viewer SHALL be fully responsive from 360px to 4K resolution.
- NFR-5: The viewer SHALL meet WCAG 2.1 AA accessibility standards (table keyboard navigation, screen reader support for data cells).
- NFR-6: Export files SHALL use signed URLs with a 15-minute expiry for security.
- NFR-7: BRIN indexes on `timestamp` and GIN indexes on JSONB columns SHALL be leveraged for efficient querying.

## 6. Business Rules
- BR-1: Only users with `Audit.Read` permission can access the audit log viewer.
- BR-2: Audit records are read-only in the viewer; no modification or deletion is possible through the UI.
- BR-3: The viewer respects the tenant's audit log retention period; records beyond the retention window are not displayed (they may be archived or purged).
- BR-4: Export is limited to 100,000 records per export operation to prevent excessive load.
- BR-5: Viewing the audit log creates a meta-audit record (action: "AuditLog.View") to maintain accountability.
- BR-6: The Auditor role has read-only access to the audit log; they cannot perform any write operations.

## 7. Data Requirements
**Filter inputs:**
| Field | Type | Required | Validation |
|-------|------|----------|------------|
| date_from | timestamptz | No | Must be <= date_to |
| date_to | timestamptz | No | Must be >= date_from |
| actor_user_id | uuid | No | Autocomplete lookup |
| action | varchar(100)[] | No | Multi-select from known actions |
| resource_type | varchar(50)[] | No | Multi-select from known types |
| keyword | varchar(200) | No | Full-text search across JSONB |
| page_cursor | varchar(100) | No | Keyset pagination cursor |
| page_size | int | No | Default: 50, max: 100 |

**Output:** Paginated list of audit records with total count and next page cursor.

## 8. UI/UX Notes
- Top section: filter bar with collapsible advanced filters. Date range picker with presets ("Today", "Last 7 days", "Last 30 days", "Custom").
- Actor filter: searchable dropdown with autocomplete (type-ahead).
- Action and resource type: multi-select dropdowns with checkboxes and "Select All" option.
- Table: striped rows, fixed header on scroll, sortable columns (click column header).
- Row click opens a right-side sliding panel (400px wide) with the detail view.
- Before/after diff: side-by-side comparison with syntax highlighting (JSON format), changed fields highlighted green (added) and red (removed).
- Export button in the toolbar with format selection (CSV / JSON Lines).
- On mobile (< 768px): table collapses to a card list; each card shows key fields; tap opens full detail.
- Loading states: skeleton loader for initial load; inline spinner for filter application.

## 9. Dependencies
- US-NTF-004: Audit trail must be actively recording data changes.
- US-NTF-001: In-app notification for export completion.
- Hangfire: For async export generation.
- Authentication module: User must have `Audit.Read` permission.
- File & Document Management (Technical Doc S26): For export file storage and signed URLs.

## 10. Assumptions & Constraints
- Keyset pagination is used instead of OFFSET-based pagination for consistent performance on large audit tables.
- BRIN indexes on `timestamp` and GIN indexes on JSONB `before`/`after` columns are maintained for query performance.
- The JSON diff view uses a free/open-source diff library (e.g., jsondiffpatch).
- Export files are stored in tenant-isolated object storage with signed URLs.
- Only free/open-source libraries are used.
- The system uses PostgreSQL with RLS as defense-in-depth for tenant isolation.

## 11. Test Hints
- **Happy path:** Navigate to audit log; verify records display in descending timestamp order with correct columns.
- **Date filter:** Apply "last 7 days" filter; verify only records within that range are shown.
- **Actor filter:** Search for a specific user; verify only their actions are displayed.
- **Combined filters:** Apply date + action + resource type filters simultaneously; verify AND logic is applied.
- **Detail view:** Click on an "Employee.Update" audit entry; verify before/after diff is rendered with changed fields highlighted.
- **Export:** Apply filters and export to CSV; verify Hangfire job is created, notification received on completion, and file contains correct data.
- **Tenant isolation:** Log in as Tenant A admin; verify no Tenant B audit records are visible.
- **Performance:** Seed 1 million audit records for a tenant; verify first page loads within 2 seconds and filtered queries within 3 seconds.
- **Meta-audit:** View the audit log; verify a new "AuditLog.View" audit record is created.
- **Pagination:** Generate 200 records; verify pagination shows 50 per page and "next" cursor works correctly.
- **Responsive:** Test the viewer at 360px width; verify table collapses to card list.
- **Accessibility:** Navigate the table using keyboard; verify column sort and row selection work via keyboard.
