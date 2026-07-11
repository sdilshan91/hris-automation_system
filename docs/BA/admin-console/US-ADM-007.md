---
id: US-ADM-007
module: Admin Console — Tenant Admin
priority: Must Have
persona: Tenant Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-ADM-007: Tenant Admin Manages Approval Workflows

## 1. Description
**As a** Tenant Admin,
**I want to** create, edit, and manage approval workflows for different request types (leave, attendance regularization, expense, salary revision, offer) with configurable sequential or parallel steps, approver types, SLA timers, escalation rules, and conditional logic,
**So that** my organization's approval processes are codified in the system, ensuring consistency, accountability, and timely processing of all requests.

## 2. Preconditions
- The Tenant Admin is authenticated on `{subdomain}.yourhrm.com` with the `TenantAdmin` role.
- The tenant is in `active` or `trial` status.
- The tenant's plan allows the number of workflows being created (checked against `max_workflows` plan limit).
- At least one user with an approver-eligible role (Manager, HR Officer, Department Head) exists in the tenant.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The Tenant Admin navigates to the Workflows settings | The page loads | A list of configured workflows is displayed, grouped by request type (Leave, Attendance, Expense, etc.), showing workflow name, number of steps, status (active/draft/archived), and last modified date. Only workflows belonging to the current tenant are shown. Default workflows (seeded during provisioning) are marked as "Default" and can be edited. |
| AC-2 | The Tenant Admin creates a new leave approval workflow | They define: name, request type (Leave), and add sequential steps: Step 1 — Direct Manager (SLA: 24h), Step 2 — Department Head (conditional: only if leave > 5 days, SLA: 48h), Step 3 — HR (conditional: only if leave > 15 days, SLA: 24h). They set auto-escalation on SLA breach to the next level, and save | A new workflow definition record is created with the configured steps, conditions, SLAs, and escalation rules. All new leave requests in the tenant use this workflow. The workflow is versioned (v1). The action is recorded in the tenant's `audit_log`. |
| AC-3 | The Tenant Admin edits an existing active workflow | They modify a step's SLA from 24h to 48h and save | A new version of the workflow is created (v2). In-flight requests continue to use the version they were initiated under (v1). New requests use v2. The edit is audited with before/after state. |
| AC-4 | The Tenant Admin attempts to create a workflow that would exceed the plan's `max_workflows` limit | They click "Create Workflow" | The system displays: "You have reached the maximum number of workflows ({limit}) for your plan. Please upgrade or archive an existing workflow." The create action is blocked. |
| AC-5 | The Tenant Admin configures a workflow with delegation rules | They enable delegation: "If the approver is on leave, delegate to {backup approver}" for a specific step | When a request reaches that step and the primary approver has an active approved leave, the system automatically routes the approval to the designated backup approver. The delegation is recorded in the workflow instance. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The workflow definition shall consist of: `workflow_id` (UUID), `tenant_id` (UUID), `name` (string), `entity_type` (enum: Leave, Attendance, Expense, Offer, SalaryRevision), `version` (int), `steps` (ordered array), `is_active` (boolean), `created_by`, `created_at`.
- FR-2: Each workflow step shall define: `step_order` (int), `approver_type` (enum: LineManager, Role, NamedUser, DepartmentHead), `approver_identifier` (role_id or user_id, depending on type), `is_parallel` (boolean — for parallel approval steps), `sla_hours` (int), `escalation_target` (approver_type + identifier), `condition` (optional JSON expression, e.g., `{"field": "days_requested", "operator": ">", "value": 5}`).
- FR-3: Workflow versioning: editing an active workflow creates a new version. In-flight workflow instances retain their original version reference.
- FR-4: The plan limit `max_workflows` shall be checked before creating a new workflow definition.
- FR-5: The workflow editor shall validate that at least one step exists, all approver references are valid users/roles in the tenant, and SLA values are positive integers.
- FR-6: Archiving a workflow marks it as `is_active = false`. It can be restored. Archived workflows do not count toward the plan limit.
- FR-7: All workflow definitions are scoped to the tenant via `tenant_id` and filtered by `ITenantContext`.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: The workflow editor shall load within 2 seconds, including the approver suggestion list.
- NFR-2: Workflow definitions shall be cached in Redis (`t:{tenantId}:workflows:{entityType}`) and invalidated on edit.
- NFR-3: Workflow evaluation (determining the next step for a request) shall complete in under 100ms.
- NFR-4: All workflow management actions shall be audited to the tenant's `audit_log`.
- NFR-5: The workflow editor UI shall be responsive and usable on tablet-sized screens (768px minimum for the editor).

## 6. Business Rules
- BR-1: Only `TenantAdmin` and `TenantOwner` roles may create or edit workflow definitions.
- BR-2: Each entity type can have only one active workflow at a time per tenant. Creating a new active workflow for the same entity type automatically archives the previous one.
- BR-3: Workflow steps with `is_parallel = true` require all parallel approvers to approve before the request proceeds to the next step.
- BR-4: SLA breach triggers auto-escalation to the configured escalation target. If no escalation target is defined, SLA breach triggers a notification to the Tenant Admin.
- BR-5: Conditional steps are evaluated at runtime using the request's data. If a condition is not met, the step is skipped.
- BR-6: Deletion of a workflow is only allowed if there are no in-flight instances. Otherwise, the workflow can only be archived.
- BR-7: Workflow definitions are entirely tenant-scoped; Tenant A's workflows are invisible to Tenant B.

## 7. Data Requirements
- **Input:** Workflow name, entity type, steps array (each with approver_type, identifier, sla_hours, condition, escalation_target, is_parallel), delegation rules.
- **Output:** Workflow definition with version number, step details, active status.
- **Tables affected:** Workflow definition table (tenant-scoped), workflow step table, `audit_log`.
- **Related tables:** Workflow instance (created per request), workflow step instance (per step per request).

## 8. UI/UX Notes
- Workflow editor: a visual step-by-step builder. Each step is a card that can be reordered via drag-and-drop. Cards show: step number, approver type (dropdown), approver (searchable dropdown), SLA (number input + hours label), condition builder (optional, expandable section with field/operator/value inputs), escalation (optional, expandable).
- Parallel steps: a toggle on the step card; when enabled, the card expands to allow multiple approvers for that step.
- Delegation: a toggle per step with a backup approver selector.
- A "Preview" mode that shows a flowchart visualization of the workflow (sequential and conditional paths).
- Versioning: a "Version History" panel showing previous versions with diff view.
- Notion-like design: clean step cards, smooth drag-and-drop animations, subtle transitions on expand/collapse.

## 9. Dependencies
- US-ADM-001: Tenant must be provisioned (default workflows seeded).
- US-ADM-005: Users and roles must exist for approver selection.
- Leave, Attendance, and other modules must integrate with the workflow engine for request submission and approval routing.
- Notification system for SLA breach and escalation alerts.

## 10. Assumptions & Constraints
- Phase 1 supports sequential and parallel steps, conditional logic, SLA timers, and basic delegation. Advanced workflow features (e.g., sub-workflows, loops, external approvers) are deferred.
- The condition expression language is limited to simple field comparisons (field, operator, value). Complex expressions (AND/OR groups) may be added in Phase 2.
- The workflow engine is tenant-scoped; there is no concept of a "system-wide default workflow" that applies across tenants (each tenant gets seeded defaults that they can customize independently).

## 11. Test Hints
- **Create workflow:** Create a 3-step leave approval workflow with conditions; verify the definition is stored correctly with all steps, conditions, and SLAs.
- **Versioning:** Edit an active workflow; verify a new version is created and in-flight requests still reference the old version.
- **Plan limit:** Set `max_workflows = 2`, create 2 workflows, attempt a 3rd; verify rejection.
- **Conditional step:** Submit a 3-day leave request against a workflow where Step 2 triggers only for > 5 days; verify Step 2 is skipped.
- **SLA escalation:** Submit a request, let the SLA timer expire (or mock time); verify the escalation target is notified and the request is routed.
- **Cross-tenant isolation:** As Tenant A admin, attempt to read Tenant B's workflow definitions; verify 404 or empty result.
- **Parallel approval:** Configure a parallel step with 2 approvers; verify the request only proceeds when both approve.
- **Delegation:** Configure delegation on a step, set the primary approver as on-leave; submit a request; verify it routes to the backup approver.
- **Archive and restore:** Archive a workflow; verify it no longer applies to new requests. Restore it; verify it becomes active.
