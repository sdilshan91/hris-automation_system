---
id: US-PRF-005
module: Performance Management
priority: Should Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-PRF-005: 360-Degree Review (Peers, Reports, Manager, Self)

## 1. Description
**As an** HR Officer,
**I want to** configure and run a 360-degree feedback review where peers, direct reports, the manager, and the employee themselves all provide performance feedback,
**So that** the organization obtains a holistic, multi-perspective view of each employee's performance, reducing single-rater bias and supporting well-rounded development planning.

## 2. Preconditions
- The HR Officer is authenticated and has `Performance.Review.All` permission.
- An active appraisal cycle exists with the 360-degree toggle enabled (US-PRF-004).
- The employee has at least one peer or direct report nominated or assigned as a reviewer.
- The Performance module is enabled for the tenant.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A 360-degree-enabled cycle is active | The HR Officer or manager navigates to the 360 configuration for an employee | The system displays reviewer nomination options: auto-suggest peers (same department), direct reports (from org tree), the reporting manager (auto-assigned), and self (auto-assigned), with the ability to add/remove reviewers manually |
| AC-2 | Reviewers have been assigned to an employee | The system enters the 360-feedback phase | Each assigned reviewer receives a notification (in-app + email) with a link to a feedback form containing the rating scale and competency-based questions |
| AC-3 | A peer reviewer completes and submits their feedback | The reviewer clicks "Submit Feedback" | The system saves the feedback, marks the reviewer's status as "Completed", and updates the completion tracker; if tenant anonymity is enabled, reviewer identity is hidden from the employee and manager in the results view |
| AC-4 | All (or minimum required) reviewers have submitted feedback | HR or the manager views the 360 results | The system displays an aggregated report with average ratings per competency, a radar chart comparing self/manager/peer/report perspectives, and anonymized individual comments (if anonymity is on) |
| AC-5 | A reviewer has not submitted feedback and the deadline is approaching | Hangfire triggers a reminder at the configured interval | The system sends a reminder notification to the reviewer with a direct link to the pending feedback form |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system shall support four reviewer categories: Self, Manager, Peer, and Direct Report.
- FR-2: HR or the manager shall be able to nominate peers and direct reports as reviewers; self and manager are auto-assigned.
- FR-3: The system shall support a configurable minimum number of reviewers per category (e.g., at least 2 peers).
- FR-4: The feedback form shall present competency-based questions with the tenant-configured rating scale and optional free-text comments per competency.
- FR-5: The system shall support anonymous feedback mode (tenant-configurable): when enabled, reviewer identity is not revealed in results displayed to the employee or manager.
- FR-6: The system shall aggregate 360 feedback into a composite score with configurable weights per reviewer category (e.g., self 10%, manager 40%, peers 30%, reports 20%).
- FR-7: The system shall generate a 360 feedback summary report per employee, exportable as PDF.
- FR-8: Hangfire shall schedule reviewer reminders at configurable intervals before the feedback deadline.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: The 360 feedback form shall load within 400ms (P95).
- NFR-2: All feedback data shall be tenant-isolated via PostgreSQL RLS; feedback from Tenant A shall never leak to Tenant B.
- NFR-3: Anonymous feedback must be enforced at the database level; API responses shall not include reviewer identifiers when anonymity is enabled, even in debug mode.
- NFR-4: The aggregated results view with radar chart shall render within 2 seconds for up to 20 reviewers per employee.
- NFR-5: The feedback form shall be fully mobile-responsive, enabling reviewers to submit from any device.

## 6. Business Rules
- BR-1: 360-degree feedback is optional per tenant and per cycle; it is only available when the toggle is enabled in cycle configuration.
- BR-2: An employee cannot review themselves in the peer category; self-assessment is a separate category.
- BR-3: A reviewer can only submit one feedback per employee per cycle.
- BR-4: The minimum number of peer reviewers must be met before the 360 results are released; otherwise, HR is warned.
- BR-5: Anonymous feedback anonymity cannot be retroactively disabled after feedback has been submitted.
- BR-6: The 360 composite score is incorporated into the final performance score alongside self and manager ratings.

## 7. Data Requirements
- **Input:** reviewer assignments (employee_id, reviewer_id, category), competency ratings, free-text comments, cycle_id.
- **Output:** individual feedback records (with or without reviewer identity based on anonymity), aggregated scores per competency and category, 360 summary report.
- **Storage:** feedback_360 table with reviewer_id, reviewee_id, category, ratings JSON, comments, anonymity flag, tenant_id with RLS policy.

## 8. UI/UX Notes
- Reviewer nomination UI: searchable employee list with department filters and avatar chips.
- Feedback form: clean, Notion-like question cards with star/slider rating and text area per competency.
- Results dashboard: chart.js radar chart comparing perspectives, bar charts for per-competency averages.
- Completion tracker: progress bar showing submitted/pending/overdue per reviewer category.
- PDF report: branded with tenant logo, includes charts and anonymized comments.
- Mobile: single-column layout with collapsible competency sections.

## 9. Dependencies
- US-PRF-004: Appraisal cycle with 360-degree toggle enabled.
- US-PRF-001: Goals/competencies must be defined for the cycle.
- US-PRF-002: Self-assessment (as one of the 360 perspectives).
- US-PRF-003: Manager review (as one of the 360 perspectives).
- Core HR: org tree for auto-assigning manager and identifying direct reports.
- Notification system and Hangfire for reviewer reminders.

## 10. Assumptions & Constraints
- Competency-based questions for 360 feedback are configured at the tenant level as part of the rating scale setup.
- The system uses chart.js (open-source) for all radar and bar chart visualizations.
- PDF generation uses a free open-source library (e.g., QuestPDF or similar).
- Anonymity is a binary setting per cycle; mixed-mode (some categories anonymous, some not) is not supported in the initial release.

## 11. Test Hints
- Verify tenant isolation: feedback submitted in Tenant A must not appear in Tenant B.
- Test anonymity enforcement: submit feedback with anonymity on, query the API, confirm reviewer_id is not in the response payload.
- Test minimum reviewer threshold: attempt to release results with fewer peers than required.
- Test reviewer notification flow: assign a reviewer, verify they receive notification with a deep link.
- Test composite score calculation: submit feedback from all categories, verify weighted aggregation matches expected values.
- Test radar chart rendering with varying numbers of competencies (3, 5, 10).
- Test PDF export: generate report, verify tenant branding, chart rendering, and anonymized comments.
- Test duplicate prevention: attempt to submit feedback twice for the same employee in the same cycle.
