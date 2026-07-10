---
id: US-ADM-011
module: Admin Console — Tenant Admin
priority: Must Have
persona: Tenant Admin / Approver / System
status: ready
created: 2026-07-06
updated: 2026-07-09
sprint: backlog
acceptance_criteria_count: 12
---

# US-ADM-011: Approval-Workflow RUNTIME Engine (Instances, Routing, SLA-Escalation, Delegation)

> **Reconciliation story (COMPLETION-PLAN Theme C).** US-ADM-007 built the *design-time* half —
> `WorkflowDefinition`/`WorkflowStep` entities + versioning + audit (`WorkflowService`), the pure
> `WorkflowEvaluator`/`WorkflowCondition` evaluation logic, and the definition REST API/editor UI. But
> **runtime is inert**: there is **no `WorkflowInstance` entity/table**, `WorkflowInstanceId` FKs on
> `AttendanceRegularization` (line 63) and `OvertimeRecord` (line 126) are **always null**, multi-level leave
> routing is **hardcoded to level 1** (`LeaveRequestService.cs` ~372/~1050/~1086), and the delete in-flight
> guard reads `inFlightCount = 0` hardcoded (`WorkflowService.cs:363`). Configured workflows are authorable but
> never route a live request. This story builds the runtime engine and wires Leave, Attendance-regularization,
> Overtime, and Offer approvals through it. It unblocks the multi-level ACs deferred on US-LV-005 (AC-4),
> US-ATT-004 (AC-4), and US-REC-007 (FR-10), plus the SLA-escalation and delegation promised in US-ADM-007
> (AC-5/BR-4).

## 1. Description
**As a** Tenant Admin (whose configured workflows must actually govern requests), an **Approver** (who must
receive and act on the right step at the right time), and the platform itself,
**I want** a runtime engine that instantiates a workflow per submitted request, snapshots the definition
version, routes it through the configured sequential/parallel steps, evaluates conditions with the existing
`WorkflowEvaluator`, enforces SLA timers with idempotent auto-escalation, and applies delegation when an
approver is on leave,
**So that** approval requests are processed according to each tenant's authored workflow definitions instead
of the current single-step hard-coded path.

## 2. Preconditions
- US-ADM-007 workflow definitions exist and are authorable: `WorkflowDefinition` (with `LineageId`, `Version`,
  `Status` = Draft/Active/Archived, `EntityType`) and ordered `WorkflowStep` rows (with `ApproverType`,
  `ApproverIdentifier`, `IsParallel`, `SlaHours`, `EscalationApproverType/Identifier`, `ConditionJson`,
  `DelegationEnabled`, `DelegationBackupUserId`).
- An **Active** (`WorkflowStatus.Active`) definition exists for the entity type being submitted (BR-2: exactly
  one active definition per `(tenant, entityType)`).
- Approver-eligible users/roles exist in the tenant (US-ADM-005), so `LineManager`/`Role`/`NamedUser`/
  `DepartmentHead` approver types resolve to a concrete user.
- The notification delivery layer (US-NTF-006) exists so step-assignment / SLA / escalation / decision alerts
  can actually be sent.
- Background jobs (Hangfire on PostgreSQL) are available for the SLA-timer recurring job.

## 3. Acceptance Criteria (IEEE 830 §3.2 — Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A tenant has an **Active** `WorkflowDefinition` (v2) for `EntityType.Leave` | A leave request is submitted | A `WorkflowInstance` row is created bound to the **specific definition version row** (`WorkflowDefinitionId`) with `LineageId` and `Version` copied onto the instance; `WorkflowEvaluator.Evaluate(steps, requestData)` runs to produce the ordered applicable steps; a `WorkflowStepInstance` is created for the first applicable step (Pending, `SlaDueAt = now + step.SlaHours`); the request's `WorkflowInstanceId` is set (no longer null) and its status reflects "pending step 1". |
| AC-2 | An instance is at step N with a resolved approver | The assigned approver approves | The current `WorkflowStepInstance` is stamped `Approved`/`decided_by`/`decided_at`; the instance advances to the next applicable step (creating its step-instance + SLA due) or, if none remain, transitions `WorkflowInstance.Status → Approved` and the underlying request to its Approved terminal state; the next approver (or requester on completion) is notified via US-NTF-006. |
| AC-3 | A step carries a `ConditionJson` (e.g. `{"field":"days_requested","operator":">","value":5}`) | The engine evaluates it against the request's `requestData` | If `WorkflowEvaluator`/`WorkflowCondition.IsSatisfiedBy` returns false the step is **absent from the applicable list** and never instantiated (i.e. skipped); the skip is recorded on the instance history; a malformed condition is treated as not-applicable (defensive, matches `WorkflowEvaluator`). |
| AC-4 | A parallel group (all applicable steps sharing the same `StepOrder` with `IsParallel = true`) is active | Approvers act | The instance advances to the next `StepOrder` only when **all** step-instances in the parallel group are `Approved`; **any** rejection in the group short-circuits the instance to `Rejected` and rejects the underlying request. |
| AC-5 | A step-instance's `SlaDueAt` has elapsed while still `Pending` | The recurring SLA-timer job runs | The engine auto-escalates to the step's `EscalationApproverType/Identifier` (or notifies the Tenant Admin if none), stamps the step-instance `Escalated` exactly once, records the escalation on the instance history, and notifies via US-NTF-006 — matching US-ADM-007 BR-4. A step already escalated is not escalated again (idempotent). |
| AC-6 | A step has `DelegationEnabled = true` and its resolved primary approver has an **active approved leave** at the moment the step becomes active | The instance activates that step | The step-instance is assigned to `DelegationBackupUserId` instead, stamped `Delegated` in its assignment history, and the backup approver is notified (US-ADM-007 AC-5). If no backup is configured the step stays with the primary and the Tenant Admin is notified. |
| AC-7 | An in-flight instance exists under definition **v1** and an admin edits the definition (US-ADM-007 creates **v2**) | The edit is saved | In-flight instances continue evaluating against the `WorkflowDefinitionId` they snapshotted (v1); only requests submitted after the edit instantiate v2. |
| AC-8 | A definition lineage has ≥1 live (`InProgress`) instance | An admin attempts `DELETE` on that definition | `WorkflowService.DeleteAsync` computes a **real** `inFlightCount` from `WorkflowInstance` rows on that lineage (no longer hardcoded 0); the delete is blocked with 409 `workflow_in_flight`; the definition may still be archived (BR-6). |
| AC-9 | Two tenants each run workflow instances | Any query/action on instances runs | Instances, step-instances, and `inFlightCount` are tenant-isolated via the EF global query filter (RLS-eligible); Tenant A can neither see nor act on Tenant B's instances. |
| AC-10 | A user who is **not** the assigned approver (nor delegate) of the active step attempts to approve/reject it | They call the decision endpoint | The action is rejected (403) and no state transition occurs; only the resolved approver (or delegate) of a `Pending` step-instance may decide it. |
| AC-11 | A leave/regularization/overtime/offer request is submitted for an entity type that has **no Active definition** | The request is submitted | The engine falls back to the legacy single-level direct-manager path (no `WorkflowInstance` created, `WorkflowInstanceId` stays null) so the request is not orphaned; this fallback is logged. |
| AC-12 | Two approvers act on the same step-instance concurrently (or the SLA job fires while an approver is deciding) | Both transactions attempt to advance | Exactly one transition wins; the instance is never double-advanced or double-escalated. The advance runs inside the Postgres **retry-safe execution strategy** (`CreateExecutionStrategy().ExecuteAsync`, BUG-068 class) wrapping the manual transaction / row lock. |

## 4. Functional Requirements (IEEE 830 §3.2)
- FR-1: Introduce a `WorkflowInstance : BaseEntity` entity/table (tenant-scoped): `WorkflowDefinitionId`
  (FK to the specific version row), `LineageId` (Guid), `Version` (int), `EntityType` (`WorkflowEntityType`),
  `EntityId` (Guid — the leave/regularization/overtime/offer id), `CurrentStepOrder` (int), `Status`
  (new `WorkflowInstanceStatus`: `InProgress`/`Approved`/`Rejected`/`Escalated`/`Cancelled`), `CompletedAt`
  (DateTime?). Inherits `Id`, `TenantId`, audit fields from `BaseEntity`.
- FR-2: Introduce a `WorkflowStepInstance : BaseEntity` entity/table: `WorkflowInstanceId` (FK), `StepOrder`
  (int), `IsParallel` (bool), `ApproverType` (`WorkflowApproverType`), `AssignedApproverUserId` (Guid — the
  resolved concrete user), `Decision` (new enum: `Pending`/`Approved`/`Rejected`/`Skipped`/`Delegated`/
  `Escalated`), `DecidedByUserId` (Guid?), `DecidedAt` (DateTime?), `Comments` (string?), `SlaDueAt`
  (DateTime?), `EscalatedAt` (DateTime?). Skipped steps MAY be recorded as history rows or on an instance
  history log — implementer's choice (see §13 Q4).
- FR-3: On request submission, resolve the **Active** definition for the entity type; if found, snapshot its
  `WorkflowDefinitionId`/`LineageId`/`Version`, run `WorkflowEvaluator.Evaluate` over its steps with the
  request's data dictionary, create the instance + first-applicable step-instance, and set the request's
  `WorkflowInstanceId`. If no Active definition exists, use the legacy path (FR-12 / AC-11).
- FR-4: Advance/complete/reject the instance on approver decisions; on completion transition the underlying
  domain request (leave/regularization/overtime/offer) to its terminal Approved/Rejected state, replacing the
  current single-step routing.
- FR-5: Resolve `ApproverType` → concrete user at step activation: `LineManager` → requester's
  `ReportsToEmployeeId`; `DepartmentHead` → the requester's department head; `Role` → users holding
  `ApproverIdentifier` role (fan-out for parallel); `NamedUser` → `ApproverIdentifier` directly.
- FR-6: Evaluate step conditions at runtime by **reusing** `WorkflowEvaluator.Evaluate` (do not reimplement
  the condition language); non-applicable steps are skipped and recorded.
- FR-7: Enforce parallel groups — steps sharing a `StepOrder` with `IsParallel = true` form one group;
  advance only when all are `Approved`, fail on any `Rejected` (BR-3 / US-ADM-007).
- FR-8: A recurring Hangfire SLA-timer job scans `WorkflowStepInstance` rows where `Decision = Pending` and
  `SlaDueAt < now`, and performs a single auto-escalation per step (idempotent via `EscalatedAt`/`Decision`
  guard).
- FR-9: Apply delegation at step activation: when `DelegationEnabled` and the resolved primary approver has an
  active approved leave, assign `DelegationBackupUserId`; record the delegation.
- FR-10: Compute the real `inFlightCount` per definition lineage from live (`InProgress`) `WorkflowInstance`
  rows and replace the hardcoded `0` at `WorkflowService.cs:363`, so BR-6 (block delete while in-flight) is
  enforced.
- FR-11: Wire Leave approval (US-LV-005, `LeaveRequestService.cs` ~372/~1050/~1086), Attendance
  regularization (US-ATT-004, `AttendanceRegularization.WorkflowInstanceId`), Overtime
  (`OvertimeRecord.WorkflowInstanceId`), and Offer approval (US-REC-007) submission/approval paths through the
  engine.
- FR-12: Provide a read API for an instance's step chain + history so requesters/approvers see the current
  step and status on the request detail; expose real `inFlightCount` on the Workflow admin screen. Expose an
  approver decision endpoint (approve/reject/comment) authorized to the assigned approver/delegate only.
- FR-13: Emit a US-NTF-006 notification on each step assignment, escalation, delegation, and final decision.

## 5. Non-Functional Requirements (IEEE 830 §3.3)
- NFR-1: Instance creation and step-advance operations SHALL complete within 200ms (excluding async
  notification dispatch).
- NFR-2: All instance/step data SHALL be tenant-isolated via EF global query filters (and RLS once US-PLT-002
  enables it); both new entities are RLS-eligible tenant-scoped `BaseEntity` types.
- NFR-3: The SLA-timer job SHALL run at a bounded cadence (e.g. every 5 min) and be idempotent — a step is
  escalated at most once regardless of overlapping runs.
- NFR-4: State transitions on an instance SHALL be atomic and, on Postgres, run inside the retry-safe
  execution strategy (`_dbContext.Database.CreateExecutionStrategy().ExecuteAsync(...)` wrapping the manual
  transaction / `SELECT … FOR UPDATE`) — the BUG-068 manual-transaction class must not recur; verify on real
  Postgres, not InMemory.
- NFR-5: All instance actions (create/approve/reject/skip/escalate/delegate) SHALL be audited to the tenant
  `audit_log` via the existing `AuditInterceptor` pattern.

## 6. Business Rules
- BR-1: An instance is permanently bound to the `WorkflowDefinitionId` (version row) it was created under;
  later edits (new version rows on the same `LineageId`) do not retroactively change it.
- BR-2: A rejection at any step (or any approver of a parallel group) terminates the instance as `Rejected`
  and rejects the underlying request.
- BR-3: A skipped step (condition unmet) never blocks progress and requires no approver action.
- BR-4: SLA escalation fires at most once per step; with no `EscalationApproverType/Identifier` configured the
  Tenant Admin is notified instead.
- BR-5: Delegation applies only when the primary approver has an active approved leave at the moment the step
  becomes active (not retroactively for already-active steps).
- BR-6: A definition lineage with ≥1 `InProgress` instance may be archived but not deleted.
- BR-7: All runtime data is tenant-scoped; no cross-tenant instance visibility or action.
- BR-8: Only the resolved approver (or its delegate) of a `Pending` step-instance may decide that step; a
  requester cannot self-approve their own step even if resolution would name them (surface as a design
  question for the maker-checker parity with payroll — see §13 Q5).

## 7. Data Requirements
- **New tables:** `workflow_instance`, `workflow_step_instance` (both tenant-scoped `BaseEntity`,
  snake_case, RLS-eligible). New enums `WorkflowInstanceStatus` and `WorkflowStepDecision`.
- **Reads:** `WorkflowDefinition`/`WorkflowStep` (US-ADM-007) by the snapshotted `WorkflowDefinitionId`;
  approver users/roles (US-ADM-005); requester `ReportsToEmployeeId`/department head (Core HR); approver
  approved-leave status (Leave module) for delegation.
- **Writes:** `WorkflowInstanceId` back-reference on leave/regularization/overtime/offer records; the two new
  tables; `audit_log`.
- **Migrations:** created via `dotnet ef migrations add` only (never hand-written) — two new entities + FK
  columns already present (`WorkflowInstanceId` on `AttendanceRegularization`/`OvertimeRecord`; add to leave/
  offer entities if absent).

## 8. UI/UX Notes
- Requesters and approvers see the current step / approval chain and its status on the request detail
  (leave/regularization/overtime/offer), replacing the flat single-approver view.
- The Admin "Workflow" screen surfaces the **real** in-flight count per definition and (optionally) an
  instance list per definition for troubleshooting.
- Escalation/delegation/skip events appear in the request's history timeline.

## 9. Dependencies
- US-ADM-007 (design-time definitions/editor + `WorkflowEvaluator`) — hard dependency.
- US-ADM-005 (users/roles for approver resolution).
- US-NTF-006 (delivery layer) — for step/SLA/escalation notifications.
- US-LV-005, US-ATT-004, US-REC-007 — the request paths rewired through the engine.
- Leave module — approver approved-leave lookup for delegation.
- US-PLT-002 (RLS) — the new tables must be added to the RLS-eligible set when RLS lands.

## 10. Assumptions & Constraints
- Reuses the existing `WorkflowEvaluator`/`WorkflowCondition` logic; this story adds *state* (instances) and
  *routing*, not a new condition language (AND/OR groups remain Phase-2 per `WorkflowCondition`).
- Phase-1 scope matches US-ADM-007 §10: sequential + parallel steps, simple conditions, SLA timers, basic
  delegation. Sub-workflows/loops/external approvers remain deferred.
- SLA timing granularity is bounded by the job cadence (not real-time to the second).
- Requires the retry-safe Postgres execution strategy for transactional advances (project memory BUG-068
  class).
- **Payroll approval is out of scope for this engine** — see §12.

## 11. Test Hints
- **Instance creation:** submit a leave request against an Active definition; verify a `WorkflowInstance` is
  created, version-snapshotted (`WorkflowDefinitionId`/`Version`), `WorkflowInstanceId` set (not null).
- **Multi-level advance:** approve step 1 of a 3-step workflow; verify advance to step 2 + next-approver
  notification; approve the last step; verify request → Approved.
- **Condition skip:** submit a 3-day leave against a workflow whose step 2 triggers `days_requested > 5`;
  verify step 2 is skipped (never instantiated) and recorded.
- **Parallel:** two steps at the same `StepOrder`, `IsParallel = true`; verify advance only after both approve;
  verify one rejection fails the instance.
- **SLA escalation:** let a step's `SlaDueAt` elapse (advance the job clock / seed a past due); run the job
  twice; verify a single escalation + notification (idempotent).
- **Delegation:** set the resolved primary approver on approved leave at activation; verify routing to
  `DelegationBackupUserId` and a `Delegated` record.
- **Versioning:** start an instance on v1, edit the definition to v2; verify the in-flight instance still reads
  its snapshotted v1 row; a new request instantiates v2; `inFlightCount` accurate.
- **Delete guard:** with an `InProgress` instance, attempt definition delete; verify 409 `workflow_in_flight`
  (real count, not 0); archive succeeds.
- **Tenant isolation:** create instances in two tenants; verify no cross-tenant visibility/action;
  `inFlightCount` per tenant.
- **Concurrency / BUG-068:** exercise two concurrent approvals + an overlapping SLA job on real Postgres under
  the retrying execution strategy — assert no double-advance/double-escalate and no
  `BeginTransactionAsync`-throws.
- **No-definition fallback:** submit a request for an entity type with no Active definition; verify the legacy
  single-level path runs and `WorkflowInstanceId` stays null (AC-11).

---

## 12. Scope Decision — Payroll approval stays on its bespoke flow (RECOMMENDED)

Payroll **already ships a working, separate approval flow** (`PayrollApprovalService`, US-PAY-008): it
self-generates `run.CurrentWorkflowInstanceId` (a `BaseEntity.NewUuidV7()` grouping id, **not** an FK to any
table), writes immutable `PayrollApprovalHistory` rows, and enforces its own maker-checker state machine
(`ReviewPending → AwaitingApproval → Approved → Finalized`, self-approval block with a small-team exception).
It does **not** touch `WorkflowEvaluator`/`WorkflowDefinition`/`WorkflowStep`.

**Two options:**
- **(a) Subsume/migrate payroll onto the generic engine** — one workflow engine, one instance table, payroll
  authored as `EntityType.SalaryRevision`-style definitions.
- **(b) Leave payroll's bespoke flow as-is; scope the generic runtime to Leave / Regularization / Overtime /
  Offer only.** ← **RECOMMENDED for this pass.**

**Recommendation: (b).** *Confidence: 80%.*

**Why (b):**
- Payroll's flow is **built, tested, and working** (US-PAY-008); rewiring it onto a brand-new, unproven
  runtime is gratuitous risk on a compliance-sensitive path.
- Payroll's semantics differ materially from generic approvals — a **maker-checker self-approval guard with a
  small-team (< 2 eligible approvers) exception** and a `Finalized` terminal state that the generic engine
  does not model. Folding these into the generic engine would force engine complexity that only payroll needs
  (violating "simplicity first").
- The generic engine and payroll can converge **later** once the generic runtime has proven itself in
  production on the four lower-risk entity types.

**Tradeoff of (b):** two approval mechanisms coexist (two "instance id" concepts, two history tables —
`WorkflowStepInstance` vs `PayrollApprovalHistory`). That is duplicated surface and a future consolidation
debt. Accept it: the duplication is contained (payroll is one module), and forcing premature unification is
the larger risk. If (a) is ever chosen, it is a **separate migration story** ("US-PAY-XXX: migrate payroll
approval onto the generic workflow runtime"), gated on the generic engine being production-proven and on
modelling maker-checker + `Finalized` as first-class engine concepts.

## 13. Open Design Questions (implementer must resolve before build)

- **Q1 — Parallel fan-out/join schema.** The current `WorkflowStep` holds a **single** `ApproverIdentifier`,
  yet US-ADM-007's UI note promises "multiple approvers for a parallel step." Two candidate models: **(i)**
  multiple `WorkflowStep` rows sharing one `StepOrder` with `IsParallel = true` (the evaluator already orders
  by `StepOrder` and returns them all — the runtime groups by `StepOrder`); or **(ii)** a new
  `WorkflowStepApprover` child collection. **Recommendation: (i)** — no schema change to the design-time layer,
  and it falls out of the existing `Evaluate` output naturally. Confirm with the US-ADM-007 editor UI (does it
  emit sibling rows or a single row?). **This is a genuine gap — flagged OUT-OF-LANE below.**
- **Q2 — SLA clock source + idempotency key.** Is `SlaDueAt` computed from step *activation* time or instance
  *creation* time? (Recommend activation time.) What is the idempotency guard — a compare-and-swap on
  `Decision = Pending → Escalated` inside the retry-safe transaction, or a dedicated `EscalatedAt` null-check?
  (Recommend the atomic `Decision` transition so overlapping job runs can't double-fire.)
- **Q3 — Delegation trigger timing.** BR-5 says delegation is evaluated "at the moment the step becomes
  active." Confirm: is delegation re-evaluated if the primary approver goes on leave *after* the step is
  already active (recommend **no** — snapshot at activation, keep it simple), and what counts as "active
  approved leave" (a Leave request in Approved status spanning `now`)?
- **Q4 — Skip recording.** Are skipped steps materialized as `WorkflowStepInstance` rows with
  `Decision = Skipped`, or only logged to instance history? (Recommend materializing them so the chain UI and
  audit are complete.)
- **Q5 — Approver authorization + self-approval.** Is authorization purely "you are the assigned approver of a
  Pending step" (dynamic), or is a static permission also required? And does the generic engine adopt payroll's
  self-approval guard (a requester who resolves as their own approver)? (Recommend dynamic assigned-approver
  check for AC-10; defer maker-checker self-approval parity unless a story requires it — see BR-8.)
- **Q6 — `WorkflowEntityType` coverage for Overtime.** The enum is `Leave/Attendance/Expense/Offer/
  SalaryRevision` — there is **no `Overtime` member**, yet `OvertimeRecord` carries a `WorkflowInstanceId`.
  Decide whether overtime routes under `Attendance` or needs a new enum member (needs an EF migration and
  seeded default). **Flagged OUT-OF-LANE below.**

## 14. Phased Build Breakdown (each phase = one shippable `/implement-story` unit)

- **Phase 1 — Core runtime + Leave wiring (`US-ADM-011a`).** New `WorkflowInstance`/`WorkflowStepInstance`
  entities + enums + EF migration; instance creation on submit with version snapshot (AC-1, AC-7); sequential
  advance/complete/reject with `WorkflowEvaluator` condition-skip (AC-2, AC-3); approver decision endpoint +
  authorization (AC-10); wire **Leave** approval through it (FR-11 for leave); transactional advance under the
  retry-safe strategy (AC-12/NFR-4); tenant isolation (AC-9); audit (NFR-5); real `inFlightCount` + delete
  guard (AC-8). Covers AC-1/2/3/7/8/9/10/12. **Foundation — must land first.**
- **Phase 2 — Parallel steps + SLA escalation (`US-ADM-011b`).** Parallel-group join/short-circuit (AC-4,
  resolves Q1); idempotent recurring Hangfire SLA job + escalation (AC-5/FR-8, resolves Q2); US-NTF-006
  notifications on assignment/escalation/decision (FR-13). Depends on Phase 1.
- **Phase 3 — Delegation + remaining entity wiring (`US-ADM-011c`).** Delegation at activation via approver
  approved-leave lookup (AC-6/FR-9, resolves Q3); wire **Attendance regularization**, **Overtime** (resolve
  Q6), and **Offer** approvals through the engine (FR-11 remainder); no-definition legacy fallback (AC-11);
  request-detail step-chain read API + admin instance list (FR-12). Depends on Phase 1 (and Phase 2 for
  parallel offers, if any).

> Sequencing note: Phase 1 is the hard dependency for everything and should be its own PR. Phases 2 and 3 are
> independent of each other **except** that both build on Phase 1's tables/state machine, so run them
> sequentially (not in parallel) to avoid colliding migrations on the two new entities.
