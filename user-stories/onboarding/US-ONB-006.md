---
id: US-ONB-006
module: Onboarding / Offboarding
priority: Should Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-ONB-006: Exit Interview Recording

## 1. Description
**As an** HR Officer,
**I want to** record exit interview responses from departing employees using a structured questionnaire,
**So that** the organization can capture feedback on work experience, reasons for leaving, and improvement suggestions to reduce future attrition and improve employee satisfaction.

## 2. Preconditions
- The HR Officer is authenticated and has an active session within their tenant.
- An offboarding process has been initiated for the employee (US-ONB-005).
- The Onboarding/Offboarding module is enabled for the tenant's subscription plan.
- An exit interview questionnaire template exists for the tenant.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | HR Officer opens the offboarding record for a departing employee | They click "Conduct Exit Interview" | A structured questionnaire form opens with configurable questions grouped by category (e.g., "Reason for Leaving", "Work Environment", "Management", "Recommendations"), pre-loaded from the tenant's exit interview template. |
| AC-2 | HR Officer records the employee's responses including ratings and free-text comments | They save the exit interview | The responses are persisted against the employee's offboarding record with `tenant_id` set from session context, and the exit interview task in the offboarding checklist is marked as "completed". |
| AC-3 | The departing employee opts for a self-service exit interview | They log in and complete the questionnaire themselves | The same questionnaire is presented; responses are saved and linked to the offboarding record. HR is notified upon completion via SignalR and email. |
| AC-4 | HR Officer views exit interview analytics | They navigate to "Exit Interview Summary" | Aggregated data is shown: top reasons for leaving (pie chart), average ratings per category (bar chart), trends over time (line chart), all scoped to the tenant's data only. |
| AC-5 | An exit interview is recorded in Tenant A | A user from Tenant B queries exit interview data | No Tenant A data is visible; RLS and EF Core filters enforce tenant isolation. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide a configurable exit interview questionnaire template with question types: rating scale (1-5), multiple choice, free text, and yes/no.
- FR-2: The system SHALL support both HR-conducted (HR fills in responses) and self-service (employee fills in) exit interview modes.
- FR-3: The system SHALL link exit interview responses to the employee's offboarding record.
- FR-4: The system SHALL provide aggregated analytics: reason distribution, average ratings, trend analysis over configurable time periods.
- FR-5: The system SHALL anonymize individual responses in analytics views (show only aggregates, not individual answers) unless the user has `ExitInterview.ViewDetail` permission.
- FR-6: The system SHALL set `tenant_id` from the session context on all exit interview records.
- FR-7: The system SHALL record exit interview completion as an audit event.
- FR-8: The system SHALL notify HR via SignalR when a self-service exit interview is submitted.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Exit interview form loading time SHALL be <= 500 ms (P95).
- NFR-2: All exit interview data SHALL be isolated by tenant via PostgreSQL RLS and EF Core global query filters.
- NFR-3: Analytics charts SHALL render within 2 seconds for datasets up to 1000 exit interviews.
- NFR-4: The questionnaire form SHALL be fully responsive from 360px to 4K resolution.
- NFR-5: The form SHALL meet WCAG 2.1 AA accessibility standards.
- NFR-6: Free-text responses containing PII SHALL be flagged in the audit log when accessed.

## 6. Business Rules
- BR-1: Only one exit interview can be recorded per offboarding instance.
- BR-2: Exit interview responses are immutable once submitted; edits require HR to create a new version with the original preserved.
- BR-3: Self-service exit interviews must be submitted before the employee's last working day.
- BR-4: Analytics views show data only for exit interviews within the current tenant.
- BR-5: Exit interview data is retained per the tenant's data retention policy; anonymized data may be retained longer for trend analysis.
- BR-6: The exit interview questionnaire template is configurable by Tenant Admins.

## 7. Data Requirements
**Input fields (questionnaire response):**
| Field | Type | Required | Validation |
|-------|------|----------|------------|
| offboarding_id | uuid | Yes | Must belong to the tenant |
| interview_mode | varchar(20) | Yes | "hr_conducted" or "self_service" |
| conducted_by | uuid | Conditional | Required if hr_conducted; HR user ID |
| interview_date | date | Yes | Cannot be in the future |
| responses[].question_id | uuid | Yes | Must exist in tenant's template |
| responses[].rating | int | Conditional | 1-5 if question type = rating |
| responses[].selected_option | varchar(200) | Conditional | If question type = multiple_choice |
| responses[].free_text | text | Conditional | Max 2000 chars if question type = free_text |
| overall_experience_rating | int | No | 1-5 scale |
| would_recommend_employer | boolean | No | |
| additional_comments | text | No | Max 5000 chars |

**Output:** Persisted exit interview record with all responses, linked to offboarding instance.

## 8. UI/UX Notes
- Questionnaire form: one question per card, vertically stacked, with category headers.
- Rating questions: star or emoji-based rating selector (1-5) with label descriptions.
- Multiple choice: radio buttons or styled chips for single selection, checkboxes for multi-select.
- Free text: auto-expanding textarea with character count.
- Progress bar at the top showing completion through the questionnaire sections.
- Analytics view: use Chart.js / ngx-charts for pie charts (reasons), bar charts (ratings), and line charts (trends).
- On mobile (< 768px): full-width cards, large touch-friendly rating selectors.
- Success state: "Exit interview recorded. Thank you for the feedback." with a warm illustration.

## 9. Dependencies
- US-ONB-005: Offboarding process must be initiated for the employee.
- US-RPT-001: Analytics may feed into broader HR reports.
- US-NTF-001: In-app notification when self-service interview is submitted.
- US-NTF-002: Email notification template for exit interview invitation.
- Authentication module: User must be authenticated with valid tenant context.

## 10. Assumptions & Constraints
- Exit interview questionnaire templates are configured by Tenant Admins via the Tenant Admin Console.
- Chart.js or ngx-charts (free/open-source) is used for analytics visualizations.
- Self-service exit interviews require the employee's user account to still be active (not yet deactivated by offboarding completion).
- Only free/open-source libraries are used.
- The system uses PostgreSQL with RLS as defense-in-depth for tenant isolation.

## 11. Test Hints
- **Happy path (HR-conducted):** Record an exit interview with 10 questions; verify all responses persisted with correct `tenant_id` and offboarding linkage.
- **Happy path (self-service):** Log in as departing employee, complete the questionnaire; verify HR receives SignalR notification and email.
- **Duplicate interview:** Attempt to record a second exit interview for the same offboarding; expect validation error.
- **Analytics:** Record 10 exit interviews with varying reasons; verify pie chart accurately shows reason distribution.
- **Tenant isolation:** Record exit interviews in Tenant A and B; verify analytics in Tenant A show only Tenant A data.
- **Immutability:** Submit an exit interview, then attempt to edit; verify original is preserved and a new version is created.
- **Responsive:** Test the questionnaire at 360px width; verify rating selectors are touch-friendly.
- **Self-service deadline:** Attempt self-service interview after LWD (account deactivated); verify access is denied.
