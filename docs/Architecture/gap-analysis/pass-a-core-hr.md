# Pass A4 — core-hr requirements audit

> **Run:** 2026-08-08 · **Tree:** `test/local-subdomains`
> **Depth:** 7 Must-Have stories at AC level (37 ACs) + 5 Should + 1 Could at story level = **43 rows**
> **Status:** ✅ VALIDATED — 4 of 4 orchestrator spot-checks confirmed.
> **Headline:** **18 of 19 PARTIALs fail at the frontend.** Not one requirement failed because backend logic was absent. A systemic `{entity}Id` vs `id` naming drift kills the deactivate path on three entities at once.

## Orchestrator validation

| Claim | Result |
|---|---|
| `{entity}Id` vs `id` drift across three modules | ✅ **Confirmed.** `department.models.ts:13` `departmentId`, `job-title.models.ts:18` `jobTitleId`, `location.models.ts:14` `locationId` — all against `public Guid Id` in `DepartmentDto.cs:8`, `JobTitleDto.cs:10`. |
| PATCH vs POST verb mismatch on deactivate | ✅ **Confirmed.** `department.service.ts:22` documents `PATCH …/deactivate`; `DepartmentsController.cs:136` is `[HttpPost("{id:guid}/deactivate")]`. |
| **US-CHR-013 has zero FE surface** | ✅ **Confirmed.** `grep -riE 'workArrangement\|\bfte\b'` across all of `src/frontend/src/app` returns **no files**. `Employee.cs:130,135` has both properties. `STATUS.md:61` explicitly claims a shipped **"FE employee-form"**. It does not exist. |
| Directory calls the wrong endpoint | ✅ **Confirmed.** `employee.service.ts:47` `baseUrl = …/tenant/employees`, and the directory-search method at `:80` uses `this.baseUrl` — not `/directory`. The implementation lives at `EmployeesController.cs:174` `[HttpGet("directory")]`. |

**Auditor correction to the orchestrator's brief:** I asked it to check a `TC-LV-031` claim that `Employee` has no `Fte` field. **No such claim exists** — that TC contains no `Fte` reference at all. The real FTE drift runs the *opposite* direction (STATUS.md over-claiming an FE that was never built). My brief carried that error forward from the pilot; corrected here.

---

## VERDICT TABLE

| Req ID | Requirement (short) | MoSCoW | Verdict | Evidence (file:line) | Note |
|---|---|---|---|---|---|
| CHR-001 AC-1 | Multi-step add-employee wizard | Must | IMPLEMENTED | `employee-wizard.component.ts`; routed `app.routes.ts:582` | 7 sections present |
| CHR-001 AC-2 | Create; `employee_no` per tenant; tenant from session | Must | IMPLEMENTED | `EmployeeService.cs:131-134,1157-1178` | `TenantId` never from body |
| CHR-001 AC-3 | Tenant-scoped email uniqueness | Must | IMPLEMENTED | `EmployeeService.cs:79-83`; `EmployeeConfiguration.cs:149-151` | Message verbatim |
| CHR-001 AC-4 | Photo → tenant path, EXIF stripped, signed URL | Must | **PARTIAL (leg2)** | BE real: `EmployeesController.cs:648`, `ImageMetadataStripper`, `EncryptingFileStorage` | **No FE code calls `POST /{id}/profile-photo`** — the wizard posts the photo as multipart to the JSON-only create action |
| CHR-001 AC-5 | Plan employee-limit block | Must | IMPLEMENTED | `EmployeeService.cs:1111-1150` | Message exact |
| CHR-001 AC-6 | `custom_fields` JSONB | Must | IMPLEMENTED | migration `20260612105901_…:79` (jsonb + GIN) | |
| CHR-002 AC-1 | Full profile in card sections | Must | IMPLEMENTED | `EmployeeProfileDto.cs:8-86` | Education/WorkHistory/Dependents are real entities |
| CHR-002 AC-2 | Edit + save w/ `xmin` + audit | Must | **PARTIAL (leg2)** | BE emits `rowVersion` (`EmployeeProfileDto.cs:61`); FE reads `p.xmin` (`employee-profile.component.ts:2560`) | `Number(undefined)` → NaN; **no mapping exists anywhere in the FE** |
| CHR-002 AC-3 | Stale-token 409 conflict | Must | **PARTIAL (leg2)** | BE real `EmployeeService.cs:450`; same break | Concurrency unusable end-to-end |
| CHR-002 AC-4 | Employee self-serve editable subset | Must | IMPLEMENTED | `EmployeeService.cs:405-425` | + horizontal-privilege check |
| CHR-002 AC-5 | API rejects restricted-field writes | Must | IMPLEMENTED | `EmployeeService.cs:405-425` | Explicit 403 per section |
| CHR-002 AC-6 | Dept/title change → history timeline | Must | IMPLEMENTED | `EmployeeService.cs:591-710` | |
| CHR-003 AC-1 | Paginated list, name-asc default | Must | **PARTIAL (leg2)** | FE calls `baseUrl` (`employee.service.ts:47,80`), not `/directory` (`EmployeesController.cs:174`) | Wrong endpoint; fallback sorts by `EmployeeNo` |
| CHR-003 AC-2 | Search incl. **phone**, debounced | Must | **PARTIAL (leg2)** | `/directory` includes phone (`EmployeeDirectoryService.cs:164-170`); the endpoint actually called omits it | Debounce real |
| CHR-003 AC-3 | Filters + chips + URL state | Must | **PARTIAL (leg2)** | URL sync real (`employee-list.component.ts:1424-1518`); `GetEmployeesQuery.cs:10-16` binds **no filter params** | Filters are silent server-side no-ops |
| CHR-003 AC-4 | Pagination, total, tenant-scoped | Must | IMPLEMENTED | `EmployeesController.cs:37-38` | Correct on the endpoint in use |
| CHR-003 AC-5 | Export CSV **and** xlsx | Must | **PARTIAL (leg2)** | BE real incl. ClosedXML (`EmployeeDirectoryService.cs:345-380`); FE calls `/export`, route is `directory/export` (`:230`) | **Export button 404s** |
| CHR-004 AC-1 | Dept form incl. manager picker | Must | **PARTIAL (leg1+2)** | Placeholder *"available once employee management is implemented"* `department-form.component.ts:156-176`; `DepartmentDto` emits no `ManagerName` | **Stale — Employees shipped long ago** |
| CHR-004 AC-2 | Create, tenant-scoped, unique | Must | IMPLEMENTED | `DepartmentService.cs:88` | |
| CHR-004 AC-3 | Duplicate-name rejection | Must | IMPLEMENTED | `DepartmentService.cs:56` | Message exact, case-insensitive |
| CHR-004 AC-4 | Parent change + cycle prevention | Must | IMPLEMENTED | `DepartmentService.cs:323-352,157-160`; tests `DepartmentServiceTests.cs:359-390` | |
| CHR-004 AC-5 | Block deactivate w/ active employees | Must | **PARTIAL (leg2)** | BE real `DepartmentService.cs:218-225`; FE `http.patch` vs `[HttpPost]` → 405; id is `undefined` | **Doubly dead** |
| CHR-005 AC-1 | Job-title list incl. employee count | Must | **PARTIAL (leg2)** | BE computes it (`JobTitleService.cs:194-231`); FE **hardcodes "—"** (`job-title-list.component.ts:212-217`) | Data exists, never rendered |
| CHR-005 AC-2 | Create, tenant-scoped unique | Must | IMPLEMENTED | `JobTitleService.cs:44-52` | |
| CHR-005 AC-3 | Duplicate-title rejection | Must | IMPLEMENTED | `JobTitleService.cs:53` | Message exact |
| CHR-005 AC-4 | `grade_id` FK-validated + shown on profile | Must | **PARTIAL (leg1)** | Validation real `JobTitleService.cs:242-253`; **no grade field in `EmployeeProfileDto`** | First clause met, second absent |
| CHR-005 AC-5 | Block deactivate w/ assignees | Must | **PARTIAL (leg2)** | BE real `JobTitleService.cs:145-152`; FE PATCH vs POST + `jobTitleId` undefined | |
| CHR-009 AC-1 | Valid-transition list from state machine | Must | **PARTIAL (leg2)** | Machine exact vs FR-2 (`EmployeeStatusStateMachine.cs:16-23`); FE types `IStatusTransition[]`, BE returns an object | `@for` over a non-iterable |
| CHR-009 AC-2 | Status change + history + audit | Must | **PARTIAL (leg2)** | BE real `EmployeeStatusService.cs:155-240`; FE reads `response.profile`, BE emits no such field | TypeError swallows the success path |
| CHR-009 AC-3 | Termination side effects | Must | **PARTIAL (leg1, narrow)** | Login disable `:528-562`; headcount `:168`; **payroll exclusion real** `PayrollRunProcessor.cs:236-237`; **accrual paused via `IsActive`** `LeaveEntitlementService.cs:727` | Only the offboarding trigger is absent. **The `// TODO(payroll)`/`(leave)` comments are stale** |
| CHR-009 AC-4 | Daily probation reminder, no auto-transition | Must | IMPLEMENTED | `Jobs/ProbationReminderJob.cs`; registered `Program.cs:706-710` | Not orphaned |
| CHR-009 AC-5 | Invalid-transition rejection | Must | IMPLEMENTED | `EmployeeStatusStateMachine.cs:46-49` | Capitalization drift only |
| CHR-011 AC-1 | Reporting-manager field on profile | Must | IMPLEMENTED | `EmployeeProfileDto.cs:34-36` | |
| CHR-011 AC-2 | Assign manager + history + audit | Must | IMPLEMENTED | `ReportingStructureService.cs:128,454-467,134-144` | Dual-audited |
| CHR-011 AC-3 | Circular chain detection, any depth | Must | IMPLEMENTED | `ReportingStructureService.cs:412-437` | Message verbatim; depth 500 |
| CHR-011 AC-4 | "My Team" direct reports | Must | **PARTIAL (leg2)** | BE returns `DirectReportsResult` object (`EmployeesController.cs:408`); FE types a bare array; `track report.employeeId` vs BE `id` | Also gated `Employee.View.All` (`:395`), **which Managers do not hold** |
| CHR-011 AC-5 | Bulk manager assignment | Must | **PARTIAL (leg2)** | BE + FE modal + URL correct; FE reads `totalSuccess`/`totalFailed`, BE emits `successCount`/`failureCount` (`ReportingStructureDtos.cs:58-61`) | Success toast never fires |
| US-CHR-006 | Org tree / hierarchy visualization | Should | IMPLEMENTED | `OrgTreeNodeDto.cs:12-59` ↔ `org-tree.models.ts:26-38` **aligned**; routed | All 5 ACs spot-checked; contract clean |
| US-CHR-007 | Office locations | Should | **PARTIAL (leg2)** | BE solid `LocationService.cs:172-178`; FE `locationId` vs BE `Id` | Verb correct here; id is not |
| US-CHR-008 | Employee document management | Should | IMPLEMENTED | **Defensive mapping** `document.service.ts:47-49`; real `IFileStorage` | NFR-3 malware scan is a **documented deploy-gate**, stub by default |
| US-CHR-010 | Bulk CSV/Excel import | Should | **PARTIAL (leg2)** | Async Hangfire real; FE reads `err.row`, BE emits `rowNumber` (`BulkImportDtos.cs:37`) | **Error table + error CSV show `undefined`** — defeating bulk import's entire value |
| US-CHR-012 | Custom fields per tenant | Could | IMPLEMENTED | **Normalization boundary** `custom-field.service.ts:56` | DF-9 fixed the shape; contract clean |
| **US-CHR-013** | Employee FTE & work arrangement | Should | 🔴 **CONTRADICTED** | BE complete: `Employee.cs:130,135`; migration `20260715163612_…:14,21`; proration `LeaveEntitlementService.cs:453`; geofence `AttendanceService.cs:151` — **zero FE surface** | See below |

---

## CONTRADICTIONS

### 🔴 US-CHR-013 — `STATUS.md` claims a frontend that does not exist

`docs/BA/STATUS.md:61`, verbatim:
> `[x] US-CHR-013 — Employee FTE & work arrangement (**SHIPPED — CAL-6, PR #316**: Employee.Fte + proration … · Employee.WorkArrangement + Remote geofence exemption + **FE employee-form**.)`

The backend half is genuinely excellent — `Employee.Fte`/`WorkArrangement`, a real migration, FTE proration wired into leave entitlement, and `WorkArrangement == Remote` exempting geofence. But a search of the entire Angular app across four naming variants (`workArrangement`, `arrangement`, `Hybrid`/`OnSite`, `fte`/`FTE`) returns **zero hits** — orchestrator-verified.

**HR cannot set FTE or work arrangement through the product.** Both are backend-only, settable via bulk import or direct SQL. The `[x]` and the explicit "FE employee-form" claim are false.

### All 13 stories marked `[x]` — 19 of 43 requirements are PARTIAL
`STATUS.md:49-61` marks every core-hr story done with PRs #9–#22.

### Reverse drift — the ledger is *pessimistic* in four places

- **BUG-003** (cross-tenant via unvalidated `X-Tenant-Subdomain`), whose run-log body at `TEST-FINDINGS.md:322-329` still reads "CONFIRMED" against seven core-hr stories, **is fixed** — `TenantAccessGuardMiddleware.cs:34-49` 403s any request where `currentUser.TenantId != tenantContext.TenantId`, registered at `Program.cs:603` *after* `UseAuthentication()`. Credit where due: the ledger **header** at `:27-31` reconciles this correctly (fixed by #119, ISO-verified 2026-07-03); only the dated run-log body reads stale, and it is annotated as a historical record. **A live misreading hazard, not a ledger defect.**
- **ISSUE-018** (directory 403s HR Officers) is fixed — `EmployeesController.cs:179` now uses any-of `Employee.View.Own/Team/All`.
- The `sort`/`sortDirection` vs `sortBy`/`sortDescending` mismatch is fixed — `:189-197` binds both via `ResolveSort`.
- **BUG-010** (unaudited profile read) is fixed and correctly marked RESOLVED.

---

## GAPS RANKED

### 1. The `{entity}Id` vs `id` drift — systemic, 3 modules, dead write paths · **M**

The highest-blast-radius defect in the module. FE models declare `{entity}Id` where backend DTOs emit `Id` → `id`:

| FE model | FE field | BE field | Load-bearing use |
|---|---|---|---|
| `department.models.ts:13` | `departmentId` | `Id` | `track dept.departmentId`; `deactivateDepartment(dept.departmentId)` |
| `job-title.models.ts:18` | `jobTitleId` | `Id` | `track jt.jobTitleId`; `deactivateJobTitle(jt.jobTitleId)` |
| `location.models.ts:14` | `locationId` | `Id` | `track loc.locationId`; `deactivateLocation(loc.locationId)` |

**Every deactivate call sends `undefined` in the URL; every `@for` tracks `undefined`.** Departments and job titles are broken **twice over** — they also call `http.patch` against `[HttpPost(".../deactivate")]` → 405.

**Why this survived:** the Karma specs mock the impossible shape — `department.service.spec.ts:22,30,32` and `department-list.component.spec.ts:21,29,31` hand-supply `departmentId`, `managerName`, `employeeCount`, **none of which the backend emits.** Green suite over a dead feature.

*Fix:* rename the three FE fields to `id` (or add the defensive mapping **already present** at `document.service.ts:47-49`), and switch the two verbs to POST.

### 2. Employee directory calls the wrong endpoint entirely · **S**
`employee.service.ts:47,80` targets `/tenant/employees`; the whole US-CHR-003 implementation — phone search, multi-select filters, role-scoped visibility, xlsx export — lives at `/directory`. **Four of five ACs degrade, and `EmployeeDirectoryService` is effectively dead code.** Export is worse: `/export` does not exist → 404.
*Fix:* point at `directory` / `directory/export` **and** switch the FE envelope to `{data,total}` in the same change — doing one without the other trades a 404 for BUG-099.

### 3. Profile editing broken end-to-end via `xmin` vs `rowVersion` · **S**
`employee-profile.component.ts:2560` computes `Number(p.xmin)`; BE emits `rowVersion`. Every inline save ships a NaN concurrency token. Kills CHR-002 AC-2 **and** AC-3.

### 4. Status-change UI cannot render or confirm · **S**
Two independent shape breaks on one flow: `getValidTransitions()` types an array against an object payload (`@for` over a non-iterable), and `changeStatus()` reads `response.profile` which the BE never emits → TypeError swallows the success toast. The backend state machine is exact and idempotency-keyed — **purely FE contract.**

### 5. "My Team" renders permanently empty · **S**
BE returns `ApiResponse<DirectReportsResult>`; FE types a bare array, so `directReports().length` is `undefined`, the `@if` guard is falsy, and the list silently never renders. Plus four field mismatches, and separately the endpoint is gated `Employee.View.All` **which the built-in Manager role does not hold.**

### 6. Bulk-assign toast never fires; bulk-import error report shows `undefined` · **S each**
`totalSuccess`/`totalFailed` vs `successCount`/`failureCount`; `err.row` vs `rowNumber`.

### 7. Genuinely absent behaviour — small, mostly honest deferrals · **S**
- Salary grade not shown on the employee profile (CHR-005 AC-4 second clause).
- Offboarding-workflow trigger on termination (CHR-009 AC-3) — `OffboardingService.cs:372` runs the other direction.
- Department manager picker still a placeholder despite Employees shipping.
- Malware scanning defaults to `AllowWithLogVirusScanner` unless `VirusScanning:ClamAv:Host` is set (blank in `appsettings.json:44`). **A documented deployment decision, not a defect** — the real `ClamAvVirusScanner` exists and is fail-closed when configured. NFR-3 is unmet in any environment that has not set the host.

---

## COVERAGE SUMMARY

```
Requirements audited: 43 | IMPLEMENTED: 23 | PARTIAL: 19 | MISSING: 0 | CONTRADICTED: 1
```

**Where the failures concentrate — unambiguously: 18 of 19 PARTIALs fail at leg 2, and 17 of those 18 are the frontend.** Not one requirement in this module failed because backend logic was absent or stubbed.

The backend is genuinely strong: tenant global query filters present on **every** core-hr entity (`AppDbContext.cs:287-331`), uniform `TenantInterceptor` write-stamping, a status state machine matching FR-2 transition-for-transition, real cycle detection at both department and employee level, and dense leg-3 binding (38 backend test classes, 392 IEEE-829 TCs).

**The pilot lesson held, harder than expected.** Two modules escaped only because someone previously hit this class and added a normalization boundary (`document.service.ts:47-49`, `custom-field.service.ts:56`) — **proof the defect is known and was fixed pointwise rather than systemically.**

**Tenant isolation specifically:** no missing query filter, no missing write-stamping, no unscoped read on any entity this module introduces. The one historical hole (BUG-003) is closed. **Core HR's isolation posture is sound.**

---

## CONFIDENCE

- **95%+** on every FE/BE contract mismatch — both sides opened, field lists quoted, no inference from names. *(Orchestrator independently re-verified four.)*
- **95%** BUG-003 fixed — middleware logic and registration order read directly. Residual 5%: static reading cannot prove no bypass route; a replay test (tenant-A token against tenant-B subdomain expecting 403) would settle it.
- **90%** CHR-009 AC-3 — **the auditor overrode a sub-explorer's "payroll exclusion not implemented" claim.** The `// TODO(payroll)` comment at `EmployeeStatusService.cs:466` is stale; `PayrollRunProcessor.cs:236-237` and `PayrollRunService.cs:398` filter to Active/Probation, and accrual is gated by `IsActive`. Pull-side rather than push-side, which satisfies the AC. **Do not "fix" those TODOs by adding duplicate push-side logic — delete the comments instead.**
- **85%** on the exact runtime failure *mode* of the `xmin` break (400 on `null`→`uint` binding vs 409 on `OriginalValue = 0`). That it is broken is certain; which error surfaces needs a running stack.
- **90%** US-CHR-013 has no FE — four naming variants across the whole tree came back empty, clearing the ≥3-variant bar. *(Orchestrator confirmed: zero files.)*
- **What limited this pass:** static reading only; all NFRs (p95 latency, 2.5s page loads, WCAG AA, 10k-row export) untested and **not counted in the 43**. No test executed — leg 3 records existence only.

---

## OUT-OF-LANE

- **type:** test-integrity · **severity:** HIGH · **where:** `department.service.spec.ts:22,30,32`; `department-list.component.spec.ts:21,29,31`; `employee.service.spec.ts:247-249`; `my-team.component.spec.ts:20-43`; `employee-list.component.spec.ts:757-758` · **what:** FE specs fabricate response shapes the backend cannot emit (`departmentId`, `managerName`, `employeeCount`, `/export` URL, bare `IDirectReport[]`, `totalSuccess`/`totalFailed`), so suites pass green over dead features. · **suggested-action:** run `@test-authenticator` over `features/core-hr/**/*.spec.ts`; consider generating TS models from the OpenAPI doc — **this class will keep recurring while models are hand-written.**
- **type:** bug · **severity:** HIGH · **where:** `department.service.ts:68`; `job-title.service.ts:91` · **what:** deactivate uses `http.patch` against `[HttpPost]` routes → 405 regardless of the id bug. · **suggested-action:** file as a BUG; one-line fix each.
- **type:** doc-drift · **severity:** MED · **where:** `EmployeeStatusService.cs:465-467`; `JobTitleDto.cs:16-21`; `department-form.component.ts:156-176`; `job-title.models.ts:1-14`; `job-title-list.component.ts:212-217` · **what:** five stale *"not yet implemented until module X exists"* comments whose dependency modules shipped months ago. Two are **actively misleading** (payroll/leave exclusion *is* handled pull-side; job-title employee count *is* computed) and one causes a real UI gap. · **suggested-action:** sweep core-hr for `TODO(US-CHR-001)`/`TODO(payroll)`/`TODO(leave)`; delete or action each.
- **type:** risk · **severity:** MED · **where:** `docs/QA/TEST-FINDINGS.md:322-331` · **what:** BUG-003 run-log bodies still read "CONFIRMED" for seven core-hr surfaces; only the header records the #119 fix. **Any agent grepping for `CONFIRMED` re-derives a resolved critical isolation bug.** · **suggested-action:** add an inline `→ RESOLVED (#119)` pointer at the head of each dated run-log block.
- **type:** risk · **severity:** MED · **where:** `DependencyInjection.cs:914-918`; `appsettings.json:44` · **what:** `IVirusScanner` silently falls back to `AllowWithLogVirusScanner` when `ClamAv:Host` is blank (the committed default) — uploads stored unscanned with only a log line. · **suggested-action:** confirm production sets `VirusScanning__ClamAv__Host`; consider failing startup in non-Development when blank.
