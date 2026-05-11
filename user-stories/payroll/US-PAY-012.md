---
id: US-PAY-012
module: Payroll
priority: Must Have
persona: HR Officer / Tenant Admin / Auditor
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-PAY-012: Payroll History and Audit Trail

## 1. Description
**As an** HR Officer, Tenant Admin, or Auditor,
**I want to** view a comprehensive history of all payroll runs and a tamper-proof audit trail of every payroll-related action (configuration changes, run initiation, approvals, adjustments, payslip generation, email distribution),
**So that** the organization can demonstrate compliance, investigate discrepancies, and maintain financial accountability.

## 2. Preconditions
- Payroll module is enabled for the tenant.
- User has `Payroll.*.All` or audit-viewing permission.
- Audit logging infrastructure is operational (technical doc section 24).
- At least one payroll run has been executed.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | HR navigates to Payroll History | The page loads | A chronological list of all payroll runs is displayed with: Pay Period, Status, Employee Count, Total Net, Initiated By, Approved By, Finalized Date; sortable and filterable by period, status, and year |
| AC-2 | HR selects a specific payroll run from history | HR clicks on the run | The run detail page shows the complete run summary, all employee payslips (searchable), and the run's audit timeline (who initiated, when each status change occurred, who approved/rejected, comments) |
| AC-3 | A salary component is modified by HR | The modification is saved | An audit log entry is created with: timestamp, actor (user), action ("SalaryComponent.Updated"), resource_type, resource_id, before (old values as JSON), after (new values as JSON), IP address, user agent |
| AC-4 | Tenant Admin views the payroll audit trail | They search for all payroll-related actions in the last 30 days | The audit trail returns all payroll actions (config changes, run events, approval actions, adjustments) filtered by date range, with export capability |
| AC-5 | Audit trail data belongs to Tenant A | A user from Tenant B queries the audit API | Only Tenant B's audit records are returned; RLS enforces tenant-scoped isolation on the audit_log table |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL maintain a complete payroll run history with all status transitions, timestamps, and actors, accessible via `GET /api/v1/payroll/runs` with filtering and pagination.
- FR-2: The system SHALL log ALL payroll-related write operations to the `audit_log` table (per technical doc section 24), including:
  - Salary component CRUD operations
  - Salary structure CRUD operations
  - Employee salary assignment changes
  - Statutory rule configuration changes
  - Payroll run initiation, status transitions, approval/rejection actions
  - Payroll adjustment creation, modification, cancellation
  - Payslip PDF generation events
  - Payslip email distribution events
- FR-3: Each audit log entry SHALL include: tenant_id, timestamp, actor_user_id, actor_employee_no, action, resource_type, resource_id, before (jsonb), after (jsonb), ip_address, user_agent, trace_id (per technical doc section 19.9).
- FR-4: The system SHALL provide a payroll-specific audit trail view with filtering by: date range, action type, actor, resource type, and resource ID.
- FR-5: The system SHALL support exporting the audit trail to CSV and Excel formats.
- FR-6: The system SHALL display a per-payroll-run timeline view showing all actions related to that specific run in chronological order.
- FR-7: The system SHALL retain payroll audit data for 7+ years as per statutory requirements (technical doc section 19.13).
- FR-8: The system SHALL support comparison/diff view for configuration changes (side-by-side before/after).

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Audit log entries SHALL be written asynchronously (fire-and-forget) to avoid impacting the performance of the primary payroll operations.
- NFR-2: Audit log queries SHALL be performant: P95 response time <= 2 seconds for queries spanning up to 1 year of data.
- NFR-3: Audit logs SHALL use BRIN indexes on timestamp columns for efficient range queries (per technical doc section 19.12).
- NFR-4: Audit log data SHALL be immutable; no UPDATE or DELETE operations permitted on audit records via the application.
- NFR-5: Payroll data (including history) SHALL be retained for 7+ years per statutory requirements (technical doc section 19.13).
- NFR-6: Test coverage for audit logging integration SHALL be >= 85%.
- NFR-7: Audit log archival: records older than 90 days SHALL be moved to cold storage per the weekly archival job (technical doc section 28.3).

## 6. Business Rules
- BR-1: Every payroll-related write operation MUST generate an audit log entry. There are no exceptions.
- BR-2: Audit log entries are immutable and tamper-proof. The application does not expose any API to modify or delete audit records.
- BR-3: Payroll run history is never deleted, even if the tenant is terminated; it is retained per statutory data retention rules (7+ years).
- BR-4: The audit trail must be sufficient for a financial auditor to reconstruct the complete sequence of events for any payroll run.
- BR-5: Audit log entries for sensitive actions (payroll approval, finalization) must capture the actor's IP address and user agent for non-repudiation.
- BR-6: Historical payslip data is preserved as-is; if component names or structures change after a run, the historical payslip reflects the values at the time of the run (point-in-time snapshot).
- BR-7: System-initiated actions (e.g., Hangfire job processing) must be logged with a system actor identifier, not a null actor.
- BR-8: Impersonation sessions must be clearly marked in the audit trail (per technical doc section 24) with the impersonating system admin's identity.

## 7. Data Requirements

**audit_log table (per technical doc section 19.9):**
| Column | Type | Constraints |
|--------|------|-------------|
| audit_log_id | bigserial (PK) | NOT NULL |
| tenant_id | uuid | NOT NULL, RLS-enforced |
| timestamp | timestamptz | NOT NULL |
| actor_user_id | uuid | NOT NULL |
| actor_employee_no | varchar(50) | nullable |
| action | varchar(100) | NOT NULL (e.g., "PayrollRun.Initiated", "SalaryComponent.Updated") |
| resource_type | varchar(50) | NOT NULL (e.g., "PayrollRun", "SalaryComponent", "PayrollSlip") |
| resource_id | varchar(100) | NOT NULL |
| before | jsonb | nullable (null for create actions) |
| after | jsonb | nullable (null for delete actions) |
| ip_address | varchar(50) | nullable |
| user_agent | varchar(500) | nullable |
| trace_id | varchar(100) | nullable |

**Payroll Action Types (standardized action names):**
| Action | Description |
|--------|-------------|
| SalaryComponent.Created | New salary component created |
| SalaryComponent.Updated | Salary component modified |
| SalaryComponent.Deleted | Salary component soft-deleted |
| SalaryStructure.Created | New salary structure created |
| SalaryStructure.Updated | Salary structure modified |
| EmployeeSalary.Assigned | Salary structure assigned to employee |
| EmployeeSalary.Revised | Salary revision applied |
| StatutoryRule.Created | Statutory rule configured |
| StatutoryRule.Updated | Statutory rule modified |
| PayrollRun.Initiated | Payroll run started |
| PayrollRun.Completed | Payroll calculation completed |
| PayrollRun.SubmittedForApproval | Run submitted for approval |
| PayrollRun.Approved | Run approved |
| PayrollRun.Rejected | Run rejected |
| PayrollRun.Finalized | Run finalized (immutable) |
| PayrollRun.Cancelled | Run cancelled |
| PayrollAdjustment.Created | Adjustment created |
| PayrollAdjustment.Cancelled | Adjustment cancelled |
| PayslipPDF.Generated | Payslip PDFs generated |
| PayslipEmail.Sent | Payslip emails distributed |

## 8. UI/UX Notes (Notion-like)
- Payroll History page: Notion-style database table view with columns for period, status badge, totals, and action icons. Filterable by year, status, and initiator.
- Run detail page: three tabs -- "Summary" (totals and metrics), "Payslips" (searchable employee list), "Audit Trail" (timeline view).
- Audit trail timeline: vertical timeline with cards for each event. Each card shows: timestamp, actor name/avatar, action description, and an expandable diff view for before/after changes.
- Diff view: side-by-side JSON comparison with syntax highlighting and changed fields highlighted (green for additions, red for removals, amber for modifications).
- Search and filter bar at the top of the audit trail: date range picker, action type dropdown, actor search, resource type filter.
- Export button in the toolbar for CSV/Excel export of filtered audit data.
- Mobile: payroll history list viewable; run summary viewable; audit trail timeline scrollable; diff view deferred to desktop for readability.

## 9. Dependencies
- **US-PAY-001 through US-PAY-011**: All payroll stories generate audit log entries consumed by this story.
- **Audit Logging Infrastructure**: Technical doc section 24 -- shared audit_log table and logging middleware.
- **US-AUTH-xxx**: Actor identity (user_id, employee_no) available from JWT claims for audit entries.
- ClosedXML for Excel export of audit data.

## 10. Assumptions & Constraints
- The audit_log table is a shared platform table used by all modules; payroll-specific entries are filtered by resource_type.
- Audit log writes are asynchronous (e.g., via MediatR notification handlers or an outbox pattern) to avoid impacting primary operation latency.
- Cold storage archival for audit logs > 90 days is handled by a platform-level weekly job (technical doc section 28.3); this story assumes that job exists.
- BRIN indexes are suitable because audit_log is append-only and time-ordered (per technical doc section 19.12).
- The diff view in the UI is a presentation-layer feature; the backend stores raw before/after JSON.

## 11. Test Hints
- Unit test: Verify that every payroll write operation triggers an audit log entry with correct action name and resource type.
- Unit test: Verify before/after JSON captures the actual changed fields for an update operation.
- Unit test: Verify system-initiated actions (Hangfire jobs) are logged with a system actor, not null.
- Integration test: Perform a sequence of payroll operations (create component, assign salary, run payroll, approve, finalize), query audit trail, verify all events are recorded in correct chronological order.
- Integration test: Verify audit trail is tenant-scoped: Tenant A's audit entries invisible to Tenant B.
- Integration test: Verify audit log entries are immutable: attempt to call UPDATE/DELETE on audit_log via API and verify 405 Method Not Allowed.
- E2E (Playwright): Navigate to payroll history, select a run, view the audit trail timeline, expand a diff view, verify correct before/after values displayed.
- Performance test: Query audit trail for 1 year of data (estimated 50,000+ entries) and verify P95 response time <= 2 seconds.
- Data retention test: Verify payroll audit data older than 90 days is archived but still queryable from cold storage.
