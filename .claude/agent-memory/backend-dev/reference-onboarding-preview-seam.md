---
name: reference-onboarding-preview-seam
description: Onboarding checklist preview (ISSUE-374) reuses assign's PURE factories; and BUG-441's replace-mode assign contract (resolvedTasks) that closed the task-duplication defect
metadata:
  type: reference
---

# GET /onboarding/checklists/preview — the reusable seam in OnboardingChecklistService

`NewTaskFromTemplate` / `NewAdHocTask` / `ResolveResponsiblePartiesAsync` / `ResolveTaskUser` in
`HRM.Infrastructure/Services/OnboardingChecklistService.cs` are **pure** — they build entity objects
and resolve owners without registering anything on the DbContext. Only `AddTaskInstance` and
`instance.Tasks.Add` attach. That is what let `PreviewAsync` reuse assign's resolution wholesale
(passing `Guid.Empty` as the instance id) instead of duplicating BR-4/FR-2/FR-3, with zero writes.
The BR-4 anchor was extracted to `ResolveStartDate(employee, overrideStartDate)` so the two paths
cannot drift.

**Preview must never touch assign's idempotency path.** Assign catches a 23505 unique violation on
`IdempotencyKey` and returns the race winner; a preview that inserted would both trip BR-2's
one-active-checklist rule and pollute that path. Same discipline as `AttendancePolicyResolver`
(never lazily creates a policy row).

**Proving "persists nothing" needs a non-zero baseline.** `Preview_PersistsNothing` assigns to a
*different* employee first, so the row counters are demonstrably wired to real data — a bare
`0 == 0` would be green against a blind counter. Mutation-verified: a *successful* persist inside
`PreviewAsync` makes the count assertion fire (a persist that throws only produces a 500, which
proves much less).

## Wire-shape gotchas for onboarding preview
- FE `ChecklistTaskStatus` is lowercase snake (`'pending'`), NOT the PascalCase enum name — the
  preview DTO emits `Status` as a plain string.
- FE `IChecklistTask.id` is absent in a fresh preview; do not add an Id property to the preview DTO.
- Swashbuckle names the schemas `OnboardingChecklistPreviewDto` / `...PreviewTaskDto` (module prefix),
  not the C# type name; the replace-mode body is `OnboardingResolvedTaskRequest`.

## BUG-441 — assign has TWO task contracts now; know which one you are in

`AssignChecklistRequest` carries both:
- `additionalTasks` (**legacy**, unchanged): extras appended ON TOP of the expanded template; each
  entry is an *offset* (`dueOffsetDays`) from the anchored start date.
- `resolvedTasks` (**replace mode**, added by BUG-441): the AUTHORITATIVE set. When it is non-null the
  template is **not** expanded and these rows are created verbatim, each with a **concrete `dueDate`**.
  `null` vs `[]` is meaningful: null = legacy expansion, `[]` = a checklist with no tasks.
  Supplying both is a **400** (not a silent winner) — quietly dropping one set is the same invisible
  data loss the bug was.

Replace-mode rows are **server-authoritative for ownership and mandatoriness**: there is deliberately
no `responsibleUserId` on the wire (FR-3 re-resolves from the role, so an edited role gets the right
owner and nobody can name an arbitrary user id), and `isMandatory`/`requiresDocument` come from the
source template task. An unedited row therefore reproduces the preview exactly — that property is
pinned by `Assign_EchoingThePreviewUnedited_ProducesExactlyThePreviewedTasks`.

Vetting lives in `PairResolvedTasks` before anything is built: unknown `templateTaskId` → 400 (never
silently demoted to ad-hoc), the same template task twice → 400, a missing mandatory template task →
400 (BR-3 — otherwise replace mode is the one write path where a mandatory task can be dropped).

**Testing note:** the count half of "created exactly once" cannot fail against pre-fix code — old code
has no replace mode, so it *ignores* the unknown field and creates the plain template set. The honest
before/after is (a) pre-fix simulation = map `ResolvedTasks` to `null` in the controller (5/7 arms red,
the legacy arm stays green), and (b) a mutation that re-adds the template expansion inside the replace
branch (3/7 red). Do both; the first alone under-proves the duplication assertion.
