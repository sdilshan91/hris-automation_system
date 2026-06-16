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
- goal-setting status enum: `NotStarted` | `Draft` | `Submitted` | `Acknowledged`.

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
