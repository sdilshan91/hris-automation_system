---
type: module-note
module: recruitment
---

# Recruitment

Domain rules, edge cases, and decisions for the Recruitment module.

## Vacancy (US-REC-001)

The first Recruitment feature. Recruiter-facing internal app only — the anonymous
public careers page (FR-4/FR-5/NFR-5) is deferred to a later story.

### Frontend contract (camelCase DTOs, base `/api/v1/recruitment/vacancies`)
- `GET  /recruitment/vacancies?status=&departmentId=&search=&page=&pageSize=` → page envelope `{ data, total, page, pageSize }` (the FE also tolerates a bare array).
- `GET  /recruitment/vacancies/:id` → vacancy
- `POST /recruitment/vacancies` (body = create payload) → vacancy in `Draft`
- `PUT  /recruitment/vacancies/:id` (body = same payload) → updated vacancy
- `POST /recruitment/vacancies/:id/publish` → vacancy `Open` (backend validates BR-2 completeness)
- `POST /recruitment/vacancies/:id/close` → vacancy `Closed`
- `POST /recruitment/vacancies/:id/status` body `{ status }` → vacancy (backs the inline status dropdown)

Vacancy fields (camelCase): `id, referenceNumber, title, departmentId, jobTitleId,
employmentType, locationId, hiringManagerId, headcount, filledCount, salaryMin,
salaryMax, currency, description, qualifications, applicationDeadline, status, slug,
createdAt`. The FE create/update payload (`IVacancyRequest`) deliberately omits
`referenceNumber, slug, filledCount, status, tenantId` — those are server-managed.
The FE also expects display-name companions on reads (`departmentName`,
`jobTitleName`, `locationName`, `hiringManagerName`) so the list/table can render
without extra lookups.

### Business rules surfaced in the UI
- BR-2 publish-completeness: title + department + jobTitle + hiringManager +
  headcount(≥1) + description. FE validates this up front before calling `/publish`;
  the backend is still the authority. "Save as Draft" only requires `title`.
- Status enum + badge colors (§8): Draft=gray, Open=green, On Hold=amber,
  Closed=red, Cancelled=red.
- Salary range: max ≥ min (cross-field validator) when both set.

### Master-data dependencies
Form dropdowns reuse Core HR endpoints: `GET /departments`, `GET /job-titles`,
`GET /employees?search=` (hiring manager), and a `GET /locations` endpoint
(not yet confirmed in Core HR — backend should expose it or the FE location
dropdown stays empty, which is non-blocking since location is optional). All four
are normalized to a single `ILookupOption { id, label, sublabel? }` shape in
`VacancyService` so a DTO mismatch is a one-line fix.

## Public Careers + Application (US-REC-002)

The anonymous public careers experience deferred from US-REC-001. Built under
`features/recruitment/components/careers/`. Public routes (`/careers`,
`/careers/:id`) are a **top-level lazy group in app.routes.ts, OUTSIDE
MainLayout/authGuard** (only `tenantAvailabilityGuard`) so external applicants can
browse + apply with no session. Tenant is still resolved from the subdomain and
carried by the tenantInterceptor.

### Frontend contract (`CareersService`, camelCase DTOs)
- `GET  /careers/vacancies?search=&departmentName=&locationName=&employmentType=`
  → `IPublicVacancy[]` (tolerates a `{ data }` envelope). **Anonymous — no creds.**
  FE applies search/filters client-side over the fetched list (small per-tenant
  dataset); the server params are sent too if the backend wants to filter.
- `GET  /careers/vacancies/:id` → `IPublicVacancy`. Anonymous.
- `POST /careers/vacancies/:id/apply` — **multipart/form-data, anonymous** →
  `IApplicant` (AC-1). Fields: firstName, lastName, email, (phone), (coverLetter),
  `resume` file. Streams `reportProgress`.
- `POST /recruitment/vacancies/:id/applicants` — **multipart, withCredentials**
  (internal employee apply, AC-4/FR-8) → `IApplicant` (isInternal=true). Same form
  shape; backend links to the employee record.

`IApplicant`: id, applicationReferenceNumber, vacancyId, firstName, lastName,
email, phone, coverLetter, resumeFileName, stage, source, isInternal, appliedAt.
The multipart field naming lives in ONE place: `CareersService.buildFormData` — a
1-line fix if the backend expects different keys.

### Business rules surfaced in the UI
- AC-2 file validation (client-side, `CareersService.validateResume`): max 25 MB;
  allowed = PDF/DOCX/DOC. **Extension is the authoritative check** (browsers report
  `.doc` MIME inconsistently — often empty string); MIME is a secondary signal only
  when present. Backend still re-validates + virus-scans (FR-3/NFR-4).
- AC-3 duplicate: `CareersService.isDuplicate(err)` = HTTP 409 OR error body
  `code:'duplicate'`. Shown as an inline banner on the form, not a toast.
- AC-4 internal: right slide-over (`InternalApplyComponent`) pre-fills first/last
  (split from `auth.currentUser().displayName`) + email. Reached via an
  authenticated route `internal-careers/:id` with a BROAD role guard (Employee…),
  separate from the recruiter-only `recruitment/*` screens.

### Components
`careers-page` (listing+filters), `vacancy-detail` (detail + form + confirmation
screen with reference number), `application-form` (shared by public + internal,
reactive forms + signals + upload progress), `resume-upload` (CVA drag-and-drop,
keyboard-operable), `careers-branding` (logo + name + `--brand-primary`),
`internal-apply` (slide-over), `internal-vacancy` (authenticated container).

## Applicant Pipeline / Kanban (US-REC-003)

Recruiter-facing Kanban board for one vacancy's applicants. Reached from the
vacancy list "Pipeline" link → route `recruitment/vacancies/:vacancyId/pipeline`
(same recruiter `roleGuard` as US-REC-001). `PipelineService` + `pipeline.models.ts`
keep the contract in ONE place.

### Frontend contract (`PipelineService`, camelCase, base `/api/v1/recruitment`)
- `GET  /recruitment/vacancies/:vacancyId/pipeline?stage=&source=&from=&to=&search=`
  → `IPipelineBoard { vacancyId, vacancyTitle?, stages: IPipelineStage[], total }`.
  Each `IPipelineStage { stage, count, applicants: IApplicantCard[] }`. The FE
  **normalizes** the board: it fills in every canonical stage column even if the
  backend omits empty ones, and appends any non-canonical custom stages (§10)
  after the canonical set — so the backend may return only non-empty stages.
- `GET  /recruitment/applicants/:id` → `IApplicantDetail` (full applicant +
  `stageHistory: IStageTransition[]`). FE defaults `stageHistory` to `[]` if absent.
- `POST /recruitment/applicants/:id/stage` body `IStageChangeRequest { toStage,
  reason?, notes? }` → `IApplicantCard` (the updated card). Backend records the
  audit/history (BR-5) and is the authority on BR-3/BR-4 reason rules.
- `GET  /recruitment/applicants/:id/resume` → resume **blob** (responseType blob,
  observe response). FE reads the filename from `Content-Disposition`. We stream
  through the API rather than expose a blob-storage URL (NFR-5).

All recruiter requests use `withCredentials`; tenant scoping via tenantInterceptor
+ backend RLS (AC-5/NFR-3).

`IApplicantCard { id, firstName, lastName, email, source, isInternal, appliedAt,
stage }`. `ApplicantStage`/`ApplicantSource` reuse the US-REC-002 enums.

### Business rules surfaced in the UI
- Drag-and-drop (CDK `cdkDropListGroup`) moves cards between stages: **optimistic**
  UI (transferArrayItem + recount) then `POST .../stage`; on error roll back to a
  pre-move snapshot (AC-2/FR-3). Same-column reorder is local only, no server call.
- BR-3: moving to **Rejected** opens a reason dialog (canned dropdown
  `REJECTION_REASONS` + optional notes); persist is **deferred** until confirm,
  cancel rolls back. BR-4: a **backward** move (incl. moving OUT of Rejected) opens
  a free-text reason dialog. `moveRequiresReason()` in pipeline.models decides.
- Detail slide-over (AC-3/FR-7): right drawer, 65% desktop / full mobile, tabs
  Profile/Resume/Timeline/Interviews/Notes. **Interviews + Notes are placeholders**
  (no interview module yet). Resume tab = filename + Download; **no inline PDF
  preview** (pdf.js is not a dep — noted in the UI).
- FR-4 table view: flattened, client-sorted grid (name/email/stage/source/applied).
- FR-6 filters: sticky bar (search debounced 300ms, stage, source, applied
  from/to) → re-queries the board; counts come from the server response.
- NFR-4 mobile (<768px): columns horizontally scrollable + a stage-tab strip that
  shows one column at a time.

## Pipeline stage gates + actions (US-REC-004)

Deltas on top of REC-003 (board/drag-drop/detail/dialog/service unchanged in shape,
extended in place). Mapping stays in `pipeline.service.ts` + `pipeline.models.ts`.

### Move-stage contract additions (reconcile with backend)
The endpoint is still `POST /recruitment/applicants/:id/move-stage`.
- **Request** `IStageChangeRequest` adds `rejectionReason?: RejectionReason` — an
  ENUM string, one of `NotQualified | PositionFilled | Withdrew | Other`. Sent only
  when `toStage === 'Rejected'` (AC-4). `reason` (free text) is still sent too (it
  carries the human label for rejections, and the mandatory reason for backward moves).
- **Response** `MoveApplicantStageResultDto` adds optional `enteredStageAt?: string`
  (ISO, resets the time-in-stage badge) and `warnings?: string[]`. The service maps
  to `IStageMoveResult { id, stage, enteredStageAt, warnings }` and defaults
  `warnings` to `[]`.

### Soft vs hard gates (the key behaviour)
- Gates are **soft** (§10): headcount-filled (BR-4) or unmet gate criteria (FR-1)
  come back as `warnings[]` on a **successful** 2xx move. FE shows a success toast
  THEN one `toastr.warning(w, 'Heads up')` per warning. The move is NOT blocked.
- A **hard** failure — vacancy closed/cancelled (FR-8) — is an HTTP error (4xx/409);
  FE rolls back (board) / stays put (detail) and shows the error message verbatim.
- `RejectionReason` enum values MUST stay byte-identical to the backend enum. The FE
  source of truth is `REJECTION_REASON_LABELS` in pipeline.models.ts.

### Action buttons (mobile alternative to drag-drop, §8/FR-7)
The detail slide-over now has a footer: "Move to <nextStage>" (no reason),
"Move back to <prevStage>" (free-text reason dialog, FR-5), "Reject" (structured
dialog, AC-4). Terminal stages (Hired/Rejected) show no buttons. `nextStage()` never
advances into Rejected; the funnel order is Applied→Screening→Interview→Offer→Hired.
On success the drawer reloads its own detail AND emits `moved` so the board reloads.

### Time-in-stage badge (§8)
`timeInStageLabel(stage, enteredStageAt, appliedAt)` → "In Screening 5 days" /
"In <stage> today" / "" (graceful omission). Uses `enteredStageAt` if the backend
provides it, else falls back to `appliedAt`. Rendered as a neutral chip on each card.

## Interview scheduling (US-REC-005)

Recruiter-facing interview scheduling, reachable from the applicant-detail
**Interviews** tab (was a placeholder in REC-003). `InterviewService` +
`interview.models.ts` keep the contract in ONE place. New components under
`features/recruitment/components/`: `interview-form` (schedule/reschedule
slide-over), `interview-card` (presentational, reused on the timeline + agenda),
`interview-cancel-dialog` (confirm + reason), `interview-agenda` (tenant-wide
view). New route: `recruitment/interviews` (same recruiter `roleGuard`).

### Frontend contract (`InterviewService`, camelCase, base `/api/v1/recruitment`)
- `POST /recruitment/applicants/:applicantId/interviews` body `IInterviewRequest`
  → `{ interview, warnings? }` (FR-1/AC-1). **Soft** interviewer-conflict
  warnings (FR-7) come back as `warnings[]` on a **2xx** — the schedule still
  succeeds; the FE shows a success toast then a `toastr.warning` per warning. The
  service tolerates either `{ interview, warnings }` OR a bare `IInterview`.
- `GET  /recruitment/applicants/:applicantId/interviews` → `IInterview[]` (AC-4
  multiple rounds — backend assigns `roundNumber`).
- `PUT  /recruitment/interviews/:id` body `IInterviewRequest` → `{ interview,
  warnings? }` (AC-3 reschedule).
- `POST /recruitment/interviews/:id/cancel` body `{ reason? }` → `IInterview`
  (AC-3; backend notifies participants + removes the Hangfire reminder).
- `GET  /recruitment/interviews?interviewerId=&vacancyId=&from=&to=&status=` →
  `IInterview[]` (FR-5/FR-6 tenant-wide agenda).
- Interviewer lookup reuses Core HR `GET /tenant/employees?search=` (same as
  VacancyService) → normalized to `ILookupOption` with department as the sublabel.

`IInterview` ≈ `{ id, applicantId, vacancyId, roundNumber, interviewType
('in-person'|'video'|'phone'), scheduledDate (yyyy-MM-dd), startTime (HH:mm),
durationMinutes, location, videoLink, notes, status
('Scheduled'|'Completed'|'Cancelled'|'NoShow'), interviewers:[{employeeId,name,
department?}], applicantName?, vacancyTitle? }`. The request sends
`interviewerIds: string[]` (not the full objects). The DTO↔FE mapping lives in
`InterviewService.mapInterview` / `mapScheduleResult` — a field rename is a
one-line fix.

### Business rules surfaced in the UI
- Conditional required fields by type (FR-1): location required for `in-person`,
  videoLink (URL pattern) required for `video`, neither for `phone`. Validators
  are swapped on `interviewType` change; on submit the non-relevant field is
  nulled in the request body.
- BR-3/NFR-6: date input `min=today` + a cross-field `isPastDateTime` check blocks
  scheduling in the past (soft, client-side; backend re-validates).
- §8 status badge colors (single source `INTERVIEW_STATUS_BADGE`): Scheduled=blue,
  Completed=green, Cancelled=gray, NoShow=amber. Type badges tinted separately.
- FR-5 calendar: implemented as an **agenda/list grouped by date** (`groupByDate`
  pure helper), not a calendar grid (acceptable Phase-1 form; it is also the mobile
  view, NFR-5). Filters: interviewer typeahead, vacancy select, status, date range.
  Cards there are read-only (`showActions=false`, `showContext=true`).
- Notifications + the 24h Hangfire reminder (AC-1/AC-2/FR-3/FR-4) are entirely
  backend concerns; the FE only triggers schedule/reschedule/cancel.

## Interview scorecards (US-REC-006)

Structured interview scorecard: the assigned interviewer submits 1-5 ratings per
tenant criterion + an overall recommendation; the recruiter sees a consolidated
comparison on the applicant-detail **Scorecards** tab (was no tab before). New
files under `features/recruitment/`: `models/scorecard.models.ts`,
`services/scorecard.service.ts`, `components/scorecard-form/` (submit/edit
slide-over), `components/scorecard-panel/` (recruiter consolidated table +
anti-bias overlay). Reached from the new applicant-detail "Scorecards" tab AND a
"Submit/Edit scorecard" CTA added to `interview-card` (off by default, gated by
new `showScorecard`/`scorecardSubmitted` inputs + `scorecard` output).

### Frontend contract (`ScorecardService`, camelCase, base `/api/v1/recruitment`)
No backend scorecard endpoints existed when this FE was built — this is the FE's
PROPOSED contract for the backend agent to match:
- `GET  /recruitment/scorecards/criteria` → `IScorecardCriterion[]`
  `{ key, name, description?, displayOrder? }` (FR-1/BR-2 tenant defaults:
  Technical Skills, Communication, Problem Solving, Cultural Fit).
- `GET  /recruitment/interviews/:interviewId/scorecards` → `IScorecardBoard`
  `{ interviewId, scorecards[], aggregateAverage, isAssignedInterviewer?,
  hasSubmittedOwn?, canViewOthers? }`. Tolerates a bare `IScorecard[]` too (then
  FE computes the aggregate, assumes no gate). **`canViewOthers` defaults to TRUE
  when absent** so a plain recruiter view is never accidentally hidden.
- `POST /recruitment/interviews/:interviewId/scorecards` body `IScorecardRequest`
  `{ ratings:[{criterionKey, score(1-5), comment?}], overallRecommendation,
  generalNotes? }` → `IScorecard` (AC-1). Interviewer identity is server-side
  (BR-1) — NEVER sent by the FE.
- `PUT  /recruitment/scorecards/:id` body `IScorecardRequest` → `IScorecard`
  (FR-2/BR-4 edit until lock).

`IScorecard` ≈ `{ id, interviewId, interviewerEmployeeId, interviewerName,
overallRecommendation, ratings[], generalNotes?, averageScore (server-computed),
submittedAt, lockedAt?, editable?, isMine? }`. Mapping in `ScorecardService`
tolerates `fullName`→`interviewerName`. All withCredentials + RLS.

### Enum casing (US-PLT-003 — critical)
`OverallRecommendation` is `'StrongHire' | 'Hire' | 'NoHire' | 'StrongNoHire'`
(PascalCase = C# member names; the API uses a global JsonStringEnumConverter).
Source of truth is `RECOMMENDATION_OPTIONS`/`_LABELS`/`_BADGE` in
scorecard.models.ts. §8 colors: Strong Hire=green, Hire=light-green(emerald),
No Hire=orange, Strong No Hire=red.

### Business rules surfaced in the UI
- **Anti-bias (FR-6/BR-5):** the panel reflects the backend's `canViewOthers`
  flag — false blurs the table behind a "Submit your scorecard first" overlay.
  The backend is the authority (it omits the other cards); the FE never derives
  interviewer identity (the FE `IUser` has no employeeId anyway).
- **Editable until lock (FR-2/BR-4):** `isScorecardEditable()` treats
  `editable===false` OR a present `lockedAt` as locked; PUT re-checks server-side.
- Recommendation is mandatory (BR-3); all criteria must be rated before Review.
- Two-step form: fill → pre-submit summary with Edit → confirm (§8).

### No chart library (FR-8)
chart.js / ngx-charts / @swimlane are NOT project deps, so the "visual comparison"
is a clean **comparison table** (criteria × interviewers, per-card average +
highlighted aggregate), an acceptable Phase-1 form per the story note. Revisit
(radar/bar) only if a chart lib is added.

## Offer letters (US-REC-007)

Recruiter generates → previews → sends an offer, then records the applicant's
Accept/Decline or Withdraws it before acceptance. Reached from a new
applicant-detail **Offer** tab (offer history list with §8 status badges + a
"Generate offer" CTA). New files under `features/recruitment/`:
`models/offer.models.ts`, `services/offer.service.ts`,
`components/offer-form/` (a single slide-over that does both create AND
preview/actions). `OfferService.mapOffer` keeps the DTO↔FE mapping in ONE place.

### Frontend contract (`OfferService`, camelCase, base `/api/v1/recruitment`)
PROPOSED — no backend offer endpoints existed when this FE was built; the backend
agent should MATCH these (mirrors how US-REC-006 scorecards were done):
- `POST /recruitment/applicants/:applicantId/offers` body `IOfferRequest`
  → `IOffer` (status `Draft`; backend generates the PDF + ref no., AC-1/FR-1/FR-2).
- `GET  /recruitment/applicants/:applicantId/offers` → `IOffer[]` (history +
  versions, FR-9; tolerates a `{ data }` envelope).
- `GET  /recruitment/offers/:id` → `IOffer`.
- `GET  /recruitment/offers/:id/pdf` → **Blob** (responseType blob, observe
  response; filename from `Content-Disposition`, FR-4). Streamed through the API,
  not a direct blob-storage URL (NFR-3).
- `POST /recruitment/offers/:id/send` → `IOffer` (`Draft`→`Sent`, AC-2/FR-5).
- `POST /recruitment/offers/:id/respond` body `{ response: 'Accepted'|'Declined',
  notes? }` → `IOffer` (AC-3). **Accept advances the applicant to `Hired`
  server-side (BR-3)** — the FE reflects it by reloading the applicant detail and
  emitting `moved('Hired')` from the detail drawer.
- `POST /recruitment/offers/:id/withdraw` body `{ reason? }` → `IOffer`
  (`Withdrawn`, FR-8 — allowed only before acceptance).

`IOffer` ≈ `{ id, offerReferenceNumber, applicantId, vacancyId, status,
offeredPosition, departmentId/Name, reportingManagerId/Name, salaryAmount,
currency, salaryFrequency, benefitsSummary, startDate, expiryDate, probationMonths,
customClauses, version, sentAt, respondedAt, response, createdAt }`. All
withCredentials + RLS.

### Enum casing (US-PLT-003 — critical)
- `OfferStatus` = `'Draft'|'Sent'|'Accepted'|'Declined'|'Expired'|'Withdrawn'`.
- `SalaryFrequency` = `'Annual'|'Monthly'|'Weekly'|'Hourly'` (PascalCase — reconcile
  the member set with the backend `SalaryFrequency` enum; FE source of truth is
  `SALARY_FREQUENCY_OPTIONS` in offer.models.ts).
- §8 status badges (single source `OFFER_STATUS_BADGE`): Draft=gray, Sent=blue,
  Accepted=green, Declined=red, Expired=orange, **Withdrawn=gray + `line-through`**.

### Business rules surfaced in the UI
- Lifecycle gates are pure helpers (`canSendOffer`/`canRespondToOffer`/
  `canWithdrawOffer`): Send only for `Draft`; respond only for `Sent`; withdraw for
  `Draft`|`Sent` (before acceptance). The backend re-checks authoritatively.
- BR-6 expiry mandatory, **defaults to start+7 days** (`defaultExpiryDate`); a
  cross-field validator blocks expiry < start.
- The offer-form's `currentOffer()` = locally-updated signal `??` input offer, so
  a send/respond/withdraw reflects in the badge + footer **without a reload** (the
  input offer is immutable). Lesson learned the hard way in the spec.

### No inline PDF preview (FR-4)
pdf.js is still NOT a project dep, so the preview step shows the parsed offer
fields + a "Download" button that streams the server PDF (same pattern as the
US-REC-003 resume tab). Revisit an inline viewer only if pdf.js is added.

## Candidate portal — magic link (US-REC-008)

ANONYMOUS, magic-link candidate portal (NO auth/role guard — like the public
careers pages). Built under `features/recruitment/components/portal/`. New top-level
lazy UNGUARDED route `portal` in app.routes.ts (only `tenantAvailabilityGuard`),
mirroring the `/careers` wiring. The magic-link token comes from the URL
`?token=...`; the FE forwards it to the backend as a `token` QUERY PARAM on every
call (anonymous — NO withCredentials). `PortalService` + `portal.models.ts` keep
the contract + DTO mapping in ONE place.

### Frontend contract (`PortalService`, camelCase, base `/api/v1/careers/portal`)
PROPOSED — no backend portal endpoints existed when this FE was built; the backend
agent should MATCH these. All anonymous, token in the query string:
- `GET  /careers/portal/dashboard?token=...` → `IPortalDashboard
  { applicantName, applicantEmail, applications: IPortalApplication[] }` (AC-1/FR-2).
  Each application carries `vacancyTitle, departmentName, appliedAt, stage`, an
  optional `interview`, an optional `offer`, and a sanitized `timeline[]`.
- `GET  /careers/portal/offers/:offerId/document?token=...` → **Blob** (responseType
  blob, observe response; filename from Content-Disposition, FR-4).
- `POST /careers/portal/offers/:offerId/respond?token=...` body `{ response:
  'Accepted'|'Declined' }` → `IPortalOffer` (AC-3/FR-5). **Accept advances the
  applicant to Hired server-side (BR-3)** — the FE reflects it by RELOADING the
  dashboard after a successful respond. One-time (BR-2): the FE hides the buttons
  once `response` is set or status ≠ `Sent`; backend re-checks.
- `POST /careers/portal/request-link` body `{ email }` → 2xx (FR-8/BR-5). To avoid
  user enumeration (NFR-6) the backend should respond 2xx regardless and the FE
  shows the SAME generic confirmation even on error.

### Expired/invalid token (FR-8/BR-5)
`PortalService.isTokenError(err)` = HTTP **401 OR 410**. A missing token OR a
token error flips the component to the "request a new link" prompt (enter email →
`requestLink`). The backend MUST treat 401=invalid signature/applicant,
410=expired.

### Sanitized view (BR-3/NFR-5)
The backend MUST NOT expose rejection reasons, interviewer comments, or scorecard
details. The timeline labels are rendered as PLAIN TEXT (never innerHTML). The step
indicator funnel is Applied→Screening→Interview→Offer→Hired (`PORTAL_STEPS`);
**Rejected is NOT a step** — a rejected application renders a distinct "no longer
active" banner instead of the bar. `buildSteps(stage)` (pure) decides
completed(green)/current(blue)/future(gray).

### Components
`candidate-portal` (smart container: token from URL, load, error/expired/empty
states, respond+download orchestration), `portal-step-indicator`,
`portal-interview-card` (date/time, type badge, Join Meeting link or location,
interviewer names — AC-2/FR-3), `portal-offer-card` (terms + PDF download +
Accept/Decline with a one-time confirmation modal — AC-3/BR-2), `portal-timeline`
(vertical Notion-like activity log — FR-6). Reuses `careers-branding` for the
tenant logo/primary color header.

## Recruitment dashboard & analytics (US-REC-009)

Recruiter/HR analytics page under the recruiter-guarded `recruitment` route →
child route `recruitment/dashboard` (`RecruitmentDashboardComponent`).
`RecruitmentDashboardService` + `models/dashboard.models.ts` keep the contract +
DTO↔FE mapping in ONE place. ALL charts are pure SVG/CSS (no chart lib is a project
dep — same call as US-REC-006/US-ATT-010).

### Frontend contract (`RecruitmentDashboardService`, camelCase, base `/api/v1/recruitment/dashboard`)
Reconcile with the backend agent:
- `GET /recruitment/dashboard?from=&to=&departmentId=&vacancyId=` → `IRecruitmentDashboard`
  (tolerates a `{ data }` envelope). `from`/`to` are `yyyy-MM-dd` (FR-6); department/
  vacancy are the optional FR-7 drill-down. Backend RLS scopes to tenant (AC-5).
  The DTO = `{ range, kpis, funnel[], sources[], timeToHireTrend[], vacancyStatus[],
  recentActivity[] }`:
  - `kpis`: openVacancies, totalApplicants, hires, avgTimeToHireDays (BR-1),
    offerAcceptanceRate (0–100, BR-2), offersPending. Optional `previous*` companions
    drive the up/down trend arrows; FE treats them as optional.
  - `funnel[]`: `{ stage, count, conversionRate? }` — FE DERIVES conversionRate from
    adjacent counts when the backend omits it (BR-3, `funnelConversion`).
  - `sources[]`: `{ source, applicants, hires, conversionRate? }` — FE derives the
    per-source hire rate when absent. `source` is the ApplicantSource union (+ custom, BR-6).
  - `timeToHireTrend[]`: `{ label, avgDays }` (weekly/monthly buckets, FR-4).
  - `vacancyStatus[]`: `{ status, count }` — `status` is the **VacancyStatus** union
    (PascalCase, `OnHold` not "On Hold"). Donut + legend.
  - `recentActivity[]`: `{ id, type, description, occurredAt, applicantId?, vacancyId? }`.
    `type` is PascalCase ActivityType (`ApplicantApplied|StageChanged|InterviewScheduled|
    OfferSent|OfferAccepted|Hired`). FE renders `occurredAt` as a relative label.
- `GET /recruitment/dashboard/filters` → `{ departments: {id,label}[], vacancies: {id,label}[] }`
  (FR-7 drill-down options; non-blocking — empty list just hides options).
- `GET /recruitment/dashboard/export?from=&to=&format=csv|xlsx|pdf&departmentId=&vacancyId=`
  → file **blob** + Content-Disposition (FR-8). xlsx (ClosedXML) + pdf (QuestPDF) are
  server-generated; CSV is the Phase-1 button. FE streams + downloads (no client chart-to-PDF).

### Notes
- Default range = last 30 days (preset pills 7d/30d/90d/1y + custom; `presetRange`).
- Reloads on load + every filter change (BR-4: no real-time streaming). Custom range
  only reloads when `from <= to`.
- All pure helpers (donut/trend geometry, conversions, relativeTime, presetRange) are
  unit-tested in `dashboard.models.spec.ts`; the service spec flushes BARE DTOs.

## Convert applicant → employee (US-REC-010)

HR Officer converts a Hired applicant with an accepted offer into a Core HR
employee, pre-filling from the application + offer. New files under
`features/recruitment/`: `models/conversion.models.ts`,
`services/conversion.service.ts`, `components/conversion-form/` (the slide-over).
Wired into the existing `applicant-detail` drawer (the Convert action + "Converted"
badge live there). Completes the Recruitment module.

### Frontend contract (`ConversionService`, camelCase, base `/api/v1/recruitment/applicants`)
PROPOSED — reconcile with the backend agent:
- `GET  /recruitment/applicants/:id/conversion-prefill` → `IConversionPrefill`
  `{ applicantId, alreadyConverted, convertedToEmployeeId?, firstName, lastName,
  email, phone, jobTitleId/Name, departmentId/Name, reportingManagerId/Name,
  dateOfJoining, probationMonths, salaryAmount, currency, suggestedEmployeeNumber,
  vacancyId?/Title?/FilledCount?/Headcount? }` (FR-2/FR-3/FR-4). name/email/phone
  from the application; job title/dept/manager/salary/start date from the accepted
  offer; `suggestedEmployeeNumber` is the tenant pattern (FR-4).
- `POST /recruitment/applicants/:id/convert` body `IConvertEmployeeRequest`
  `{ firstName, lastName, email, phone, employeeNumber(null→auto), jobTitleId,
  departmentId, reportingManagerId, employmentType, workLocationId, dateOfJoining,
  salaryAmount, currency }` → `IConvertResult { employeeId, employeeNumber,
  firstName, lastName, userAccountCreated?, vacancyFilledCount?, vacancyHeadcount?,
  vacancyClosed? }`. Atomic server-side (NFR-3): creates employee, links applicant,
  bumps vacancy fill count, optionally creates the user account (BR-7).
- The applicant detail's `IApplicantDetail` gained `convertedToEmployeeId?` (FR-6/
  AC-4); `getApplicant` already spreads `...profile` so it flows through with no
  service change. The "Converted" badge + footer "View Employee Profile" use a plain
  `[href]="/employees/:id"` (NOT routerLink) to avoid adding ActivatedRoute to the
  existing applicant-detail spec.

### Behaviour surfaced in the UI
- **Convert visibility (FR-1/AC-1):** `canConvert()` = stage `Hired` AND an
  `Accepted` offer exists AND not already converted. The offers list is loaded
  EAGERLY when the applicant is Hired (not just when the Offer tab opens) so the
  button state is correct before the recruiter opens that tab.
- **Auto-filled marking (§8):** fields present in the prefill get an "auto-filled"
  chip + tinted input; all are editable (FR-3). Remaining required fields:
  employment type, work location (lookup), employee number.
- **Employee number (FR-4):** seeded from `suggestedEmployeeNumber`, read-only +
  disabled control until the user clicks "Override"; on override→auto it reverts and
  re-disables. The convert request sends `employeeNumber: null` unless overridden.
- **Confirmation step (§8):** form → confirm summary (name/position/department/start
  date/salary) → "Create employee". Then a success state with a "View employee
  profile" routerLink (the slide-over DOES use routerLink — its own spec adds
  provideRouter([])).
- **Blocked conversions:** `already_converted` (BR-2) and `plan_limit_reached`
  (BR-3) come back as HTTP errors with a typed `code`; shown as an inline red banner
  (with a "View employee record" link for BR-2), NOT a toast. Untyped errors fall
  back to a toast.
- Lookups (dept/jobTitle/location/manager) reuse `VacancyService` (same
  `ILookupOption` normalization, `/tenant/...` endpoints). `EmploymentType` is
  imported from the Core HR employees model (PascalCase: Full-Time/…).

## Rich text
Description + qualifications use a small in-repo `contenteditable` editor
(`RichTextEditorComponent`, a ControlValueAccessor) — NOT a 3rd-party lib — to
keep the build/test gate lean. Output is HTML displayed via Angular's default
`[innerHTML]` sanitizer (NFR-4); never bypassSecurityTrust. If a later story needs
tables/images in the JD, revisit (ngx-editor/TipTap).
