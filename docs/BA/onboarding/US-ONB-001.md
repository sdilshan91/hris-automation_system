---
id: US-ONB-001
module: Onboarding / Offboarding
priority: Must Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-ONB-001: Create Onboarding Checklist Template

## 1. Description
**As an** HR Officer,
**I want to** create reusable onboarding checklist templates with configurable tasks grouped by category,
**So that** every new hire receives a consistent, role-appropriate set of onboarding activities and nothing is missed during their joining process.

## 2. Preconditions
- The HR Officer is authenticated and has an active session within their tenant (subdomain resolved).
- The Onboarding/Offboarding module is enabled for the tenant's subscription plan.
- At least one department exists in the tenant.
- The user has the `Onboarding.Manage` permission.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | HR Officer navigates to the Onboarding module | They click "Create Checklist Template" | A form opens allowing entry of template name, description, applicable department(s), applicable job title(s), and a task list builder. |
| AC-2 | HR Officer adds tasks to the template with category groupings (e.g., "Documentation", "IT Setup", "Training") | They save the template | The template is persisted with all tasks, each having: title, description, category, responsible party (role or named user), due offset (days from joining date), and mandatory flag. The `tenant_id` is set from the session context. |
| AC-3 | HR Officer creates a template with a name that already exists in the same tenant | They attempt to save | The system displays a validation error: "A checklist template with this name already exists." |
| AC-4 | HR Officer marks certain tasks as mandatory | They save the template | Those tasks are flagged as mandatory and cannot be skipped when the checklist is assigned to a new hire. |
| AC-5 | A user from Tenant B queries the onboarding templates API | The request is processed | Only Tenant B templates are returned; Tenant A templates are invisible due to RLS policies and EF Core global query filters. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide a checklist template builder with drag-and-drop task ordering within categories.
- FR-2: The system SHALL support task categories as free-text labels (e.g., "Documentation", "IT Setup", "Training", "Compliance").
- FR-3: Each task SHALL have: title (required), description (optional), category, responsible party (role-based or named user), due offset in days from date of joining, mandatory flag, and sort order.
- FR-4: The system SHALL allow templates to be scoped to specific departments and/or job titles, or marked as "universal" (applies to all).
- FR-5: The system SHALL set `tenant_id` from the authenticated session context (never from user input).
- FR-6: The system SHALL support cloning an existing template to create a new one.
- FR-7: The system SHALL support activating/deactivating templates (soft status toggle) without deletion.
- FR-8: The system SHALL populate audit columns (`created_at`, `created_by`, `updated_at`, `updated_by`) automatically.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Template creation API response time SHALL be <= 500 ms (P95).
- NFR-2: All template data SHALL be isolated by tenant via PostgreSQL RLS policies and EF Core global query filters.
- NFR-3: The template builder form SHALL be fully responsive from 360px to 4K resolution.
- NFR-4: The form SHALL meet WCAG 2.1 AA accessibility standards (keyboard navigable drag-and-drop, screen-reader friendly).
- NFR-5: Template data changes SHALL be recorded in the tenant audit log via EF Core `SaveChangesInterceptor`.

## 6. Business Rules
- BR-1: Template names must be unique within a tenant but may repeat across tenants.
- BR-2: A template must contain at least one task to be saved.
- BR-3: Due offset must be a non-negative integer (0 = same day as joining).
- BR-4: Deactivated templates cannot be assigned to new hires but remain visible for historical reference.
- BR-5: Deleting a template uses soft delete (`is_deleted = true`); templates already assigned to employees remain unaffected.

## 7. Data Requirements
**Input fields:**
| Field | Type | Required | Validation |
|-------|------|----------|------------|
| template_name | varchar(200) | Yes | Min 3, Max 200 chars, unique per tenant |
| description | text | No | Max 2000 chars |
| applicable_departments | uuid[] | No | Must exist in tenant; empty = universal |
| applicable_job_titles | uuid[] | No | Must exist in tenant; empty = universal |
| is_active | boolean | No | Default: true |
| tasks[].title | varchar(200) | Yes | Min 3, Max 200 chars |
| tasks[].description | text | No | Max 1000 chars |
| tasks[].category | varchar(100) | No | Free-text label |
| tasks[].responsible_role | varchar(50) | No | One of: HR, Manager, IT, Employee, Custom |
| tasks[].responsible_user_id | uuid | No | Must exist in tenant |
| tasks[].due_offset_days | int | Yes | >= 0 |
| tasks[].is_mandatory | boolean | No | Default: false |
| tasks[].sort_order | int | Yes | >= 0 |

**Output:** Created template object with `template_id`, all persisted fields, and nested task list.

## 8. UI/UX Notes
- Use a Notion-like card-based layout for the template builder; each task is a draggable card within its category group.
- Categories are collapsible sections with a subtle divider and category label in bold.
- Drag handle icon on the left of each task card; drag-and-drop reorders within and across categories.
- "Add Task" button at the bottom of each category, plus a floating "+" button for adding new categories.
- Use Tailwind-styled Angular Material inputs with floating labels for all fields.
- On mobile (< 768px): tasks stack vertically; drag handle replaced with up/down arrow buttons.
- Success toast with slide-in animation on save. Validation errors inline below fields.
- Clone button on existing templates opens a pre-filled form with "(Copy)" appended to the name.

## 9. Dependencies
- US-CHR-004: Departments must exist for department scoping.
- US-CHR-005: Job titles must exist for job title scoping.
- Authentication module: User must be authenticated with valid tenant context.
- Tenant subscription must have Onboarding/Offboarding module enabled.

## 10. Assumptions & Constraints
- The Onboarding/Offboarding module is a toggleable module per the tenant's subscription plan.
- Templates are tenant-scoped; there are no system-wide default templates in Phase 1 (each tenant creates their own).
- Only free/open-source libraries are used (e.g., Angular CDK drag-drop for task reordering).
- The system uses PostgreSQL with RLS as defense-in-depth for tenant isolation.

## 11. Test Hints
- **Happy path:** Create a template with 3 categories and 8 tasks; verify DB persistence with correct `tenant_id`.
- **Duplicate name:** Attempt to create two templates with the same name in the same tenant; expect validation error. Verify the same name succeeds in a different tenant.
- **Tenant isolation:** Create templates in Tenant A and Tenant B; query from Tenant A must not return Tenant B templates.
- **Clone:** Clone an existing template; verify all tasks are duplicated with a new `template_id`.
- **Deactivation:** Deactivate a template; verify it no longer appears in the "assign to new hire" dropdown but is still visible in the template list.
- **Responsive:** Test the drag-and-drop builder at 360px, 768px, 1024px, and 1920px widths.
- **Accessibility:** Navigate the task builder using keyboard only; verify drag-and-drop alternative (up/down buttons) works.
