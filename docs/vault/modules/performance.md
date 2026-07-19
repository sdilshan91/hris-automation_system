---
type: module-note
module: performance
---

# Performance Management

Domain rules, edge cases, and FE↔BE contract notes for the Performance module.

## US-PRF-001 — Manager sets goals/KPIs for team members (FE established the module)

The frontend feature folder `src/frontend/src/app/features/performance/` was created
here. Lazy-loaded at `/performance` (roleGuard: Manager / HR Officer / HR Manager /
Tenant Admin). Two views: a team-goals dashboard (default) and a per-member
goal-setting form (`/performance/goals/:employeeId`).

### Goal validation rules (mirror the backend — keep both in sync)
- title ≤ 200 chars, description ≤ 2000 chars (FR-2).
- weight is a whole percent, **multiple of 5** (BR-3); per-employee weights must sum
  to **exactly 100%** (FR-3). The "Save Goals" button is gated on this; off-total
  shows the exact string **"Goal weights must total 100%"** (AC-3 — QA asserts it).
- 1-10 goals per employee per cycle (BR-2).
- category enum: `KPI` | `Competency` | `Project` (PascalCase strings, US-PLT-003).
- goal-setting status enum: `Draft`(0) | `Submitted`(1) | `Acknowledged`(2) | `Finalized`(3)
  (there is **no** `NotStarted` value — `GoalStatus.cs`). `Finalized` = the locked/signed-off state (BUG-056, #387).

### FE↔BE contract the FE service ASSUMES (backend agent must confirm/reconcile)
`apiBaseUrl` already includes `/api/v1`. All under `/performance`:
- `GET  /cycles/active` → `IAppraisalCycle` incl. **`goalSettingOpen: boolean`**
  (authoritative window gate — the FE renders read-only + the closed message off
  this flag, AC-5; it does NOT compute open/closed from the dates client-side).
- `GET  /cycles/:cycleId/team` → `ITeamGoalStatus[]` (employeeId, employeeName,
  jobTitle, status, goalCount, totalWeight) — drives the dashboard (AC-4).
- `GET  /cycles/:cycleId/employees/:employeeId/goals` → `IGoal[]` (prefill).
- `PUT  /cycles/:cycleId/employees/:employeeId/goals` with `{ goals: IGoalInput[] }`
  → returns the persisted `IGoal[]`. This is a **full replace** of the goal set, not
  a per-goal CRUD. Server re-validates 100%/count and notifies the employee (FR-7).
- **Finalize/lock (BUG-056, #387):** `POST /performance/goals/finalize` with `{ employeeId, cycleId }`
  requires the set to sum to **exactly 100%** (else `422 weight_not_100`), then transitions every goal to
  `Finalized`. Thereafter Create/Update/Delete/SaveGoals reject writes to that set with `409 goals_finalized`
  (immutable). **Re-open decision (DF-46):** HR (`SetGoal.All`) OR the finalizing manager may re-open with a
  mandatory audit reason, resetting the set to `Acknowledged` — endpoint not yet built (tracked DF-46).
- **Goal-read authz (#387/DF-18):** `GET goals/{id}` is report-scoped — allowed for HR (`SetGoal.All`),
  the goal's **owner** (self-read), or the direct manager; other in-tenant managers are denied.

If the backend lands per-goal CRUD instead of a bulk replace, the FE
`PerformanceGoalService` is the single-file change point.

### ⚠️ FE↔BE contract MISMATCH (US-PRF-001) — reconcile at US-PRF-004
The backend (authoritative, fully tested) actually shipped a DIFFERENT contract than the
FE service above assumes. They pass tests independently (mocked HTTP) but are NOT wired
end-to-end yet:

| Concern | FE assumes | BE (`GoalsController`) actually exposes |
|---|---|---|
| Base path | `/api/v1/performance` | `/api/v1/tenant/performance` |
| Active cycle | `GET /cycles/active` → `goalSettingOpen` flag | **no endpoint** (belongs to US-PRF-004) |
| Team dashboard | `GET /cycles/:id/team` | `GET /cycles/{id}/team-dashboard` |
| Employee goals | `GET /cycles/:id/employees/:eid/goals` | `GET /employees/{eid}/cycles/{id}/goals` |
| Save | `PUT …/goals` **full-replace** of `{goals:[]}` | per-goal `POST/PUT/DELETE /goals[/{id}]` |

Reconciliation is genuinely **blocked on US-PRF-004**: the FE renders the window gate off
an active-cycle endpoint that only exists once HR cycle-management lands. When US-PRF-004
is built, align `PerformanceGoalService` to the real routes (single-file change) and decide
full-replace vs per-goal CRUD (BE currently per-goal). Tracked in the US-PRF-001 PR.

## US-PRF-002 — Employee self-rates against goals ("My Review")

FE-only so far (backend not yet built). New employee-persona view, **separate top-level
route `/my-review`** (guard `['Employee','Manager','HR Officer','Tenant Admin']`) — NOT
under `/performance`, because `/performance` is gated to managers/HR. Mirrors the
`/my-payslips` self-service pattern (US-PAY-005). Files: `models/self-assessment.models.ts`,
`services/self-assessment.service.ts`, `components/my-review/`, `my-review.routes.ts`.

### FE↔BE contract the FE service ASSUMES (backend agent must build/reconcile)
`apiBaseUrl` includes `/api/v1`. All under `/performance/self-assessment`. Tenant +
employee resolved server-side from session (FE sends no ids); `Performance.Read.Self` + RLS.

- `GET  /performance/self-assessment/active` → `ISelfAssessment` — the whole "My Review"
  screen in one call: the active cycle, assigned goals (read-only goal fields + the
  employee's saved rating/achievement/comment/attachments), `ratingScaleMax`
  (tenant-configured scale, FR-2), and **`windowOpen: boolean`** (authoritative
  open/closed gate, AC-4 — FE renders read-only off this flag, NOT off dates).
- `PUT  /performance/self-assessment/{id}/draft` body `{goals:[{goalId,selfRating,
  achievementPercent,comment}]}` → `ISelfAssessment` (partial save, status stays Draft).
- `POST /performance/self-assessment/{id}/submit` same body → `ISelfAssessment`. Server
  re-validates all-goals-rated + each comment ≥20 chars, computes weighted self-score
  (FR-4), flips status→`Submitted`, locks, notifies the manager.
- `POST /performance/self-assessment/{id}/goals/{goalId}/attachments` multipart field
  **`file`** → `IAssessmentAttachment` (FR-5: ≤5 files, ≤10MB each; virus-scan + tenant
  storage). **Most speculative part of the contract** — the upload route/field is a guess.
- `DELETE /performance/self-assessment/{id}/attachments/{attachmentId}` → 204.

Status enum `SelfAssessmentStatus`: `NotStarted | Draft | Submitted` (PascalCase strings).
Closed-window message is the literal **"The self-assessment period for this cycle has ended"**
(AC-4 — QA asserts verbatim, exported as `WINDOW_CLOSED_MESSAGE`). Like US-PRF-001 this is a
thin single-file service so a route mismatch is a one-file fix; reconcile alongside US-PRF-004.

Deferred (AC-5 / FR-7 Hangfire deadline reminders) is a BACKEND concern — no FE work.
Rich-text comment is a plain textarea, drag-drop upload is a plain file input (§8 pragmatic).

## US-PRF-003 — Manager rates direct reports ("Team Reviews" + per-employee review)

MANAGER-side, FE-only so far (backend not yet built). Extends the `/performance`
area (manager/HR role-gated). Two child routes added to `performance.routes.ts`:
`/performance/team-reviews` (the AC-4 dashboard) and `/performance/reviews/:employeeId`
(the per-employee review). Files: `models/manager-review.models.ts`,
`services/manager-review.service.ts`, `components/team-reviews/`,
`components/manager-review/`.

The per-employee view is side-by-side (AC-1): employee self-rating/comment read-only
on the LEFT, manager rating/comment inputs on the RIGHT, stacking on mobile. Submit
(AC-2) is gated on all-goals-rated + each manager comment ≥20 chars; a blocked submit
shows a validation error LISTING the unrated goal titles (AC-3, pure helper
`unratedManagerGoalTitles`). After submit the view locks read-only (AC-5). The final
combined score (BR-4) is DISPLAY-ONLY off `finalScore` — the FE never computes it
(depends on tenant self/manager weights the server owns). No chart lib added (FE has
none); self-vs-manager comparison is the layout + `ratingBandClasses` green/yellow/red
badges (§8 radar chart skipped).

### FE↔BE contract the FE service ASSUMES (backend agent must build/reconcile)
`apiBaseUrl` includes `/api/v1`. All under `/performance/manager-review`. Manager +
tenant resolved server-side from the session (FE sends no tenant id);
`Performance.Review.Team` (direct reports, BR-2) / `Performance.Review.All` (HR, BR-3)
+ RLS. Bare payloads (US-PLT-001 unwrap); PascalCase enum strings (US-PLT-003).

- `GET  /performance/manager-review/cycles/active/team` → `IManagerTeamRow[]` (the
  AC-4 dashboard rows: reviewId, employeeId, employeeName, jobTitle, status, goalCount,
  selfSubmittedOn). Tolerates a `{ data }` page.
- `GET  /performance/manager-review/employees/{employeeId}/active` → `IManagerReview`
  (one employee's review for the active cycle: each goal's self-rating/comment +
  manager rating/comment, `ratingScaleMax`, `windowOpen` authoritative gate, and the
  computed `selfScore`/`managerScore`/`finalScore`). One call loads the whole screen.
- `PUT  /performance/manager-review/{reviewId}/draft` body `ISaveManagerReviewRequest`
  `{goals:[{goalId,managerRating,managerComment}],summaryComment,flag}` → `IManagerReview`
  (partial save).
- `POST /performance/manager-review/{reviewId}/submit` same body → `IManagerReview`.
  Server re-validates all-goals-rated + each comment ≥20 chars, computes weighted
  manager score + final combined score (FR-4/BR-4), flips status→`ManagerReviewSubmitted`,
  locks, notifies the employee (AC-2).

Status enum `ManagerReviewStatus`: `PendingSelfAssessment | SelfAssessmentSubmitted |
ManagerReviewSubmitted | Completed`. Flag enum `ReviewFlag`: `None | Recognition |
PromotionConsideration | PIP` (FR-6). Like US-PRF-001/002 this is a thin single-file
service so a route mismatch is a one-file fix — reconcile alongside US-PRF-004.

### Design choices
- Weight distribution bar is a **pure CSS/Tailwind stacked bar**, not chart.js
  (§8 suggested chart.js but no chart lib is a FE dependency — see frontend-dev
  memory `no-chart-lib-comparison-table`).
- Drag-reorder, cascade tree, and bulk template assignment (§8) were deferred as
  nice-to-haves; not implemented in US-PRF-001.

## US-PRF-004 — HR creates/manages appraisal cycles (Cycle Management UI)

HR/manager-side, FE-only so far (backend not yet built). Extends the `/performance`
area. Added FOUR child routes to `performance.routes.ts`: `/performance/cycles` (list,
the cycle-management entry point), `/performance/cycles/new` (create form),
`/performance/cycles/:cycleId/edit` (edit), `/performance/cycles/:cycleId` (dashboard).
Files: `models/cycle.models.ts`, `services/cycle.service.ts`, `components/cycle-list/`,
`components/cycle-form/`, `components/cycle-dashboard/`.

Key FE decisions:
- **No chart lib** (still none) → the dashboard phase timeline is a **vertical CSS/Tailwind
  stepper** with per-phase **progress bars**, NOT chart.js donuts (§8 nice-to-have).
- **Phase sequencing validation is a pure helper** `validatePhaseSequencing(phases,
  cycleStart, cycleEnd)` (FR-2/BR-3): sorts to canonical PHASE_ORDER, rejects reversed
  ranges, out-of-window dates, and overlaps (each phase must start strictly after the
  prev ends). Shared by the form gate + QA. `allowedTransitions(status)` is the FR-7
  status machine (Draft→Activate/Cancel; Active→Pause/Complete/Cancel; Paused→Resume/
  Complete/Cancel; Completed/Cancelled terminal).
- **Participant scope is self-contained** (FR-3): a `ParticipantScopeType` enum
  (`AllEmployees|Departments|Grades|CustomList`) + comma-separated id lists the FE sends
  raw; the **backend resolves ids→participants**. Deliberately did NOT couple to a Core
  HR department tree / employee picker (kept the contract thin, like US-PRF-003 deferrals).
- `canSave()` in the form is a **method, not a computed** — it reads `form.valid` which is
  not a signal, so a computed would cache against its signal deps and miss validity changes.

### ASSUMED backend contract (backend agent must build/reconcile) — US-PRF-004
`apiBaseUrl` includes `/api/v1`. All under `/performance/cycles`. Tenant resolved
server-side; `Performance.SetGoal.All` / `Performance.Publish.All` + RLS (BR-1/NFR-2).
Bare payloads (US-PLT-001 unwrap), PascalCase enum strings (US-PLT-003).
- `GET    /performance/cycles`                  → `ICycleSummary[]` (FR-7 list; tolerates `{data}`)
- `GET    /performance/cycles/{id}`             → `ICycle` (full detail: phases + scope)
- `POST   /performance/cycles`  body `ISaveCycleRequest` → `ICycle` (AC-1/AC-2; schedules Hangfire)
- `PUT    /performance/cycles/{id}`  body `ISaveCycleRequest` → `ICycle` (AC-5 edit/extend)
- `GET    /performance/cycles/{id}/dashboard`   → `ICycleDashboard` (AC-3 per-phase stats + overdue)
- `POST   /performance/cycles/{id}/transition`  body `{action, reason?}` → `ICycle` (FR-7; reason req. for Cancel/BR-6)
- `POST   /performance/cycles/{id}/clone`       body `{name, startDate, endDate}` → `ICycle` (FR-8)
- `GET    /performance/cycles/rating-scales`    → `IRatingScaleOption[]` (FR-6 scale picker; tolerates `{data}`)

DTO shapes (see `cycle.models.ts`):
- `ICyclePhase {kind: CyclePhaseKind, startDate, endDate}` where `CyclePhaseKind =
  GoalSetting|SelfAssessment|ManagerReview|Calibration|Publish`.
- `IParticipantScope {type, departmentIds[], gradeIds[], employeeIds[]}` (only the list
  matching `type` is populated).
- `ICycle` adds `ratingScaleId, selfWeight (0-100; manager=100-self), enable360,
  enableCalibration, participantCount, cancelledReason?`.
- `IPhaseStat {kind, startDate, endDate, completedCount, totalCount, overdueCount}`.
- Enums: `CycleStatus = Draft|Active|Paused|Completed|Cancelled`; `CycleType =
  Annual|Quarterly|Probation`; `CycleTransitionAction = Activate|Pause|Resume|Complete|Cancel`.

## US-PRF-005 — 360-degree review (peers, reports, manager, self)

HR/manager-side, FE-only so far (backend not yet built). Extends the `/performance`
area. Added THREE child routes to `performance.routes.ts`:
`/performance/feedback-360/:employeeId` (reviewer nomination + completion tracker,
AC-1/AC-3), `/performance/feedback-360/:employeeId/results` (aggregated dashboard,
AC-4), and `/performance/feedback-360/assignment/:assignmentId` (a single reviewer's
feedback form, FR-4/AC-2 — STATIC `assignment` segment so it doesn't collide with the
`:employeeId` config route). Files: `models/feedback-360.models.ts`,
`services/feedback-360.service.ts`, `components/feedback-360-config|-form|-results/`.

Key FE decisions:
- **No chart lib** (still none). §8 radar chart is rendered as a **non-chart CSS
  visual**: grouped horizontal bars per reviewer category for the perspective
  comparison + per-competency average bars + per-category split chips. Documented in
  the component header. (Consistent with US-PRF-003/004 and the no-chart-lib memory.)
- **Anonymity (FR-5/NFR-3) is rendered defensively**: the comment author block is gated
  on `results.anonymous`, NOT on the presence of `reviewerName`. So even if the backend
  mistakenly leaks a name under anonymity, the FE renders "Anonymous" and never shows
  it. The FE NEVER reconstructs identity — it renders only what the API returns. (A unit
  test asserts a leaked name is not in the DOM.)
- **BR-2 (self can't be a peer)** is enforced client-side (`isValidPeerNomination`)
  AND re-validated server-side: self is filtered from the candidate pool + suggestions,
  and `addReviewer(_, 'Peer')` toasts an error and refuses self.
- **Composite score (FR-6)** is **display-only** off `compositeScore` — the FE never
  computes it (depends on tenant per-category weights the server owns).
- **PDF export (FR-7)** is a BACKEND concern: the download button is wired only when
  `results.exportAvailable` is true; it reads the Content-Disposition filename off an
  `HttpResponse<Blob>`. Omitted entirely if the endpoint isn't there.
- Reviewer save is a **full-replace** of the manual Peer/DirectReport set; Self +
  the auto-assigned Manager are server-owned (`locked`) and never sent.

### ASSUMED backend contract (backend agent must build/reconcile) — US-PRF-005
`apiBaseUrl` includes `/api/v1`. All under `/performance/feedback-360`. Tenant + acting
user resolved server-side; `Performance.Review.All` (HR config) / reviewer ownership +
RLS (NFR-2/NFR-3). Bare payloads (US-PLT-001 unwrap), PascalCase enum strings
(US-PLT-003). Anonymity enforced server-side — results OMIT reviewer ids when on.
- `GET /performance/feedback-360/employees/{employeeId}/config` → `IReviewerConfig`
  (AC-1: auto Self+Manager, suggestedPeers/suggestedDirectReports, candidatePool,
  per-category minimums, anonymous flag, editable gate).
- `PUT /performance/feedback-360/employees/{employeeId}/reviewers` body
  `ISaveReviewersRequest {reviewers:[{reviewerId,category}]}` → `IReviewerConfig`
  (full-replace of manual Peer/DirectReport rows; Self+Manager not sent).
- `GET /performance/feedback-360/employees/{employeeId}/tracker` → `ICompletionTracker`
  (AC-3 per-category submitted/pending/overdue + minimum).
- `GET /performance/feedback-360/assignments/{assignmentId}/form` → `IFeedbackForm`
  (FR-4 competency/goal question cards + ratingScaleMax + submitted lock + anonymous).
- `POST /performance/feedback-360/assignments/{assignmentId}/submit` body
  `ISubmitFeedbackRequest {answers:[{questionId,rating,comment}]}` → `IFeedbackForm`
  (AC-3; marks Completed, BR-3 one-per-reviewer-per-cycle, locks).
- `GET /performance/feedback-360/employees/{employeeId}/results` → `IFeedback360Results`
  (AC-4: per-competency `overallAverage`+`byCategory`, `categoryAverages`,
  `compositeScore` FR-6, `comments` — reviewerName OMITTED when anonymous, `released`
  BR-4, `exportAvailable`).
- `GET /performance/feedback-360/employees/{employeeId}/results/export` (OPTIONAL) →
  `application/pdf` (HttpResponse<Blob> + Content-Disposition filename). FR-7.

Enums: `ReviewerCategory = Self|Manager|Peer|DirectReport`; `AssignmentStatus =
Pending|Completed|Overdue`; `QuestionKind = Competency|Goal`. Like US-PRF-001..004 this
is a thin single-file service so a route mismatch is a one-file fix. AC-2/AC-5 (in-app +
email reviewer notifications + Hangfire deadline reminders) are BACKEND concerns — no
FE work. NOTE: model fn `ratingBand360Classes` is renamed (vs US-PRF-003's
`ratingBandClasses`) to avoid a barrel re-export collision.

### ⚠️ US-PRF-001 reconciliation is NOW UNBLOCKED
US-PRF-004 lands the active-cycle/cycle-management endpoints US-PRF-001 was waiting on.
The US-PRF-001 path mismatch (`/performance` vs `/tenant/performance`, team vs
team-dashboard, full-replace vs per-goal CRUD — see the table above) can now be
reconciled once the BACKEND for both stories exists. NOT done in this FE-only US-PRF-004
pass (no backend to wire against yet); still a single-file change in `PerformanceGoalService`.

## US-PRF-006 — Meeting notes + digital sign-off (manager + employee)

FE-only so far (backend not yet built). Extends BOTH performance areas, keyed by the
US-PRF-003 manager-review `reviewId`:
- MANAGER: new child route `/performance/reviews/:employeeId/signoff?reviewId=…`
  (STATIC `signoff` segment declared BEFORE `reviews/:employeeId` so it matches first).
  Reached from a new **"Add Meeting Notes"** link in the locked-banner of the
  manager-review view (US-PRF-003), shown once the manager review is submitted.
- EMPLOYEE: new child route `/my-review/sign-off/:reviewId` (same employee
  self-service guard as `/my-review`).
- Files: `models/review-signoff.models.ts`, `services/review-signoff.service.ts`,
  `components/review-signoff/` (manager notes editor + request), `components/review-
  signoff-employee/` (acknowledge/dispute), `components/review-record/`
  (shared completed-record view: timeline + signatures + export, `[record]` input).

Key FE decisions:
- **No heavy RTE dep** (per the no-dep RTE convention): meeting notes use a self-
  contained `contenteditable` + `document.execCommand` editor inside the manager
  component (NOT the recruitment `RichTextEditorComponent` — kept performance feature
  self-contained). Notes are stored/echoed as sanitized HTML; rendered via Angular
  `[innerHTML]` which sanitizes (NFR-4 XSS) — never bypassSecurityTrust.
- **Template (AC-1)** is a pure helper `buildMeetingNotesTemplate(goals, scaleMax)`:
  strengths / areas for improvement / agreed actions w/ deadlines / overall summary +
  a "Goals reviewed" list referencing goal titles + manager ratings. HTML-escapes titles.
- **Timeline (AC-4)** is a pure helper `buildSignoffTimeline(record)`: uses the server
  `timeline` if present, else derives Notes Added → Sign-Off Requested → Employee
  Signed / **Disputed** → Completed from the signatures + status. No chart lib (CSS
  ordered-list stepper).
- **Dispute gate (FR-4)**: `canSubmitDispute` requires ≥`DISPUTE_COMMENT_MIN` (10)
  trimmed chars; submit button disabled until met.
- **Acknowledge (AC-3)** opens a touch-friendly confirmation modal with the VERBATIM
  copy `SIGNOFF_CONFIRM_MESSAGE` = "By signing, you acknowledge this review has been
  discussed." (exported; QA may assert). On confirm the server records the signature.
- **Export PDF (FR-6)** lives in `review-record`, wired only when
  `record.exportAvailable` is true; reads Content-Disposition filename off an
  `HttpResponse<Blob>` (the blob-export-download pattern).
- **Immutability (NFR-3)** is a BACKEND concern; the FE never edits a recorded
  signature — once status is SignedOff/Completed/NoResponse the record is read-only.
- BR-3 auto-close (NoResponse) is a BACKEND/Hangfire concern; the FE only renders the
  resulting status + badge.

### ASSUMED backend contract (backend agent must build/reconcile) — US-PRF-006
`apiBaseUrl` includes `/api/v1`. All under `/performance/sign-off`. Tenant + acting
user resolved server-side; manager/HR (`Performance.Review.Team`/`.All`) for notes +
request + resolve, review-ownership for acknowledge/dispute, + RLS (NFR-2). Bare
payloads (US-PLT-001), PascalCase enums (US-PLT-003). Keyed by the US-PRF-003 reviewId.
- `GET  /performance/sign-off/reviews/{reviewId}` → `IReviewSignoff` (whole record:
  meetingNotesHtml, status, manager/employee `ISignature`, disputeComments, goal+rating
  snapshot, optional `timeline`, `exportAvailable`).
- `PUT  /performance/sign-off/reviews/{reviewId}/notes` body `{meetingNotesHtml}` →
  `IReviewSignoff` (BR-1: only while `NotesDraft`).
- `POST /performance/sign-off/reviews/{reviewId}/request` body `{meetingNotesHtml}` →
  `IReviewSignoff` (AC-2: records manager signature, status→`PendingEmployeeSignOff`).
- `POST /performance/sign-off/reviews/{reviewId}/acknowledge` (no body) →
  `IReviewSignoff` (AC-3: records employee signature, status→`SignedOff`).
- `POST /performance/sign-off/reviews/{reviewId}/dispute` body `{comments}` →
  `IReviewSignoff` (FR-4/FR-5: status→`Disputed`, notifies manager+HR).
- `POST /performance/sign-off/reviews/{reviewId}/resolve` body
  `{resolution: 'Amend'|'Confirm', note?}` → `IReviewSignoff` (BR-4, HR-only).
- `GET  /performance/sign-off/reviews/{reviewId}/export` (OPTIONAL) → `application/pdf`
  (HttpResponse<Blob> + Content-Disposition). FR-6.

Enum `SignoffStatus` = `NotesDraft | PendingEmployeeSignOff | SignedOff | Disputed |
Completed | NoResponse`. Thin single-file service so a route mismatch is a one-file fix.
**Most speculative parts**: the `/sign-off` base path, the reviewId↔employeeId linkage
(manager route passes `?reviewId=`; falls back to the employeeId as the lookup id), and
whether the server returns `timeline` or the FE derives it. AC-2/AC-3 notifications +
BR-3 auto-close are BACKEND concerns — no FE work.

## US-PRF-008 — Performance Improvement Plan (PIP) (FE + BE both landed this branch)

HR-led, sensitive. EXTENDS `/performance` (HR/manager-gated) + `/my-review` (employee).
FE files: `models/pip.models.ts`, `services/pip.service.ts`, `components/pip-list/`,
`components/pip-form/`, `components/pip-detail/`, `components/my-pip/`. Routes added to
both `performance.routes.ts` (`pips`, `pips/new`, `pips/:pipId`) and `my-review.routes.ts`
(`pip/:pipId`). Confidentiality banner copy = `PIP_CONFIDENTIAL_NOTICE`; acknowledge copy
= `PIP_ACKNOWLEDGE_MESSAGE` (both exported, QA may assert verbatim).

Key FE decisions: no chart lib — the §8 timeline is a pure CSS/SVG duration bar with
checkpoint markers (`pipTimelineProgress` / `checkpointMarkerPosition` pure helpers) +
an accordion per objective. Role gating (BR-1) is derived client-side via `isPipHrRole`
/ `canSetOutcome` / `canEscalate` / `canRecordCheckpoint` pure helpers AND re-enforced
server-side: managers see Record-Checkpoint only, never close/extend/escalate. Checkpoint
modal uses the traffic-light selector (`CHECKPOINT_STATUS_OPTIONS`, green OnTrack / amber
AtRisk / red NotMet); file attach is multipart (field `file`) when present, JSON otherwise.
Lifecycle badges via `PIP_STATUS_BADGE`/`PIP_STATUS_LABEL`.

### ⚠️ FE↔BE contract MISMATCH (US-PRF-008) — reconcile in PipService (one-file fix)
The FE service ASSUMES a contract; the BE (`PipController`, fully tested) shipped a
DIFFERENT one. Both pass tests independently (mocked HTTP) but are NOT wired end-to-end.
Reconcile by editing `PipService` + `pip.models.ts` (the documented single change-point);
some field renames also touch the components (employeeNo, names→ids, enum split fields).

| Concern | FE assumes | BE (`PipController`/`PipDtos`) actually exposes |
|---|---|---|
| Base path | `/api/v1/performance/pips` | `/api/v1/tenant/performance/pips` |
| Create-form prefill | `GET /pips/draft?employeeId&reviewId` → `IPipDraft` | **NO draft endpoint** — FE must build the blank/prefilled form itself; origin review id is `OriginManagerReviewId` on the create body |
| Record checkpoint | `POST /pips/{id}/checkpoints/{checkpointId}` (per scheduled slot) | `POST /pips/{id}/checkpoints` — checkpoint identified by `CheckpointDate` in the body, **append-only** (no slot id; checkpoints are a history list, not pre-scheduled rows) |
| Checkpoint attach | multipart, server stores the file (field `file`) | metadata-only JSON: `AttachmentFileName/StorageKey/ContentType/SizeBytes` — **client uploads the blob elsewhere first**, then sends the storage key. The FE has no such upload step yet |
| Escalate path | `POST /pips/{id}/escalate` | `POST /pips/{id}/escalation` |
| Outcome enum | `ISetOutcomeRequest.outcome: PipOutcome` ('SuccessfullyCompleted'\|'Extended'\|'NotMet') | `SetPipOutcomeRequest.Outcome: PipOutcomeKind` (same names, **serialized as PascalCase via the platform enum-string convention** — confirm string vs int) + `Notes` field the FE omits |
| PIP DTO shape | `IPip` flat: `mentorName`, `escalationAction`, `escalation: IPipEscalation\|null`, `acknowledgement`, `acknowledgedSignature`, objectives carry **nested `checkpoints[]`** | `PipDto`: split `Status`/`StatusName` (+ same dual fields for escalation/ack), `EmployeeNo`, `OriginManagerReviewId`, `InitiatedAt`, top-level **`Checkpoints[]` + `Events[]` as flat lists** (NOT nested under objectives) + `FinalOutcomeNotes`/`EscalationNotes`/`EscalationConfirmed` |
| Objective shape | `successCriteria`, `dueDate`, nested `checkpoints` | `PipObjectiveDto`: `Description` is **nullable**, has `SortOrder`/`AddedAtExtension`, **no nested checkpoints** |
| Summary row | `objectiveCount`, `checkpointsRecorded`/`checkpointsTotal`, `jobTitle` | `PipSummaryDto`: `EmployeeNo`, `ObjectiveCount`, single `CheckpointCount` (no recorded/total split), `MentorName` (no jobTitle) |
| Acknowledge | `POST /pips/{id}/acknowledge` (no body) | **same** ✓ |

Biggest reconciliation items: (1) the FE groups checkpoints UNDER objectives for the
accordion, but the BE returns a FLAT `Checkpoints[]` keyed by date — the FE will need to
group client-side (no `objectiveId` link on the BE checkpoint, so grouping is by date/order,
not objective — may need a BE field). (2) The FE assumes the server stores the attachment;
the BE expects a pre-uploaded storage key — needs either a BE multipart variant or a FE
pre-upload step. (3) No draft/prefill endpoint — the "pre-filled when flagged" AC-1 path
needs either a BE endpoint or the FE to pass through the flagged employee via route state.
Tracked in the US-PRF-008 PR; deliberately NOT force-fitted here (FE specs are built around
the assumed contract and all pass; reconciliation is a deliberate single-file+models change).

## US-PRF-009 — Goal tracking with progress updates (FE-only so far; backend pending)

Employee + manager. EMPLOYEE "My Goals" lives under the self-service area at the NEW
route `/my-review/my-goals` (same employee guard as /my-review). MANAGER "Team Goal
Progress" is a NEW, DISTINCT route `/performance/team-goal-progress` — deliberately
SEPARATE from the existing US-PRF-001 `team-goals` goal-SETTING dashboard (that one
tracks goal-setting status; this one tracks goal PROGRESS). Do NOT clobber team-goals.

Files: `models/goal-progress.models.ts`, `services/goal-progress.service.ts`,
`components/my-goals/`, `components/team-goal-progress/`.

Key FE decisions:
- **No chart lib** (still none) → the §8 overall-completion widget is a pure SVG/CSS
  donut (`completionDonut` helper, reusing the US-PRF-007 `donutGeometry` pattern).
  Progress bars are animated CSS-transition fills colored by status.
- **BR-2 (100%→Completed, overridable)** is the pure helper `statusForProgress(pct,
  current)`: 100% forces Completed; dropping a stale Completed below 100% demotes to
  InProgress/NotStarted; AtRisk/Blocked chosen below 100% are left untouched. Unit-tested.
- **FR-4 weighted overall completion** is `weightedOverallCompletion(goals)` (pure):
  weighted average by goal weight, simple-average fallback when all weights are 0. The
  SERVER value (`IMyGoals.overallCompletionPercent`) is authoritative; the FE recompute
  is the optimistic-display fallback after posting an update.
- **Window gate (BR-1)** rendered off authoritative `windowOpen`, NOT cycle dates (like
  US-PRF-001/002). Closed message = `GOAL_WINDOW_CLOSED_MESSAGE` (exported; QA may assert).
- **Add Update** is a mobile-first bottom-sheet (NFR-4): slider (step 5) + status chips +
  notes textarea (≤2000, plain — no RTE) + optional ≤3 file attachments (10MB each).
  Multipart with repeated `files` field when attached, JSON otherwise.
- **History timeline (AC-3)** is lazily loaded on card expand; manager comment thread
  (FR-8) renders under each update. Manager side can POST a comment (FR-8) per update.
- Append-only (NFR-3): the FE exposes no edit/delete of a posted update.

### ASSUMED backend contract (backend agent must build/reconcile) — US-PRF-009
`apiBaseUrl` includes `/api/v1`. All under `/performance/goal-progress`. Tenant + acting
user resolved server-side (FE sends no ids for self views); `Performance.Read.Self`
(employee) + RLS for my-goals; `Performance.Review.Team`/`.All` (manager/HR) for team
views. Bare payloads (US-PLT-001), PascalCase enum strings (US-PLT-003).
- `GET  /performance/goal-progress/my-goals` → `IMyGoals` { cycleId, cycleName,
  windowOpen, overallCompletionPercent, goals[ IGoalProgress { goalId, title, target,
  weight, progressPercent, status, lastUpdatedOn, needsAttention } ] }. One call = the
  whole My Goals screen (AC-1).
- `GET  /performance/goal-progress/goals/{goalId}/updates` → `IGoalUpdate[]` (AC-3
  timeline; each update has progressPercent, previousProgressPercent, status, notes,
  authorName, createdOn, attachments[], comments[] FR-8). Tolerates `{ data }`.
- `POST /performance/goal-progress/goals/{goalId}/updates` body
  `IAddGoalUpdateRequest {progressPercent,status,notes}` → `IGoalUpdate` (AC-2/FR-2).
  Multipart (repeated field `files`, ≤3) when attachments present, JSON otherwise.
  Server appends + timestamps, notifies manager (FR-5), + HR if Blocked (BR-3).
- `POST /performance/goal-progress/updates/{updateId}/comments` body `{comment}` →
  `IGoalComment` (FR-8 manager/HR comment, ≤500 chars).
- `GET  /performance/goal-progress/team` → `ITeamGoalProgressRow[]` (AC-4: employeeId,
  employeeName, jobTitle, overallCompletionPercent, goalCount, goalsAtRisk,
  lastUpdatedOn). Tolerates `{ data }`. Restricted to direct reports server-side.
- `GET  /performance/goal-progress/team/{employeeId}` → `IEmployeeGoalProgress`
  (AC-4 drill-down: employee + their `IGoalProgress[]`).

Enum `GoalProgressStatus` = `NotStarted | InProgress | Completed | AtRisk | Blocked`
(§8 chip colors gray/blue/green/amber/red). AC-5 stale-goal Hangfire nudge + `needsAttention`
flag are a BACKEND concern (the FE only renders the flag). Thin single-file service so a
route mismatch is a one-file fix in `GoalProgressService`. **Most speculative parts**: the
multipart `files` upload field (server may instead want pre-uploaded storage keys, like
the US-PRF-008 reconciliation), and the team/{employeeId} drill-down shape.

## US-PRF-007 — Performance dashboard + analytics (BACKEND landed; FE pending)

Read-only analytics. **NO new entities, NO migration.** Aggregates LIVE over the existing
ManagerReview / SelfAssessment / Goal / Feedback360 / AppraisalCycle / CycleParticipant +
Core HR (Employee / Department / JobTitle / Location), all tenant-scoped by the EF global
query filters (NFR-2/AC-5). The headline score REUSES `ManagerReview.FinalScore` (BR-4) — the
dashboard never recomputes scores; only a SUBMITTED manager review counts as "scored".

Files: `IPerformanceDashboardService` + `PerformanceDashboardService` (Infrastructure),
`PerformanceDashboardDtos.cs` + `PerformanceDashboardQueries.cs` (Application/Features/Performance),
`PerformanceDashboardController.cs` (Api). DI in `DependencyInjection.cs`.

### Permission mapping (important for FE/QA)
The story names `Performance.Read.All` / `Performance.Read.Team`, which **do not exist** in
`PermissionCatalog`. Reused the existing equivalents: **`Performance.View.All`** (HR org-wide,
on HR Officer/Manager/Tenant Admin) and **`Performance.View.Team`** (Manager). Every endpoint
admits EITHER; the **service** resolves the scope. A caller with neither (e.g. Employee) is
**403 `forbidden`** server-side (BR-1 — FE also redirects).

### Scope enforcement (AC-5/BR-1/BR-3) — the critical part
`ResolveScopeAsync` reads the caller's permissions:
- `View.All` ⇒ **Organization** scope: all in-tenant employees, top-N AND bottom-N performers.
- else `View.Team` ⇒ **Team** scope: the in-scope employee id set is HARD-restricted to the
  caller's direct reports (`Employee.ReportsToEmployeeId == me`). A manager gets the team ranking
  as `topPerformers` and an **empty `bottomPerformers`** (BR-3). A manager drilling into a dept
  where they have no reports gets an empty list — they can never pull org-wide or non-report data.

### API contract (camelCase, ApiResponse<T> envelope) — base `/api/v1/tenant/performance`
- `GET dashboard/overview?cycleId&departmentId&gradeId&employmentType&locationId&topBottomCount=10&includeProbation=false`
  → `PerformanceDashboardDto` { cycleId, cycleName, scope ("Organization"|"Team"), ratingScaleMax,
  scoredEmployeeCount, averageScore, progress{ totalParticipants, goalSettingCompleted,
  selfAssessmentCompleted, managerReviewCompleted, signedOff, completionRate }, scoreDistribution[
  { rangeStart, rangeEnd, label, count } ], departmentAverages[ { departmentId, departmentName,
  headcount, averageScore } ], topPerformers[ { employeeId, employeeName, employeeNo, departmentId,
  departmentName, score } ], bottomPerformers[] }.
- `GET dashboard/department/{departmentId}?cycleId&…` → `DepartmentDrilldownDto` { cycleId,
  departmentId, departmentName, headcount, averageScore, employees[ { employeeId, employeeName,
  employeeNo, jobTitle, score?, status } ] }. (FR-5)
- `GET dashboard/trend?cycleIds=g1&cycleIds=g2&includeDepartmentSeries=false&…` → `PerformanceTrendDto`
  { scope, points[ { cycleId, cycleName, startDate, averageScore, scoredEmployeeCount } ],
  departmentSeries[ { departmentId, departmentName, points[] } ] }. Empty cycleIds ⇒ all cycles. (AC-3/FR-7)
- `GET dashboard/export?format=csv|xlsx&cycleId&…` → file download (`File(...)`, no envelope). (FR-8/AC-4)

Filters (FR-4) all combine (AND): cycle (default = most recently started cycle), department,
grade (resolved via `JobTitle.GradeId` — there's no Grade entity), employmentType (PascalCase enum,
e.g. "FullTime"), location (`Employee.LocationId`). BR-2: probation-status employees excluded
unless `includeProbation=true`. Distribution = unit-wide buckets across the rating scale, top
bucket inclusive of the max.

### Export decision (FR-8)
**Reused ClosedXML** (already a `HRM.Infrastructure` dependency) for XLSX + a hand-rolled CSV
StringBuilder — mirroring `RecruitmentDashboardService`. **PDF is a documented SEAM** (QuestPDF is
present-but-commented-out in `HRM.Infrastructure.csproj`); `NormalizeFormat` accepts only csv/xlsx,
anything else → 400 `invalid_format`. PDF + async large-dataset export are deferred (same posture as
the recruitment dashboard).

### EXTENSION POINT — materialized views / Redis (NFR-3/BR-4) — NOT built
The platform does **not** use Postgres materialized views or a Redis cache for these aggregates.
They are computed **live** per request with tenant-scoped EF GroupBy queries (correct + fine for
current data sizes). A future story can add a `performance_summary` materialized view + a Hangfire
4-hourly refresh + a Redis read-through cache keyed by (tenantId, cycleId, filter-hash, scope)
**without changing the contract** — the DTOs are the stable seam. Marked with comments in
`IPerformanceDashboardService` / `PerformanceDashboardService`.

### InMemory gotcha (for future dashboard work)
Projecting through a **required navigation that has its own query filter** (`e.Department.Name`,
`e.JobTitle.TitleName`) returned an EMPTY list under the EF InMemory provider. Fixed by selecting
the raw FK ids and resolving department/job-title NAMES via separate tenant-scoped lookups. Also:
captured `HashSet`/`IReadOnlySet.Contains` is not translated by InMemory — use `List.Contains` in
EF `Where` predicates.

## US-PRF-010 — Performance-based recommendations (promotion/bonus/increment) (FE-only so far; backend pending)

FINAL Performance story. HR-led, SENSITIVE compensation data (NFR-3/NFR-5). EXTENDS the
`/performance` area (HR/manager-gated). Added THREE child routes to `performance.routes.ts`:
`/performance/recommendations` (the HR workspace), `/performance/recommendations/summary`
(AC-4 aggregate; STATIC `summary` declared BEFORE `recommendations` so it matches first —
the workspace links to it relatively), and `/performance/team-recommendations` (AC-5 manager
view, DISTINCT from the HR workspace). Files: `models/recommendation.models.ts`,
`services/recommendation.service.ts`, `components/recommendation-workspace/`,
`components/team-recommendations/`, `components/recommendation-summary/`.

Key FE decisions:
- **No chart lib** (still none) → AC-4 increment/promotion distribution by department is
  pure CSS bars (`deptBarPercent` helper); the budget tracker (FR-8) is a CSS progress bar.
- **Auto-gen rule engine is a PURE model fn** (`matchAutoRule` / `buildAutoPreview`): the
  wizard PREVIEW (BR-3) is computed client-side from the current page rows so what HR sees
  equals what the backend will apply; strongest-threshold-wins. `apply` re-POSTs with
  `preview=false` to persist. Unit-tested without HTTP.
- **Override justification gate (FR-3)** is the pure `isOverrideJustified`: type None never
  needs a justification (clearing); any non-None recommendation requires a non-blank one.
  The drawer's `canSaveEdit()`/`justificationRequired()` are **methods, not computeds** — they
  read `form.*` plain-object props (not signals), so a computed would cache against empty deps
  (same reasoning as US-PRF-004's `canSave()`).
- **Budget thresholds (FR-8/BR-4)** are pure: `budgetHealth` green <80% / amber 80–100% /
  red >100% (Exceeded is a SOFT warning, never a hard block — `BUDGET_EXCEEDED_MESSAGE`). The
  bar width clamps at 100% even when consumed exceeds allocated.
- **Comparison cards (FR-5)** are the current-vs-recommended layout in the edit drawer.
- **Manager scope (AC-5)** is enforced server-side; the FE's TeamRecommendationsComponent only
  knows the `/team` endpoint (no workspace/edit/submit surface) — read-only direct reports.
- **Compensation visibility (§10)** is server-controlled: nullable comp fields render "—"; the
  workspace carries `compensationVisible`. **Export (FR-6)** wired only for
  `availableExportFormats` the backend reports (blob-download + Content-Disposition filename).
- **Access (NFR-5)** is the parent /performance role guard + server authz; no general employee
  access (no `/my-review` self-service route was added — recommendations are HR/manager-only).

### ASSUMED backend contract (backend agent must build/reconcile) — US-PRF-010
`apiBaseUrl` includes `/api/v1`. All under `/performance/recommendations`. Tenant + acting
user resolved server-side; `Performance.Publish.All` (HR org-wide workspace) /
`Performance.Read.Team` (manager direct reports only — AC-5) + RLS (NFR-2). Bare payloads
(US-PLT-001), PascalCase enum strings (US-PLT-003). Compensation encrypted at rest (NFR-3).
- `GET  /performance/recommendations/cycles/completed` → `ICompletedCycleOption[]` (BR-1; `{data}` ok)
- `GET  /performance/recommendations/workspace?cycleId&type&status&search&departmentId&sort&dir&page&pageSize`
  → `IRecommendationWorkspace` { cycleId, cycleName, page{ rows[ `IRecommendationRow` ], totalCount,
  page, pageSize }, budget `IBudgetTracker`, compensationVisible, availableExportFormats[] } (AC-1, paginated)
- `POST /performance/recommendations/auto-generate?preview=true|false` body `IAutoGenerateRequest`
  {cycleId, rules[ `IAutoGenerateRule` {minScore,type,amount?,percentage?} ], skipManualOverrides}
  → `IAutoGeneratePreview` {applied, rows[], affectedCount} (AC-2/FR-2/BR-3 preview-then-apply)
- `PUT  /performance/recommendations/{id}` body `IUpdateRecommendationRequest` → `IRecommendationRow`
  (§8 inline edit / FR-3 — server re-validates mandatory justification, 400 without it)
- `POST /performance/recommendations/{id}/submit` body {comment?} → `IRecommendationRow` (AC-3 → workflow)
- `POST /performance/recommendations/{id}/decision` body {decision:'Approve'|'Reject',comment?} → row
- `GET  /performance/recommendations/budget?cycleId` → `IBudgetTracker` {enabled,currency,allocated,consumed}
- `GET  /performance/recommendations/summary?cycleId` → `IRecommendationSummary` (AC-4/FR-6: totals,
  bonusPoolAllocated, byDepartment[ `IDepartmentRecommendationStat` ], comparison[ `ICycleComparisonStat` ])
- `GET  /performance/recommendations/team?cycleId` → `IRecommendationRow[]` (AC-5 direct reports; `{data}` ok)
- `GET  /performance/recommendations/export?format=Excel|Pdf&cycleId` → HttpResponse<Blob> (FR-6)

Enums: `RecommendationType = None|Promotion|Bonus|Increment|TrainingNomination|LateralMove|PipReferral`;
`RecommendationStatus = Draft|Submitted|PendingApproval|Approved|Rejected`. Thin single-file service so
a route mismatch is a one-file fix in `RecommendationService`. **Most speculative parts**: the
workspace filter/sort param names, the auto-generate `preview` flag vs a separate preview endpoint,
and the summary `comparison` shape (FR-7 cross-cycle history). Downstream Core HR/Payroll/Training
integration on approval (BR-6) + the approval workflow engine (FR-4) are BACKEND concerns — no FE work.
