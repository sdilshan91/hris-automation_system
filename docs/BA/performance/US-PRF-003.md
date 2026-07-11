---
id: US-PRF-003
module: Performance Management
priority: Must Have
persona: Manager
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-PRF-003: Manager Rates Employee Performance

## 1. Description
**As a** Manager,
**I want to** rate each of my direct reports against their assigned goals/KPIs after they have completed their self-assessment,
**So that** I can provide a fair, evidence-based evaluation that contributes to the final performance score and supports decisions on promotions, bonuses, and development plans.

## 2. Preconditions
- The manager is authenticated and has the `Performance.Review.Team` permission.
- The appraisal cycle's manager-review window is open.
- The employee has submitted their self-assessment (or the self-assessment window has closed, whichever applies per tenant configuration).
- Goals exist for the employee in the current cycle.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The manager-review window is open and the employee has submitted self-assessment | The manager navigates to Performance > Team Reviews and selects an employee | The system displays each goal with the employee's self-rating and comments alongside empty fields for manager rating and manager comments |
| AC-2 | The manager has rated all goals and provided comments | The manager clicks "Submit Manager Review" | The system saves the manager ratings, calculates the weighted manager score, updates the review status to "Manager Review Submitted", and notifies the employee |
| AC-3 | The manager attempts to submit without rating all goals | The manager clicks "Submit Manager Review" | The system displays a validation error listing unrated goals and prevents submission |
| AC-4 | The manager wants to view the overall team review status | The manager navigates to Performance > Team Reviews | The system displays a summary table showing each team member's review status (pending self-assessment, self-assessment submitted, manager review pending, completed) with color-coded indicators |
| AC-5 | The manager review has been submitted | The manager attempts to edit the submitted review | The system displays a read-only view; editing is only possible if HR reopens the review |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system shall display the employee's self-rating and comments alongside each goal for manager reference during rating.
- FR-2: Manager rating shall use the same tenant-configured rating scale as the self-assessment.
- FR-3: The system shall require a manager comment (minimum 20 characters) for each goal.
- FR-4: The system shall calculate the final weighted score using the tenant-configured self vs. manager weight ratio (e.g., 30% self + 70% manager).
- FR-5: The system shall support an overall review summary comment from the manager (max 5000 characters).
- FR-6: The system shall allow the manager to flag an employee for recognition, promotion consideration, or performance improvement.
- FR-7: All rating submissions shall be audit-logged with the manager's user ID and timestamp.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: The review form for a single employee shall load within 400ms (P95), including self-assessment data.
- NFR-2: All review data shall be tenant-isolated via PostgreSQL RLS; a manager shall only access reviews for their direct reports within their tenant.
- NFR-3: The system shall use optimistic concurrency to prevent lost updates if HR and the manager edit simultaneously.
- NFR-4: The UI shall conform to WCAG 2.1 AA accessibility standards, including keyboard navigation for all rating inputs.

## 6. Business Rules
- BR-1: Manager review can only be submitted during the manager-review phase of the appraisal cycle.
- BR-2: A manager can only rate employees who are their direct reports in the org tree.
- BR-3: HR Officers with `Performance.Review.All` can rate any employee and reopen submitted reviews.
- BR-4: The final score is computed as: `(self_score * self_weight) + (manager_score * manager_weight)` where weights are tenant-configurable.
- BR-5: If the tenant has 360-degree feedback enabled, the final score calculation also incorporates peer/report ratings with their configured weights.

## 7. Data Requirements
- **Input:** goal ID, manager rating (per scale), manager comment (text), overall summary comment, flag (recognition/promotion/PIP), cycle ID, employee ID.
- **Output:** manager review record with weighted manager score, final combined score, review status, audit timestamps, tenant_id.
- **Storage:** Review table with foreign keys to goal, cycle, employee, and manager; tenant_id column with RLS policy.

## 8. UI/UX Notes
- Side-by-side comparison layout: employee self-rating on the left, manager rating on the right.
- Notion-like card per goal with expandable comments section.
- Color-coded rating badges (green for high, yellow for mid, red for low).
- Team review dashboard with sortable/filterable table and status chips.
- Chart.js radar chart showing self vs. manager ratings per competency category.
- Mobile: stacked layout replacing side-by-side, with swipeable goal cards.

## 9. Dependencies
- US-PRF-001: Goals must exist for the employee.
- US-PRF-002: Employee self-assessment should be completed (or window closed).
- US-PRF-004: Appraisal cycle must define the manager-review window.
- Core HR: org tree for manager-report relationships.
- Notification system for status change alerts.

## 10. Assumptions & Constraints
- The org tree is the single source of truth for determining who a manager can review.
- Rating scales do not change mid-cycle; they are locked when the cycle begins.
- The final combined score is recalculated if 360-degree ratings are added later (US-PRF-005).
- ASP.NET Core 10 backend with MediatR CQRS pattern for review submission commands.

## 11. Test Hints
- Verify tenant isolation: manager in Tenant A cannot see reviews from Tenant B.
- Verify scope isolation: manager can only see direct reports, not employees in other teams.
- Test final score calculation with various self/manager weight ratios (50:50, 30:70, 0:100).
- Test submission without completing all goal ratings; confirm validation error.
- Test concurrent editing: HR and manager editing the same review simultaneously.
- Test the review status workflow: pending -> self-assessment submitted -> manager review submitted.
- Verify audit log entries for all manager rating actions.
- Test mobile layout at 360px for the side-by-side comparison fallback.
