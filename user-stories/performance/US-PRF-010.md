---
id: US-PRF-010
module: Performance Management
priority: Could Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-PRF-010: Performance-Based Recommendations (Promotion, Bonus)

## 1. Description
**As an** HR Officer,
**I want to** generate and manage performance-based recommendations for promotions, bonuses, salary increments, and other rewards linked to appraisal outcomes,
**So that** the organization can make fair, data-driven talent decisions that recognize high performers, retain key employees, and align compensation with measured contributions.

## 2. Preconditions
- The HR Officer is authenticated and has `Performance.Read.All` and `Performance.Publish.All` permissions.
- At least one appraisal cycle has been completed with final ratings published.
- The calibration phase (if enabled) has been completed for the cycle.
- The Performance module is enabled for the tenant.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A completed appraisal cycle with published final ratings exists | The HR Officer navigates to Performance > Recommendations | The system displays a recommendation workspace listing all employees with their final performance score, current grade/band, tenure, compensation data, manager recommendation flags (from US-PRF-003), and empty recommendation fields |
| AC-2 | HR wants to generate bulk recommendations based on rating thresholds | HR clicks "Auto-Generate Recommendations" and configures rules (e.g., rating >= 4.5 -> promotion eligible, rating >= 3.5 -> bonus eligible) | The system applies the rules across all employees in the cycle, populates recommendation types (promotion, bonus, increment, training nomination, PIP referral), and displays results for HR review and adjustment |
| AC-3 | HR has reviewed and finalized recommendations for an employee | HR clicks "Submit Recommendation" for the employee | The system saves the recommendation, links it to the employee's performance record, and routes it through the tenant's approval workflow (if configured) |
| AC-4 | All recommendations have been submitted and approved | HR navigates to the recommendation summary | The system displays aggregate statistics: total promotions recommended, total bonus pool allocated, increment distribution by department, and a comparison with the previous cycle's recommendations |
| AC-5 | A manager views recommendations for their team | The manager navigates to Performance > Team Recommendations | The system displays only the manager's direct reports with their final scores and the recommendation status (pending, approved, rejected), without revealing recommendations for other teams |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system shall support recommendation types: promotion, bonus (with amount/percentage), salary increment (with amount/percentage), training nomination, lateral move, PIP referral, and custom types (tenant-configurable).
- FR-2: The system shall support rule-based auto-generation of recommendations based on configurable rating thresholds and criteria.
- FR-3: The system shall allow HR to manually override auto-generated recommendations with a mandatory justification comment.
- FR-4: The system shall integrate with the tenant's approval workflow engine for recommendation approvals (sequential or parallel approvers).
- FR-5: The system shall provide a comparison view showing current vs. recommended grade, title, and compensation for each employee.
- FR-6: The system shall generate a recommendation summary report (PDF/Excel) with aggregate statistics for leadership review.
- FR-7: The system shall maintain a history of all recommendations across cycles for trend analysis.
- FR-8: The system shall support budget tracking: HR can set a bonus/increment budget and the system tracks consumed vs. remaining as recommendations are made.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Auto-generation of recommendations for up to 5,000 employees shall complete within 10 seconds.
- NFR-2: All recommendation data shall be tenant-isolated via PostgreSQL RLS.
- NFR-3: Compensation data within recommendations shall be encrypted at rest using pgcrypto, as it constitutes sensitive financial PII.
- NFR-4: The recommendation workspace shall load within 2.5 seconds (P95) with paginated employee list.
- NFR-5: Access to recommendation data shall be restricted to users with explicit Performance.Publish.All or Performance.Read.Team permissions; no general employee access.

## 6. Business Rules
- BR-1: Recommendations can only be generated after the appraisal cycle's final ratings are published.
- BR-2: If calibration is enabled for the cycle, recommendations can only proceed after calibration is completed.
- BR-3: Auto-generated recommendations are suggestions only; HR must review and submit each one (or batch-submit after review).
- BR-4: Budget tracking enforces a warning (not a hard block) when the allocated budget is exceeded, allowing HR to proceed with justification.
- BR-5: Promotion recommendations must include the target grade/band and effective date.
- BR-6: Approved recommendations feed into downstream modules: promotions update Core HR records, bonuses feed into Payroll as one-time earnings, training nominations create records in the Training module.
- BR-7: Recommendation history is retained indefinitely for compliance and trend analysis.

## 7. Data Requirements
- **Input:** employee_id, cycle_id, recommendation type, details (target grade, bonus amount, increment percentage, training course), justification, budget allocation.
- **Output:** recommendation record with status (draft, submitted, pending approval, approved, rejected), approval history, aggregate statistics, tenant_id.
- **Storage:** recommendations table with employee_id, cycle_id, type, details (JSONB), status, approver chain, budget_id FK, tenant_id with RLS policy. Sensitive compensation fields encrypted.

## 8. UI/UX Notes
- Notion-like workspace with a data table (sortable, filterable) listing all employees and their recommendation status.
- Inline editing for recommendation type, amount, and justification.
- Auto-generation wizard: step-by-step rule configuration with preview before applying.
- Budget tracker widget: horizontal progress bar showing consumed vs. remaining budget with color thresholds (green/amber/red).
- Comparison cards: current state vs. recommended state side-by-side per employee.
- Chart.js bar charts for aggregate statistics (promotions by department, increment distribution).
- Mobile: condensed table with swipeable row actions, bottom-sheet for editing recommendations.

## 9. Dependencies
- US-PRF-003: Manager review with recommendation flags.
- US-PRF-004: Completed appraisal cycle with published final ratings.
- US-PRF-007: Performance dashboard (recommendations feed into analytics).
- Approval workflow engine (section 34 of technical document).
- Core HR: employee grade, band, compensation data for promotion/increment processing.
- Payroll module: bonus and increment amounts feed into payroll runs.
- Training module: training nominations create enrollment records.

## 10. Assumptions & Constraints
- The approval workflow for recommendations uses the same workflow engine as other modules (leave, attendance, etc.).
- Downstream module updates (Core HR, Payroll, Training) are triggered after recommendation approval, not at submission time.
- Budget allocation and tracking is optional; tenants can disable it.
- Compensation data access is additionally restricted by role; not all HR Officers may have compensation visibility (tenant-configurable).
- Auto-generation rules are tenant-specific and stored as configuration, not hard-coded.

## 11. Test Hints
- Verify tenant isolation: recommendations in Tenant A are not visible from Tenant B.
- Verify access control: employees cannot view any recommendations; managers see only their team.
- Test auto-generation: configure rules (rating >= 4 -> bonus), run auto-generation, verify correct employees are flagged.
- Test manual override: auto-generate, then manually change a recommendation, verify justification is required.
- Test approval workflow: submit a recommendation, verify it routes through configured approvers.
- Test budget tracking: set a budget of $100,000, add recommendations totaling $110,000, verify warning is displayed.
- Test downstream integration: approve a promotion, verify Core HR record is updated with new grade and effective date.
- Test compensation encryption: query database directly, verify bonus amounts and increment values are encrypted.
- Test aggregate report export: generate PDF/Excel, verify statistics match dashboard.
- Test historical comparison: complete two cycles, verify trend comparison shows correct data.
