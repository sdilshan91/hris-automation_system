---
id: US-PAY-002
module: Payroll
priority: Must Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-PAY-002: Assign Salary Structure to Employee

## 1. Description
**As an** HR Officer,
**I want to** assign a salary structure to an employee with specific component values (CTC breakdown),
**So that** the employee's compensation is accurately defined and ready for payroll processing.

## 2. Preconditions
- At least one active salary structure with components exists (US-PAY-001 completed).
- Employee record exists in Core HR module with Active status.
- HR Officer has `Payroll.*.All` permission.
- Payroll module is enabled for the tenant.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An active salary structure exists | HR assigns it to an employee with a CTC of 600,000 and an effective date | The system creates `employee_salary_component` records for each component in the structure, calculated from the CTC and the component rules |
| AC-2 | An employee already has a salary structure assigned | HR assigns a new salary structure with a future effective date | The new assignment is saved with the future effective date; the current structure remains active until that date; salary revision history is maintained |
| AC-3 | HR is assigning a salary structure | HR overrides the calculated value of a specific component (e.g., sets HRA to a custom fixed amount) | The override is saved for that employee-component combination while other components retain their calculated values |
| AC-4 | HR needs to assign the same salary structure to multiple employees | HR uses the bulk assignment feature, selects employees, and confirms | All selected employees receive the structure assignment with their individual CTC values; a progress indicator shows completion |
| AC-5 | An employee belongs to Tenant A | A user from Tenant B attempts to access the employee's salary assignment | The request is denied; RLS prevents cross-tenant data access |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL allow assigning a salary structure to an employee with fields: salary_structure_id, effective_from date, annual_ctc, and optional per-component overrides.
- FR-2: The system SHALL auto-calculate component values from the annual CTC based on the structure's component rules (percentages, formulas, fixed amounts).
- FR-3: The system SHALL display a CTC breakdown preview before HR confirms the assignment.
- FR-4: The system SHALL maintain a complete history of salary structure assignments for each employee (salary revision history).
- FR-5: The system SHALL support bulk salary structure assignment for multiple employees with individual CTC values via CSV upload or multi-select UI.
- FR-6: The system SHALL validate that the sum of all component values equals the declared CTC (within a configurable tolerance, e.g., +/- 1 currency unit).
- FR-7: The system SHALL prevent assigning an inactive or deactivated salary structure to an employee.
- FR-8: All employee salary records SHALL carry `tenant_id` and be governed by PostgreSQL RLS policies.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Bulk assignment of salary structures to up to 500 employees SHALL complete within 30 seconds.
- NFR-2: CTC breakdown calculation SHALL respond in <= 500ms for preview rendering.
- NFR-3: Test coverage for salary assignment logic SHALL be >= 85%.
- NFR-4: All salary assignment changes SHALL be audit-logged with before/after values.
- NFR-5: Employee salary data SHALL be encrypted at rest in PostgreSQL (column-level or TDE).

## 6. Business Rules
- BR-1: An employee can have only ONE active salary structure at any point in time. A new assignment with a current or past effective date immediately supersedes the previous one.
- BR-2: If a salary structure assignment has a future effective date, it does not affect the current payroll until that date arrives.
- BR-3: Salary revision history must capture: old structure, new structure, old CTC, new CTC, effective date, changed_by, changed_at, and reason for change.
- BR-4: Employees on probation may have a different salary structure than confirmed employees; the system must allow this distinction.
- BR-5: An employee without any salary structure assigned SHALL be flagged as "Payroll Incomplete" and excluded from payroll runs with a clear warning.
- BR-6: The system must not allow backdating a salary assignment to a period that has a finalized payroll run, unless a payroll adjustment is created (see US-PAY-007).
- BR-7: CTC breakdown must account for employer-side statutory contributions (e.g., employer EPF, ETF) which may or may not be included in the CTC depending on tenant configuration.

## 7. Data Requirements

**employee_salary_component table:**
| Column | Type | Constraints |
|--------|------|-------------|
| employee_salary_component_id | uuid (PK) | NOT NULL |
| tenant_id | uuid (FK) | NOT NULL, RLS-enforced |
| employee_id | uuid (FK) | NOT NULL |
| salary_structure_id | uuid (FK) | NOT NULL |
| salary_component_id | uuid (FK) | NOT NULL |
| annual_amount | numeric(18,2) | NOT NULL |
| monthly_amount | numeric(18,2) | NOT NULL (computed) |
| is_override | boolean | default false |
| effective_from | date | NOT NULL |
| effective_to | date | nullable (null = current) |
| created_at | timestamptz | NOT NULL |
| updated_at | timestamptz | NOT NULL |

**salary_revision_history table:**
| Column | Type | Constraints |
|--------|------|-------------|
| revision_id | uuid (PK) | NOT NULL |
| tenant_id | uuid (FK) | NOT NULL, RLS-enforced |
| employee_id | uuid (FK) | NOT NULL |
| old_structure_id | uuid (FK) | nullable |
| new_structure_id | uuid (FK) | NOT NULL |
| old_annual_ctc | numeric(18,2) | nullable |
| new_annual_ctc | numeric(18,2) | NOT NULL |
| effective_from | date | NOT NULL |
| reason | text | nullable |
| changed_by | uuid (FK) | NOT NULL |
| changed_at | timestamptz | NOT NULL |

## 8. UI/UX Notes (Notion-like)
- Employee profile page should have a "Compensation" tab showing current salary structure, CTC, and component breakdown in a clean card layout.
- CTC breakdown preview should render as a two-column table (Component Name | Monthly Amount) with a total row, styled as a Notion-like inline table.
- Salary revision history should display as a timeline view with expandable entries showing before/after comparison.
- Bulk assignment should use a spreadsheet-like interface (Notion table view) where HR can paste CTC values next to employee names.
- Override fields should be visually distinct (highlighted border or background) to indicate non-standard values.
- Mobile: CTC breakdown card stacks vertically; bulk assignment not available on mobile (desktop only with a clear message).

## 9. Dependencies
- **US-PAY-001**: Salary structures and components must be configured first.
- **US-CORE-xxx**: Employee records must exist with Active/Probation status.
- **US-PAY-006**: Statutory deduction configuration needed for accurate CTC calculation with statutory items.

## 10. Assumptions & Constraints
- CTC is defined as annual cost; monthly amounts are derived by dividing by 12 (or by configured pay periods per year).
- Currency is configured at tenant level (single currency per tenant in Phase 1).
- The system does not handle currency conversion for multi-country tenants in Phase 1.
- CSV bulk upload format will be documented and a template provided for download.

## 11. Test Hints
- Unit test: Verify CTC breakdown calculation for a structure with Basic (40%), HRA (20% of Basic), and a fixed Conveyance allowance.
- Unit test: Verify that assigning a structure with a future effective date does not change current active structure.
- Integration test: Assign salary to Employee in Tenant A, verify invisible from Tenant B.
- Integration test: Attempt to assign an inactive salary structure and verify 400 Bad Request.
- Integration test: Bulk assign 100 employees and verify all records created with correct tenant_id.
- E2E (Playwright): Navigate to employee profile, assign salary structure, verify CTC breakdown preview, confirm, verify compensation tab shows correct data.
- Regression test: Verify salary revision history captures old and new values accurately.
