---
id: US-ADM-011
module: Admin Console — Tenant Admin
priority: Must Have
persona: Tenant Admin / System
status: draft
created: 2026-07-06
sprint: backlog
acceptance_criteria_count: 8
---

# US-ADM-011: Approval-Workflow RUNTIME Engine (Instances, Routing, SLA-Escalation, Delegation)

> **Reconciliation story (COMPLETION-PLAN Theme C).** US-ADM-007 built the *design-time* half —
> `WorkflowEvaluator`, versioned definitions, and the editor UI. But **runtime is inert**:
> `WorkflowInstanceId` is always null, `inFlightCount` is hard-coded 0, and there is no instance table.
> Configured workflows are authorable but never actually route a live request. This story builds the
> runtime engine and wires Leave, Attendance-regularization, and Offer approvals through it. It unblocks
> the multi-level ACs deferred on US-LV-005 (AC-4), US-ATT-004 (AC-4), and US-REC-007 (FR-10), plus
> SLA-escalation and delegation promised in US-ADM-007 (AC-5/BR-4).

## 1. Description
**As a** Tenant Admin (whose configured workflows must actually govern requests) and the platform itself,
**I want** a runtime engine that instantiates a workflow per submitted request, routes it through the
configured multi-level steps, evaluates conditions, enforces SLA timers with auto-escalation, and applies
delegation,
**So that** approval requests are processed according to the tenant's authored workflow definitions instead
of the current single-step hard-coded path.

## 2. Preconditions
- US-ADM-007 workflow definitions (versioned, with steps/conditions/SLAs/escalation/delegation) exist and are authorable.
- Approver-eligible users/roles exist in the tenant (US-ADM-005).
- The notification delivery layer (US-NTF-006) exists so SLA/escalation/step alerts can actually be sent.
- Background jobs (Hangfire) are available for SLA-timer evaluation.

## 3. Acceptance Criteria (IEEE 830 §3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A tenant has an active workflow definition for an entity type (e.g. Leave) | A request of that type is submitted | A **workflow instance** row is created, bound to the definition **version** in effect at submission, positioned at the first applicable step; the request's `WorkflowInstanceId` is set (no longer null) and the request status reflects "pending step 1". |
| AC-2 | A request is at step N with a defined approver | The step's approver approves | The instance advances to step N+1 (or completes if last), a step-instance record captures who/when/decision, and the next approver is notified via US-NTF-006. On the final approval the underlying request transitions to Approved. |
| AC-3 | A workflow step carries a condition (e.g. "only if days_requested > 5") | The engine evaluates the step against the request's data | If the condition is not met the step is **skipped** and the instance moves to the next applicable step; the skip is recorded on the instance. |
| AC-4 | A parallel step has multiple approvers | Approvers act | The instance only advances when **all** parallel approvers have approved; any rejection short-circuits the instance to Rejected. |
| AC-5 | A step's SLA elapses without a decision | The SLA-timer job runs | The engine auto-escalates to the configured escalation target (or notifies the Tenant Admin if none), records the escalation on the instance, and notifies via US-NTF-006 — matching US-ADM-007 BR-4. |
| AC-6 | A step's primary approver has an active approved leave and delegation is configured | The request reaches that step | The engine routes the approval to the designated backup approver and records the delegation on the instance (US-ADM-007 AC-5). |
| AC-7 | An in-flight instance exists under definition v1 and an admin edits the definition to v2 | The edit is saved | In-flight instances continue on v1; only requests submitted after the edit instantiate v2 (US-ADM-007 AC-3/FR-3). `inFlightCount` reflects the real count of live instances (no longer hard-coded 0). |
| AC-8 | Two tenants each run workflow instances | Any query/action on instances runs | Instances, step records, and `inFlightCount` are tenant-isolated; Tenant A can neither see nor act on Tenant B's instances. |

## 4. Functional Requirements (IEEE 830 §3.2)
- FR-1: Introduce a `workflow_instance` table (tenant-scoped): `instance_id`, `tenant_id`, `definition_id`, `definition_version`, `entity_type`, `entity_id`, `current_step_order`, `status` (InProgress/Approved/Rejected/Escalated/Cancelled), `created_at`, `completed_at`.
- FR-2: Introduce a `workflow_step_instance` table: `instance_id`, `step_order`, `assigned_approver`, `decision` (Pending/Approved/Rejected/Skipped/Delegated/Escalated), `decided_by`, `decided_at`, `is_parallel`, `sla_due_at`.
- FR-3: On request submission, resolve the active definition for the entity type, snapshot its version, create an instance, and set the request's `WorkflowInstanceId`.
- FR-4: Advance/complete/reject the instance on approver decisions; on completion, transition the underlying domain request (leave/regularization/offer) to its terminal state.
- FR-5: Evaluate step conditions at runtime against the request data and skip non-matching steps (reuse the design-time `WorkflowEvaluator` condition logic).
- FR-6: Enforce parallel steps (all-approve-to-advance, any-reject-to-fail).
- FR-7: A recurring SLA-timer job scans steps whose `sla_due_at` has passed and are still Pending, and performs auto-escalation per definition.
- FR-8: Apply delegation: when a step's primary approver is on active approved leave (and delegation is enabled), assign the backup approver.
- FR-9: Compute real `inFlightCount` per definition/entity-type from live instances; block definition deletion while in-flight instances exist (US-ADM-007 BR-6).
- FR-10: Wire Leave approval (US-LV-005), Attendance regularization (US-ATT-004), and Offer approval (US-REC-007) submission/approval paths through the engine, replacing their current single-step/`false`-defaulted routing.
- FR-11: Emit a notification via US-NTF-006 on each step assignment, escalation, delegation, and final decision.

## 5. Non-Functional Requirements (IEEE 830 §3.3)
- NFR-1: Instance creation and step-advance operations SHALL complete within 200ms (excluding async notification).
- NFR-2: All instance/step data SHALL be tenant-isolated (EF query filters + RLS once enabled).
- NFR-3: The SLA-timer job SHALL evaluate due steps at a bounded cadence (e.g. every 5 min) and be idempotent (a step is escalated at most once).
- NFR-4: State transitions on an instance SHALL be atomic and, on Postgres, run inside the retry-safe execution strategy (avoid the BUG-068 manual-transaction class).
- NFR-5: All instance actions (create/approve/reject/skip/escalate/delegate) SHALL be audited to the tenant `audit_log`.

## 6. Business Rules
- BR-1: An instance is permanently bound to the definition version it was created under; later edits do not retroactively change it.
- BR-2: A rejection at any step (or any approver of a parallel step) terminates the instance as Rejected and rejects the underlying request.
- BR-3: A skipped step (condition unmet) never blocks progress and requires no approver action.
- BR-4: SLA escalation fires at most once per step; if no escalation target is configured, the Tenant Admin is notified instead.
- BR-5: Delegation applies only when the primary approver has an active approved leave at the moment the step becomes active.
- BR-6: A definition with ≥1 in-flight instance may be archived but not deleted.
- BR-7: All runtime data is tenant-scoped; no cross-tenant instance visibility or action.

## 7. Data Requirements
- New tables: `workflow_instance`, `workflow_step_instance` (both tenant-scoped, RLS-enforced).
- Reads: workflow definitions/steps (US-ADM-007), approver users/roles (US-ADM-005), approver leave status (Leave module) for delegation.
- Writes: `WorkflowInstanceId` back-reference on leave/regularization/offer records; `audit_log`.
- Input: request submission event + data; approver decisions. Output: instance/step records, request terminal state, notifications.

## 8. UI/UX Notes
- Requesters and approvers see the current step / approval chain and its status on the request detail (leave/regularization/offer), replacing the flat single-approver view.
- Admin "Workflow" screen surfaces real in-flight counts and, optionally, an instance list per definition for troubleshooting.
- Escalation/delegation events appear in the request's history timeline.

## 9. Dependencies
- US-ADM-007 (design-time definitions/editor) — hard dependency.
- US-ADM-005 (users/roles for approver resolution).
- US-NTF-006 (delivery layer) — for step/SLA/escalation notifications.
- US-LV-005, US-ATT-004, US-REC-007 — the request paths rewired through the engine (their multi-level ACs are deferred pending this story).
- Leave module — approver leave-status lookup for delegation.

## 10. Assumptions & Constraints
- Reuses the existing `WorkflowEvaluator` condition logic; this story adds *state* (instances) and *routing*, not a new condition language.
- Phase-1 scope matches US-ADM-007 §10: sequential + parallel steps, simple conditions, SLA timers, basic delegation. Sub-workflows/loops/external approvers remain deferred.
- SLA timing granularity is bounded by the job cadence (not real-time to the second).
- Requires the retry-safe Postgres execution strategy for transactional advances (see project memory BUG-068 class).

## 11. Test Hints
- **Instance creation:** submit a leave request; verify an instance is created, version-snapshotted, `WorkflowInstanceId` set (not null).
- **Multi-level advance:** approve step 1 of a 3-step workflow; verify advance to step 2 and next-approver notification.
- **Condition skip:** submit a 3-day leave against a workflow whose step 2 triggers >5 days; verify step 2 skipped and recorded.
- **Parallel:** configure a 2-approver parallel step; verify advance only after both approve; verify one rejection fails the instance.
- **SLA escalation:** let a step's SLA elapse (mock time / advance job clock); verify single escalation + notification.
- **Delegation:** set primary approver on approved leave; verify routing to backup and delegation record.
- **Versioning:** start an instance on v1, edit definition to v2; verify in-flight stays v1, new request gets v2, `inFlightCount` accurate.
- **Tenant isolation:** create instances in two tenants; verify no cross-tenant visibility/action; `inFlightCount` per tenant.
- **Postgres transaction:** exercise advance under the retrying execution strategy on real Postgres (BUG-068 class) — not InMemory only.
