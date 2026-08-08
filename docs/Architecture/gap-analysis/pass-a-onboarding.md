# Pass A8 — onboarding requirements audit

> **Run:** 2026-08-08 · **Tree:** `test/local-subdomains`
> **Depth:** 4 Must-Have stories at AC level (21 ACs) + 2 Should-Have at story level = **23 rows**
> **Status:** ✅ VALIDATED — 3 of 3 orchestrator spot-checks confirmed.
> **Headline:** the backend is among the strongest audited. **10 of 11 failures are leg 2, all at the Angular↔API boundary** — and two FE services carry a written admission that the contract was guessed.

## Orchestrator validation

| Claim | Result |
|---|---|
| ONB-002 AC-1 dead route | ✅ **Confirmed.** FE calls `${this.base}/applicable` (`onboarding-checklist.service.ts:52`); the route is `[HttpGet("applicable-templates")]` (`OnboardingChecklistsController.cs:35`). The `{id:guid}` route cannot absorb a non-GUID segment → **404**. |
| **The FE admits the contract was never verified** | ✅ **Confirmed — and this is the smoking gun for the whole codebase-wide pattern.** `onboarding-checklist.service.ts:33` and `onboarding-template.service.ts:22` both carry, verbatim: `CONTRACT (assumed — reconcile with backend; mapping kept in ONE place here):`. **The team knew it was guessing, wrote it down, and shipped.** |
| ONB-005 AC-3 five-field mismatch | ✅ **Confirmed.** BE `OffboardingDtos.cs` exposes `Id`, `ClearanceCategoryName`, `ClearanceStatus`, `FullyCleared`. The dashboard binds `inst.overallClearance` (`:116-117`), `dept.department` (`:122,127`), `dept.clearanceStatus` (`:127`). **`overallClearance` and `department` do not exist on the DTO.** |

**Auditor pushback on the brief — accepted, and I was wrong.** My brief singled out the asset register as the risk area ("audit the asset ACs carefully"). **US-ONB-004 is the healthiest story in the module** — the one FE contract that is genuinely pinned rather than assumed: paths, verbs, multipart field names and every DTO field match. The real damage is in **US-ONB-002 and US-ONB-005**, which my brief did not mention.

---

## VERDICT TABLE

| Req ID | Requirement (short) | MoSCoW | Verdict | Evidence (file:line) | Note |
|---|---|---|---|---|---|
| ONB-001 AC-1 | Create-template form: name, desc, departments, job titles, task builder | Must | PARTIAL | `template-builder.component.ts:607`; `onboarding-template.service.ts:63`; `OnboardingTemplatesController.cs:20-164` | **leg2.** Builder routed but calls `GET /onboarding/templates/lookups`, which **no controller serves** → dept/title/user pickers can never populate |
| ONB-001 AC-2 | Persist template + tasks; `tenant_id` from session | Must | IMPLEMENTED | `OnboardingTemplateService.cs:61-82,240-253`; TenantId `:64,243` | All 8 task fields match FE↔BE field-for-field; 14 xUnit facts |
| ONB-001 AC-3 | Duplicate name → exact message | Must | IMPLEMENTED | `OnboardingTemplateService.cs:53-55` (verbatim, 409); `:255-263` case-insensitive, tenant-scoped | |
| ONB-001 AC-4 | Mandatory tasks flagged; cannot be skipped once assigned | Must | IMPLEMENTED | `:251`; `OnboardingChecklistService.cs:447`; removal blocked `:339-341` | BR-3 enforced on modify |
| ONB-001 AC-5 | Tenant B cannot see Tenant A templates | Must | IMPLEMENTED | `AppDbContext.cs:693,697` | Filters on template + template-task |
| **ONB-002 AC-1** | List active templates filtered by dept/job-title + universal | Must | 🔴 **CONTRADICTED** | BE correct `OnboardingChecklistService.cs:79-117`; FE calls `/checklists/applicable`, route is `applicable-templates` | **404 → dropdown always empty.** Spec mocks the wrong path (`…spec.ts:97`) |
| ONB-002 AC-2 | Task instances w/ due date, status, responsible + notifications | Must | PARTIAL | BE correct `:121-276`, due `:445`, outbox `:479-525`; FE sends `tasks` vs BE `AdditionalTasks` (`OnboardingChecklistDtos.cs:87`) | **leg2.** Inline-edited task set silently dropped; FE reads `notifiedCount`/`checklistInstanceId`, BE emits `notificationsQueued`/`id` |
| **ONB-002 AC-3** | Existing checklist → replace/merge prompt | Must | 🔴 **CONTRADICTED** | BE merge/replace real `:159-233`; FE `GET /checklists/employee/{id}` (`onboarding-checklist.service.ts:78`) — **no such route** | **The prompt's trigger never resolves** |
| ONB-002 AC-4 | Add/remove tasks post-assignment; soft-delete; audit | Must | PARTIAL | BE `:307-368`; FE uses `PATCH` (`:105`) vs BE `[HttpPut]` (`OnboardingChecklistsController.cs:112`) | **leg2.** 405; body `{tasks}` vs `{addTasks, taskChanges}` |
| ONB-002 AC-5 | Notify Manager + IT via SignalR + email | Must | IMPLEMENTED | `:487-491`; `OnboardingNotificationDispatchJob.cs:64-68`; registered `Program.cs:353` | API-level complete; unreachable via UI only because AC-1/AC-3 gate the flow |
| ONB-003 AC-1 | Dashboard "Onboarding Progress" widget | Must | PARTIAL | Widget exists `onboarding-progress-widget.component.ts:1-160`; BE `/me/progress` correct; **contract matches** | **leg2 — the widget is orphaned.** Only exported at `features/onboarding/index.ts:12`, never imported by the dashboard or any route |
| ONB-003 AC-2 | Checklist grouped by category, status, overdue red | Must | IMPLEMENTED | `:538-572` (grouping `:560-568`, overdue `:558`); routed `app.routes.ts:606` | Drift: `:947` emits `"inprogress"` vs FE `'in_progress'` — chip label only |
| ONB-003 AC-3 | Mark complete → timestamp + actor, progress updates, HR notified | Must | IMPLEMENTED | `:640-655,669-673`; outbox `:867-917` | 15 xUnit facts |
| ONB-003 AC-4 | Doc upload to `{tenantId}/onboarding/{employeeId}/{taskId}/{filename}` | Must | IMPLEMENTED | `:859` (exact path), MIME `:835`, 10 MB `:841`, scan `:845` | **Path confirmed live on disk** under `HRM.Api/uploads/` |
| ONB-003 AC-5 | Overdue highlighted; Hangfire already notified HR + manager | Must | IMPLEMENTED | `:683-797`; recurring `Program.cs:894` | BR-4 deviation: `:681` states tenant-timezone 09:00 scheduling is **not** built; sweep runs UTC. AC-5 itself is met |
| ONB-005 AC-1 | Exit checklist from **tenant's** offboarding template | Must | PARTIAL | `OffboardingService.cs:62-186`, due `:170`; gate `:106`; `CreateOnboardingTemplateRequest` has **no `Kind`** | **leg1.** `ChecklistKind.Offboarding` is referenced in exactly one place — the rejection. **No API or seed can create one**, so only the hardcoded `DefaultClearanceTaskSpecs()` is reachable |
| ONB-005 AC-2 | IT marks asset returned → register updated, task complete, audit | Must | PARTIAL | BE `OffboardingController.cs:105-122`; FE path/verb/body all match | **leg2.** FE derives the URL from `t.taskId` but BE emits `Id` → posts to `/tasks/undefined/return-asset` |
| **ONB-005 AC-3** | Per-department clearance; "fully cleared" only when all approved | Must | 🔴 **CONTRADICTED** | BE correct `OffboardingDtos.cs:33-56`; FE binds `overallClearance`, `dept.department`, `dept.clearanceStatus`, `task.taskId`, `task.clearanceStatus==='cleared'` | **Not one of those five names exists on the BE DTO.** Spec mocks the FE's own invention |
| ONB-005 AC-4 | Complete → terminated, account deactivated, F&F trigger | Must | PARTIAL | BE strong `:371-372,383,396-402`; **real** `RealPayrollFnFIntegration` registered (`DependencyInjection.cs:384`) | **leg2.** `canComplete()` requires `clearanceStatus === 'cleared'`, which BE never emits → **"Complete Offboarding" is permanently disabled** |
| ONB-005 AC-5 | Block completion + list pending mandatory items | Must | PARTIAL | BE `:338-362`; 409 body `OffboardingController.cs:141-144`; FE reads `err.error.pending` as `string[]` | **leg2.** Real body is `{data:{pendingItems:[{taskId,title,…}]}}` → parser returns `null`, list never shown |
| ONB-005 AC-6 | Tenant B cannot see Tenant A offboarding data | Must | IMPLEMENTED | `AppDbContext.cs:717,721`; TenantId from session `:127,152` | |
| **US-ONB-004** | Asset issuance tracking | Should | **IMPLEMENTED** | `AssetService.cs:219,249-251,286`; `OnboardingAssetsController.cs:21-162`; filter `AppDbContext.cs:709`; FE `onboarding-asset.service.ts:26-31` | **The only genuinely pinned FE contract in the module** — paths, verbs, multipart field names and every DTO field match. Drift: BE `"Available"` vs FE `'available'`, display-only |
| **US-ONB-006** | Exit interview recording | Should | IMPLEMENTED | `ExitInterviewService.cs`; `ExitInterviewsController.cs:23,37,52,74,109-110`; routes `app.routes.ts:647,666` | Drift `exitInterviewId`/`offboardingId` vs `Id`/`OffboardingInstanceId` is **cosmetic** — the component reads the route param, not the response |

---

## CONTRADICTIONS

`docs/BA/STATUS.md:239-244` marks all six stories `[x]` (PRs #95–#100).

**C-1 — ONB-002 AC-1: the applicable-template dropdown 404s.** FE requests `/checklists/applicable`; the route is `applicable-templates`. **The service file admits it** at `:33`: *"CONTRACT (assumed — reconcile with backend…)"*. It was never reconciled. The spec keeps it green by mocking the invented path.

**C-2 — ONB-002 AC-3: the replace/merge prompt cannot fire.** BE implements both modes properly; the FE detects the pre-existing checklist via `GET /checklists/employee/{employeeId}` — **no controller declares that route.**

**C-3 — ONB-005 AC-3: the clearance dashboard binds five fields the API does not return.** `offboarding.models.ts:4` calls this a *"PINNED backend contract … field names are fixed"*. **It is not pinned.** Blast radius beyond rendering: `pendingMandatoryTitles()` filters `clearanceStatus !== 'cleared'` — always true — so `canComplete()` is permanently `false` and **the "Complete Offboarding" button can never be enabled**, disabling AC-4's entire flow through the UI even though the backend implements it correctly.

**Reverse drift:** `TEST-STATUS.md:208` flags BUG-088 against ONB-002 — **it is fixed** (`:218-221` carries the fix comment; `IdempotencyKey` is its own column with migration `20260717003336`, a filtered UNIQUE index, and a concurrency race handler at `:240-262`). The ledger does not record the resolution. `ISSUE-200` (audit) is confirmed **still open** but is an NFR, not an AC — no verdict moves.

---

## GAPS RANKED

1. **ONB-005 clearance-dashboard contract (5 field mismatches) — CRITICAL, M.** Blast radius is the whole offboarding UI: dashboard blank, asset-return posts to `/tasks/undefined/…`, Complete permanently disabled. *Fix in the FE models only* — `offboardingId→id`, `taskId→id`, `department→clearanceCategoryName`, `dept.clearanceStatus→status`, and map `approved|pending_issues|null → cleared|issues|pending`. **Do not change the API** — other consumers and 21 `OffboardingServiceTests` facts depend on it.
2. **ONB-002 assign/modify contract (2 dead routes, 1 wrong verb, 2 body mismatches) — CRITICAL, M.** Assignment is unusable from the UI. Fix the FE names/verbs, then **add** the two genuinely missing endpoints (`/checklists/preview`, `/checklists/employee/{id}`) or drop the features needing them — **AC-3 cannot be met without the latter.**
3. **ONB-001 `applicableDepartmentIds`/`applicableJobTitleIds` mismatch — HIGH, S.** Every template created through the UI **silently becomes universal**, which makes ONB-002 AC-1's filtering meaningless even after gap #2 is closed.
4. **ONB-005 AC-1 offboarding-kind templates are uncreatable — HIGH, S.** FR-1 "configurable per tenant" is dead. The `kind` column already exists (`20260617070629_AddOffboarding.cs:15`); add `Kind` to the create request.
5. **ONB-001 `/templates/lookups` missing + activate/deactivate verb — HIGH, S.** Pickers never populate; Activate/Deactivate 405s.
6. **ONB-003 AC-1 orphaned progress widget — MEDIUM, S.** Component and API both correct; nothing renders it. One import.
7. **ONB-005 AC-5 pending-list parser — MEDIUM, S.**
8. **ONB-003 BR-4 tenant-timezone overdue scheduling — LOW, M.** Documented deliberate deferral, not a defect.
9. **Status-string casing drift — LOW, S.** Label-only today; **will bite the first component that branches on the value.**

---

## COVERAGE SUMMARY

```
Requirements audited: 23 | IMPLEMENTED: 12 | PARTIAL: 8 | MISSING: 0 | CONTRADICTED: 3
```

**The cleanest leg split of any module audited:**
- **Leg 1:** 1 failure in 23. **The backend does what the specs say.**
- **Leg 2:** **10 of 11 failures** — 4 wrong paths, 2 wrong verbs, 3 body-field mismatches, ~12 response-field mismatches, 1 orphaned component.
- **Leg 3:** **0 failures.** ~200 xUnit facts across 24 files plus 101 IEEE-829 TCs. **But the FE Karma specs are test theater** — mocking `/lookups`, `/preview`, `/employee/{id}`, `offboardingId`, `overallClearance`, `department`: endpoints and fields that do not exist.

**By layer: backend ~95% clean; frontend ~55% dead on arrival.**
**By story:** ONB-003/-004/-006 essentially shippable; ONB-001 80% there; **ONB-002 and ONB-005 are non-functional through the UI despite complete, tested backends.**

---

## CONFIDENCE

**Overall: 90%.** Static reading only; no stack run.

| Verdict | Conf. | Settled by |
|---|---|---|
| C-1/C-2 dead routes | **97%** | One curl. Verified no other controller declares those paths and that `{id:guid}` cannot absorb a non-GUID segment *(orchestrator re-confirmed)* |
| C-3 dashboard mismatch | **96%** | Load the page and diff network vs DOM *(orchestrator re-confirmed)* |
| ONB-001 lookups dead | **92%** | Grepped `Program.cs` for a catch-all/minimal-API mapping; none found |
| ONB-005 AC-1 uncreatable kind | **90%** | Residual: a DB seed outside `src/` could set `kind` |
| ONB-002 AC-2 `tasks` dropped | **93%** | Confirm System.Text.Json ignores the unknown member silently |
| ONB-003 orphaned widget | **95%** | Grepped class name **and** selector repo-wide; only the barrel export matched |
| ONB-004 / ONB-006 | **85%** | Story-level per the depth rule — all 5 ACs spot-checked, not every FR traced |
| ONB-002 AC-5 notifications | **80%** | Did not open `INotificationDispatcher`'s SignalR implementation |

**Limits:** no runtime verification — every leg-2 finding is a static route/field comparison, **though the FE's own "CONTRACT (assumed)" comments corroborate them**. NFR-1 response-time budgets excluded as unverifiable by inspection. RLS-as-defence-in-depth asserted by the stories but only EF filters were verified.

---

## OUT-OF-LANE

- **type:** doc-drift · **severity:** HIGH · **where:** `EmployeeStatusService.cs:467` · **what:** `TODO(onboarding): Trigger offboarding workflow when Onboarding module is built` — the module shipped in PRs #95–#100, so **US-CHR-009 AC-3 is unmet and its blocking dependency is long gone.** · **ownership answer for the brief:** the trigger belongs on the **CHR side**; `OffboardingService.cs:371-372` runs the *opposite* direction (completion sets the employee terminal). **There is no auto-trigger in either direction.** US-ONB-005 AC-1 specifies a *manual* HR "Initiate Offboarding" action, so **ONB-005 is not defective here — CHR-009 AC-3 is.** · **suggested-action:** file against US-CHR-009 AC-3; implement the call to `IOffboardingService.InitiateAsync` on transition to Terminated, or record DECIDED-NOT-BUILT and delete the TODO.
- **type:** test-integrity · **severity:** HIGH · **where:** `onboarding-template.service.spec.ts:135`, `onboarding-checklist.service.spec.ts:114,134`, `offboarding-dashboard.component.spec.ts:48-58` · **what:** specs assert against endpoints and fields that do not exist, staying green over a feature that cannot work in production. · **suggested-action:** route to `@test-authenticator`; consider a CI contract test (Verify snapshot of the Swagger doc diffed against FE service base paths) so **invented routes fail the build**.
- **type:** risk · **severity:** MED · **where:** `HRM.Api/uploads/019ef3ba-…/onboarding/…/valid.pdf` · **what:** test-run upload artefacts committed under the API project tree. *(Positively, real evidence that the AC-4 tenant-isolated path works.)* · **suggested-action:** gitignore `HRM.Api/uploads/` and purge, unless deliberate fixtures.
- **type:** bug · **severity:** LOW · **where:** `OnboardingChecklistService.cs:947` · **what:** `Status.ToString().ToLowerInvariant()` yields `"inprogress"` while every FE union and the DTO doc-comment use `in_progress`. · **suggested-action:** emit snake_case explicitly; pin the wire value in a test.
