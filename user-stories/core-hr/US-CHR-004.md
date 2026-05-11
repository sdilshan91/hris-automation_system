---
id: US-CHR-004
module: Core HR
priority: Must Have
persona: Tenant Admin / HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-CHR-004: Create and Manage Departments

## 1. Description
**As a** Tenant Admin or HR Officer,
**I want to** create, view, edit, and deactivate departments with hierarchical parent-child relationships,
**So that** the organization structure is accurately represented and employees can be assigned to the correct departments.

## 2. Preconditions
- The user is authenticated with Tenant Admin or HR Officer role within their tenant.
- Tenant context is resolved from the subdomain.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A Tenant Admin navigates to the Departments management page | They click "Add Department" | A form/modal appears with fields: Department Name (required), Parent Department (optional dropdown), Department Manager (optional employee picker), Description, and Status. |
| AC-2 | The admin enters a valid department name and submits | The form is saved | A new department record is created with `tenant_id` set from session context, name unique within the tenant, and the department appears in the department list and hierarchy tree. |
| AC-3 | The admin enters a department name that already exists in the tenant | They submit the form | The system rejects with: "A department with this name already exists." The same name is allowed in a different tenant. |
| AC-4 | The admin edits an existing department and changes the parent department | They save | The hierarchy is updated; the org tree visualization reflects the new parent-child relationship; all employees in the department retain their assignment. |
| AC-5 | The admin attempts to deactivate a department that has active employees assigned | They click "Deactivate" | The system warns: "This department has X active employees. Please reassign them before deactivating." and blocks the deactivation until employees are reassigned. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL support CRUD operations on departments scoped to the current tenant.
- FR-2: The system SHALL enforce unique department names within a tenant.
- FR-3: The system SHALL support hierarchical parent-child relationships via `parent_department_id` (self-referencing FK).
- FR-4: The system SHALL allow assigning a department manager (`manager_employee_id` FK to employee table).
- FR-5: The system SHALL prevent circular parent-child references (e.g., A -> B -> A).
- FR-6: The system SHALL prevent deactivation of departments with active employees until reassignment.
- FR-7: The system SHALL use soft delete for departments; deactivated departments are hidden from dropdowns but visible in admin views.
- FR-8: The system SHALL display department hierarchy as both a flat list/table and a tree view.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Department CRUD API response time SHALL be <= 400 ms for reads, <= 800 ms for writes (P95).
- NFR-2: All department data SHALL be tenant-isolated via RLS and EF Core global query filters.
- NFR-3: The department management page SHALL be fully responsive (360px to 4K).
- NFR-4: The system SHALL support up to 500 departments per tenant without degradation.
- NFR-5: Audit log entries SHALL be created for all department create, update, and deactivate operations.

## 6. Business Rules
- BR-1: Department names are unique within a tenant but may duplicate across tenants.
- BR-2: A department can have at most one manager.
- BR-3: A department's parent must belong to the same tenant.
- BR-4: Root departments (no parent) form the top level of the org tree.
- BR-5: Deactivated departments cannot be assigned to new employees.
- BR-6: Deleting a parent department requires reassigning or deactivating all child departments first.

## 7. Data Requirements
**Department table schema:**
| Column | Type | Required | Notes |
|--------|------|----------|-------|
| department_id | uuid (PK) | Auto | |
| tenant_id | uuid (FK) | Auto | Set from session |
| name | varchar(150) | Yes | Unique per tenant |
| parent_department_id | uuid (FK) | No | Self-referencing |
| manager_employee_id | uuid (FK) | No | Nullable |
| description | text | No | |
| is_active | boolean | Yes | Default: true |
| created_at / updated_at | timestamptz | Auto | |
| created_by / updated_by | uuid | Auto | |
| is_deleted | boolean | Auto | Default: false |

## 8. UI/UX Notes (Notion-like, cards-based)
- Department list page: card-based table with columns for Name, Parent, Manager, Employee Count, Status.
- "Add Department" button in the top-right with a `+` icon.
- Create/edit form as a slide-over panel (right side, 400px width) or modal card with smooth slide-in animation (300ms ease-out).
- Parent department field: searchable dropdown with hierarchy indentation (indent child departments with dashes or tree lines).
- Manager field: employee search/autocomplete with avatar + name display.
- Department tree view toggle: show departments as an interactive tree (expand/collapse nodes) using a tree component, each node as a small card.
- Row actions: Edit (pencil icon), Deactivate (archive icon), with confirmation dialog for deactivate.
- On mobile: table collapses to card list; tree view uses collapsible accordions.

## 9. Dependencies
- US-CHR-001: Employees are referenced as department managers.
- US-CHR-006: Organization tree visualization consumes department hierarchy data.
- Authentication & Authorization: Role-based access to department management.

## 10. Assumptions & Constraints
- The department hierarchy depth is not explicitly limited but is expected to be <= 10 levels for practical use.
- Circular reference detection is performed server-side before persisting.
- Department changes do not trigger automatic employee notifications (this can be added as a future enhancement).

## 11. Test Hints
- **Create department:** Create a root department and a child department; verify parent-child FK is correct.
- **Duplicate name:** Attempt to create two departments with the same name in one tenant; expect error. Verify it succeeds in another tenant.
- **Circular reference:** Create A -> B -> C; attempt to set A's parent to C; expect rejection.
- **Deactivate with employees:** Assign an employee to department D; attempt to deactivate D; expect warning/block.
- **Tenant isolation:** Create departments in Tenant A; query from Tenant B; verify zero results.
- **Hierarchy rendering:** Create 3-level hierarchy; verify tree view renders correctly with expand/collapse.
- **Audit trail:** Create and edit a department; verify audit_log entries with before/after snapshots.
