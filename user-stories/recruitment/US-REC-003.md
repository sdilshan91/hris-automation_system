---
id: US-REC-003
module: Recruitment
priority: Must Have
persona: Recruiter
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-REC-003: Recruiter Views Applicant Pipeline with Stage Management

## 1. Description
**As a** Recruiter,
**I want to** view all applicants for a vacancy organized by pipeline stage in a Kanban board,
**So that** I can quickly assess the hiring funnel and manage candidates through the recruitment process.

## 2. Preconditions
- The user is authenticated and has the `Recruitment.Read.All` permission within the resolved tenant.
- At least one vacancy exists with one or more applicants.
- Pipeline stages are configured for the tenant (default or vacancy-specific).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A vacancy has applicants in various pipeline stages | The recruiter navigates to the vacancy's applicant pipeline view | A Kanban board is displayed with columns for each pipeline stage (Applied, Screening, Interview, Offer, Hired, Rejected), each column showing applicant cards with name, applied date, and source |
| AC-2 | The recruiter is viewing the Kanban board | They drag an applicant card from "Applied" to "Screening" | The applicant's pipeline stage is updated in the database, the board reflects the change with a smooth animation, and an audit log entry is recorded |
| AC-3 | The recruiter is viewing the pipeline | They click on an applicant card | A detail slide-over panel opens showing the applicant's full profile, resume (with inline preview for PDFs), timeline of stage transitions, interview scores, and notes |
| AC-4 | The recruiter wants to filter applicants | They use the filter bar to filter by stage, source, date range, or search by name/email | The Kanban board updates to show only matching applicants with counts per column updated |
| AC-5 | A recruiter in Tenant A views the pipeline | The system queries applicant data | Only applicants belonging to Tenant A are returned; RLS policies prevent any cross-tenant data leakage |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL display applicants in a Kanban board layout with one column per configured pipeline stage, ordered left-to-right by stage sequence.
- FR-2: Each applicant card on the Kanban board SHALL display: applicant name, application date, source badge (public/internal/referral), and a visual indicator for unread/new applications.
- FR-3: The system SHALL support drag-and-drop to move applicant cards between pipeline stages with optimistic UI update and server-side persistence.
- FR-4: The system SHALL provide an alternative table/list view toggle for users who prefer a traditional grid layout with sortable columns.
- FR-5: The system SHALL display aggregate counts per stage (e.g., "Screening (12)") and a total applicant count for the vacancy.
- FR-6: The system SHALL support filtering by: pipeline stage, application source, date range (applied date), and free-text search on name/email.
- FR-7: The system SHALL provide an applicant detail panel (slide-over) with: personal info, resume viewer (inline PDF preview), stage transition history/timeline, interview schedules and scores, notes/comments, and action buttons (advance, reject, schedule interview).
- FR-8: The system SHALL support bulk actions: select multiple applicants and move them to a stage, send bulk emails, or export to CSV.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: The Kanban board SHALL load within 400 ms (P95) for up to 200 applicants per vacancy.
- NFR-2: Drag-and-drop stage transitions SHALL persist within 800 ms (P95) and provide optimistic UI feedback.
- NFR-3: All applicant queries SHALL be tenant-scoped via `tenant_id` with PostgreSQL RLS as defense-in-depth.
- NFR-4: The Kanban board SHALL be fully responsive; on mobile (< 768px), stages should be horizontally scrollable or displayed as a stacked list with stage filter tabs.
- NFR-5: Inline PDF resume preview SHALL use a client-side viewer (e.g., pdf.js) and not expose direct blob storage URLs to the browser.

## 6. Business Rules
- BR-1: Only users with `Recruitment.Read.All` permission can view the applicant pipeline.
- BR-2: Only users with `Recruitment.Manage.All` permission can move applicants between stages or perform bulk actions.
- BR-3: Moving an applicant to the "Rejected" stage requires a rejection reason (dropdown + optional free text).
- BR-4: Stage transitions are unidirectional by default (forward only), but users with `Recruitment.Manage.All` can move applicants backward with a reason.
- BR-5: Each stage transition is recorded with timestamp, user, from-stage, to-stage, and optional notes for full audit trail.
- BR-6: The "Hired" stage is terminal and triggers the convert-to-employee workflow (see US-REC-010).

## 7. Data Requirements
- **Input:** Vacancy ID for board context; filter parameters (stage, source, date range, search text); drag-and-drop events (applicant ID, target stage ID).
- **Output:** Kanban board data structure: array of stages, each containing an ordered array of applicant summary DTOs. Applicant detail DTO with full profile, resume URL (signed, short-lived), stage history, interview data.
- **Storage:** `applicant` table with `pipeline_stage_id` FK, `tenant_id`. `applicant_stage_history` table for transition audit trail. Indexes on `(tenant_id, vacancy_id, pipeline_stage_id)`.

## 8. UI/UX Notes
- **Kanban board:** Notion-like aesthetic with clean column headers showing stage name and count badge. Applicant cards should have subtle shadows, rounded corners, and hover elevation effect.
- **Drag-and-drop:** Use Angular CDK Drag-and-Drop module. Show a ghost card during drag with a drop-zone highlight on the target column.
- **Applicant card:** Compact design -- avatar placeholder (initials), name, "Applied 3 days ago" relative timestamp, source badge (colored pill).
- **Detail panel:** Slide-over from the right (60-70% width on desktop, full width on mobile) with tabs: Profile, Resume, Timeline, Interviews, Notes.
- **Resume viewer:** Inline PDF viewer using pdf.js; for DOCX files, show a download button.
- **Filters:** Sticky filter bar above the board with chip-style active filters.
- **Mobile:** Horizontal scroll for Kanban columns or tab-based stage navigation; cards stack vertically within each stage.
- **Empty state:** Friendly illustration with "No applicants yet" message when a vacancy has no applications.

## 9. Dependencies
- US-REC-001 (vacancies must exist).
- US-REC-002 (applicants must exist).
- File & Document Management module for signed resume URLs.
- Audit logging module for stage transition tracking.
- Angular CDK for drag-and-drop functionality.

## 10. Assumptions & Constraints
- Default pipeline stages are: Applied, Screening, Interview, Offer, Hired, Rejected. Tenants can customize stage names and add/remove stages via module configuration (S35.2.9).
- The Kanban board is the primary view; the table view is a secondary option.
- Real-time updates (e.g., new applicant appears on board) are a "Should Have" enhancement via SignalR; initial implementation uses manual refresh.
- PDF preview is rendered client-side; no server-side rendering of documents.

## 11. Test Hints
- Load a vacancy with 200 applicants across all stages and verify Kanban board loads within 400 ms.
- Test drag-and-drop: move an applicant from "Applied" to "Screening" and verify the database is updated and audit log entry created.
- Test backward stage movement: verify it requires `Recruitment.Manage.All` permission and a reason.
- Test rejection: move to "Rejected" without a reason and verify the system requires one.
- Test cross-tenant isolation: verify Tenant B cannot see Tenant A's applicants via API or UI.
- Test mobile layout at 360px: verify horizontal scrolling or tab navigation works for stages.
- Test inline PDF preview: upload a multi-page PDF resume and verify all pages are viewable.
- Test filters: apply stage + date range filter and verify correct results; clear filters and verify all applicants return.
- Test bulk select: select 5 applicants and move them to "Screening"; verify all 5 are updated.
