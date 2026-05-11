---
id: US-PAY-001
module: Payroll
priority: Must Have
persona: Tenant Admin / HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 6
---

# US-PAY-001: Configure Salary Structure and Components per Tenant

## 1. Description
**As a** Tenant Admin or HR Officer,
**I want to** define salary structures and their constituent components (earnings, deductions, statutory items) scoped to my tenant,
**So that** payroll calculations follow our organization's compensation framework and statutory obligations.

## 2. Preconditions
- Tenant has been provisioned and is in Active or Trial status.
- Payroll module is enabled in the tenant's subscription plan.
- User has `Payroll.*.All` permission (HR Officer) or Tenant Admin role.
- At least one active employee exists in the tenant (for assignment, not for configuration).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | HR is on the Payroll Settings page | HR creates a new salary component (e.g., "Basic Salary", type=Earning, calculation=Fixed) | The component is saved with `tenant_id` and appears in the component list for this tenant only |
| AC-2 | A salary component exists | HR edits the component name, type, or calculation method | The component is updated and all future payroll runs use the updated definition; historical payslips remain unchanged |
| AC-3 | HR is on the Salary Structures page | HR creates a salary structure (e.g., "Full-Time India") and adds components with rules (percentage of basic, fixed amount, formula) | The structure is saved and available for assignment to employees |
| AC-4 | A salary structure has components | HR reorders component processing priority | Components are processed in the specified order during payroll calculation |
| AC-5 | A salary component is in use by active employees | HR attempts to delete the component | The system prevents deletion and displays an error message listing the count of affected employees |
| AC-6 | User from Tenant A is logged in | User attempts to view or modify salary components | Only components belonging to Tenant A are visible; RLS enforces isolation at the database level |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL allow CRUD operations on salary components with fields: name, code (unique per tenant), type (Earning | Deduction | Statutory | Reimbursement), calculation method (Fixed | Percentage of Basic | Percentage of Gross | Formula), default amount/percentage, taxability flag, is_active flag.
- FR-2: The system SHALL allow CRUD operations on salary structures with fields: name, code (unique per tenant), description, effective_from date, is_default flag, is_active flag.
- FR-3: The system SHALL allow linking multiple salary components to a salary structure with per-component overrides: amount/percentage, processing order, mandatory flag.
- FR-4: The system SHALL support formula-based components using a safe expression evaluator (e.g., `basic * 0.12` for PF).
- FR-5: The system SHALL validate that at least one earning component exists in a salary structure before it can be marked active.
- FR-6: The system SHALL support cloning an existing salary structure to create a new one.
- FR-7: The system SHALL maintain version history of salary structure changes with effective dates.
- FR-8: All salary component and structure records SHALL carry `tenant_id` and be governed by PostgreSQL RLS policies.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Salary structure and component lists SHALL be cached in Redis with a TTL of 15 minutes; cache invalidated on any write operation (as per technical doc caching strategy).
- NFR-2: API response time for fetching all salary components for a tenant SHALL be <= 200ms (P95).
- NFR-3: The salary component/structure APIs SHALL support pagination (default 25, max 100 per page).
- NFR-4: Test coverage for salary structure configuration logic SHALL be >= 85% (critical module requirement).
- NFR-5: All write operations on salary components and structures SHALL be audit-logged per section 24 of the technical document.

## 6. Business Rules
- BR-1: Each tenant can have multiple salary structures but only ONE can be marked as the default structure.
- BR-2: A salary component code must be unique within a tenant (e.g., only one "BASIC" component per tenant).
- BR-3: Statutory components (e.g., EPF, ETF, PAYE tax) must be flagged as `is_statutory = true` and cannot be removed from a structure if statutory compliance is enabled for the tenant.
- BR-4: Deduction components cannot exceed the gross earnings total when evaluated; the system must validate this constraint during structure definition.
- BR-5: A salary structure can only be deactivated if no active employees are currently assigned to it, OR the admin explicitly reassigns them first.
- BR-6: Formula expressions must be validated for syntax and circular references before saving.
- BR-7: Component types cannot be changed after the component has been used in a finalized payroll run.

## 7. Data Requirements

**salary_component table:**
| Column | Type | Constraints |
|--------|------|-------------|
| salary_component_id | uuid (PK) | NOT NULL |
| tenant_id | uuid (FK) | NOT NULL, RLS-enforced |
| name | varchar(100) | NOT NULL |
| code | varchar(50) | NOT NULL, UNIQUE per tenant |
| type | enum | Earning, Deduction, Statutory, Reimbursement |
| calculation_method | enum | Fixed, PercentageOfBasic, PercentageOfGross, Formula |
| default_value | numeric(18,2) | nullable |
| formula_expression | text | nullable |
| is_taxable | boolean | default true |
| is_statutory | boolean | default false |
| is_active | boolean | default true |
| processing_order | int | default 0 |
| created_at | timestamptz | NOT NULL |
| updated_at | timestamptz | NOT NULL |
| created_by | uuid | NOT NULL |

**salary_structure table:**
| Column | Type | Constraints |
|--------|------|-------------|
| salary_structure_id | uuid (PK) | NOT NULL |
| tenant_id | uuid (FK) | NOT NULL, RLS-enforced |
| name | varchar(100) | NOT NULL |
| code | varchar(50) | NOT NULL, UNIQUE per tenant |
| description | text | nullable |
| effective_from | date | NOT NULL |
| is_default | boolean | default false |
| is_active | boolean | default true |
| created_at | timestamptz | NOT NULL |
| updated_at | timestamptz | NOT NULL |

**salary_structure_component (junction table):**
| Column | Type | Constraints |
|--------|------|-------------|
| salary_structure_component_id | uuid (PK) | NOT NULL |
| tenant_id | uuid (FK) | NOT NULL, RLS-enforced |
| salary_structure_id | uuid (FK) | NOT NULL |
| salary_component_id | uuid (FK) | NOT NULL |
| override_value | numeric(18,2) | nullable |
| override_formula | text | nullable |
| processing_order | int | NOT NULL |
| is_mandatory | boolean | default false |

## 8. UI/UX Notes (Notion-like)
- Use a clean, card-based layout for salary structures list with status badges (Active/Inactive).
- Component configuration should use an inline-editable table (Notion-style database view) with drag-and-drop reordering for processing order.
- Formula editor should provide syntax highlighting and a "Test" button that evaluates the formula against sample values.
- Salary structure detail page should show a breakdown preview: a mock payslip calculated from the structure's components using sample base values.
- Use a slide-over panel (not a full page navigation) for creating/editing individual components.
- Mobile responsive: stack columns vertically on screens < 768px; component list scrollable horizontally if needed.
- Tenant branding (primary color) applied to action buttons and headers.

## 9. Dependencies
- **US-CORE-xxx**: Core HR module must provide employee records and department data.
- **US-AUTH-xxx**: Authentication and RBAC must be in place for permission checks (`Payroll.*.All`).
- **US-TENANT-xxx**: Tenant provisioning and module toggle (Payroll enabled).
- No dependency on leave or attendance modules for this configuration story.

## 10. Assumptions & Constraints
- Phase 1 supports statutory configuration for one country only (as per technical doc section 3.2).
- Formula expressions use a safe subset of mathematical operations; arbitrary code execution is not permitted.
- All monetary values stored as `numeric(18,2)` in PostgreSQL to avoid floating-point precision issues.
- Redis is available at `localhost:6379` for caching salary structures.
- Free/open-source expression evaluator library will be used for formula parsing (e.g., NCalc or similar).

## 11. Test Hints
- Unit test: Validate formula parser rejects circular references and invalid syntax.
- Unit test: Verify unique constraint on component code within a tenant (two tenants can have same code).
- Integration test: Create component in Tenant A, verify it is invisible from Tenant B's API context.
- Integration test: Attempt to delete a component assigned to active employees and verify 409 Conflict response.
- Integration test: Verify Redis cache is invalidated when a salary component is updated.
- E2E (Playwright): Create a salary structure, add components, reorder via drag-and-drop, verify display order persists.
- Performance test: Load 200 salary components for a tenant and verify API response <= 200ms.
