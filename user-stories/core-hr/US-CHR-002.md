---
id: US-CHR-002
module: Core HR
priority: Must Have
persona: HR Officer / Employee
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 6
---

# US-CHR-002: View and Edit Employee Profile

## 1. Description
**As an** HR Officer or Employee,
**I want to** view a comprehensive employee profile and edit permitted fields,
**So that** employee records remain accurate and up-to-date, and employees can self-serve updates to their personal information.

## 2. Preconditions
- The user is authenticated with a valid tenant context.
- The employee record exists within the current tenant.
- HR Officer role has full read/write access; Employee role has read access to own profile and write access to a limited subset of fields (phone, address, emergency contact).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An HR Officer navigates to an employee's profile page | The page loads | A comprehensive profile is displayed in card-based sections: Summary Header (photo, name, employee_no, department, status badge), Personal Info, Contact, Emergency Contacts, Employment Details, Education, Work History, Dependents, Documents, and Custom Fields. |
| AC-2 | An HR Officer clicks "Edit" on any profile section | They modify fields and click "Save" | The system updates the record, applies optimistic concurrency via `xmin` token, records the change in the audit log (`before`/`after` JSONB snapshots), and displays a success toast. |
| AC-3 | Two HR Officers open the same employee profile and both edit simultaneously | The second officer submits after the first | The system detects the concurrency conflict (stale `xmin`), rejects the second save with a message "This record was modified by another user. Please refresh and try again.", and does not overwrite the first change. |
| AC-4 | An Employee views their own profile via the self-service portal | The page loads | They see their full profile in read-only mode, except for permitted editable fields (phone, personal email, address, emergency contacts) which show an "Edit" button. |
| AC-5 | An Employee attempts to edit a restricted field (e.g., salary, department, job title) | They inspect the page | Those fields are rendered as read-only with no edit affordance; API rejects any PATCH attempts on restricted fields from Employee role. |
| AC-6 | An HR Officer changes the employee's department or job title | They save the change | The system records the change in the employment history timeline and updates related references (e.g., reporting structure). |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL display the employee profile in card-based sections, each independently collapsible.
- FR-2: The system SHALL support inline editing (click-to-edit) or a modal edit form per section.
- FR-3: The system SHALL enforce field-level permissions based on the user's role (HR Officer: all fields; Employee: limited subset; Manager: read-only for direct reports).
- FR-4: The system SHALL use optimistic concurrency control via PostgreSQL `xmin` column to prevent lost updates.
- FR-5: The system SHALL log every field change to the audit_log table with `before` and `after` JSONB snapshots.
- FR-6: The system SHALL maintain an employment history timeline showing changes to department, job title, status, and reporting manager with effective dates.
- FR-7: The system SHALL scope all queries by `tenant_id` via EF Core global filter and RLS.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Employee profile page SHALL load within 2.5 seconds (P95) on a 4G connection.
- NFR-2: API read response time SHALL be <= 400 ms (P95).
- NFR-3: All profile data SHALL be tenant-isolated via PostgreSQL RLS and EF Core query filters.
- NFR-4: PII access (viewing sensitive fields) SHALL be recorded in the audit log.
- NFR-5: The profile page SHALL be fully responsive (360px to 4K) with card layout reflowing to single column on mobile.
- NFR-6: The page SHALL meet WCAG 2.1 AA standards.

## 6. Business Rules
- BR-1: Employees can only view and edit their own profile; they cannot access other employees' profiles.
- BR-2: HR Officers can view and edit any employee profile within their tenant.
- BR-3: Managers can view (read-only) profiles of their direct reports.
- BR-4: Changes to employment-critical fields (department, job title, status, salary) by HR trigger an employment history entry.
- BR-5: Profile edits by Employees on sensitive fields (e.g., bank details) may require HR approval (configurable per tenant).
- BR-6: Soft-deleted employees are not visible in normal views but accessible via an "Archived" filter for HR Officers.

## 7. Data Requirements
**Displayed fields (read):** All fields from the `employee` table plus joined data from `department`, `job_title`, related emergency contacts, education records, work history, dependents, and `custom_fields` JSONB.

**Editable fields by role:**
| Field Group | HR Officer | Employee | Manager |
|-------------|-----------|----------|---------|
| Personal info (name, DOB, gender) | Read/Write | Read | Read |
| Contact (phone, address, personal email) | Read/Write | Read/Write | Read |
| Emergency contacts | Read/Write | Read/Write | No access |
| Employment (department, title, type, status) | Read/Write | Read | Read |
| Education & work history | Read/Write | Read/Write | No access |
| Dependents | Read/Write | Read/Write | No access |
| Custom fields | Read/Write | Read (configurable) | Read |

## 8. UI/UX Notes (Notion-like, cards-based)
- Profile header card: large avatar (circular, 96px), employee name, employee_no badge, department tag, status badge (green=active, amber=probation, red=terminated, gray=suspended).
- Each section is a separate card (`rounded-xl shadow-sm bg-white`) with a section title and an edit icon (pencil) in the top-right corner.
- Inline edit mode: clicking edit transitions the card fields from read-only typography to editable inputs with a smooth fade transition (200ms).
- Save/Cancel buttons appear at the bottom of the card in edit mode.
- Employment history displayed as a vertical timeline with date markers and change descriptions.
- On mobile: cards stack vertically, full-width; avatar shrinks to 64px; horizontal tabs collapse into a dropdown selector.
- Use Angular Material `MatTabGroup` for section navigation on desktop, with smooth tab indicator animation.
- Skeleton loading placeholders (Notion-style shimmer) while data loads.

## 9. Dependencies
- US-CHR-001: Employee must exist before viewing/editing.
- US-CHR-004: Department data for display and reassignment.
- US-CHR-005: Job title data for display and reassignment.
- US-CHR-012: Custom fields configuration for rendering dynamic fields.
- Authentication & Authorization module: For role-based field-level access control.
- Audit Logging module (Technical Doc S24): For change tracking.

## 10. Assumptions & Constraints
- Optimistic concurrency via `xmin` is the chosen strategy; no pessimistic locking.
- Employment history is an append-only log; historical entries cannot be edited.
- The system uses Angular standalone components with signals for reactive state management.
- Tailwind CSS + Angular Material are the only styling frameworks (no Bootstrap).

## 11. Test Hints
- **View profile:** Load an employee profile; verify all sections render with correct data from the database.
- **Edit as HR:** Edit personal info and employment details; verify audit log contains `before`/`after` snapshots.
- **Edit as Employee:** Login as employee; verify only permitted fields are editable; attempt to PATCH restricted fields via API; expect 403.
- **Concurrency conflict:** Open profile in two browser tabs, edit in both, submit second; expect concurrency error.
- **Tenant isolation:** Attempt to fetch an employee by ID belonging to a different tenant; expect 404 (not 403, to avoid leaking existence).
- **Employment history:** Change department twice; verify two timeline entries appear with correct dates.
- **Responsive:** Check profile layout at 360px, 768px, 1440px.
- **Accessibility:** Verify all edit buttons have accessible labels; screen reader announces section headings.
