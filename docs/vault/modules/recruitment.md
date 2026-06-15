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

### Rich text
Description + qualifications use a small in-repo `contenteditable` editor
(`RichTextEditorComponent`, a ControlValueAccessor) — NOT a 3rd-party lib — to
keep the build/test gate lean. Output is HTML displayed via Angular's default
`[innerHTML]` sanitizer (NFR-4); never bypassSecurityTrust. If a later story needs
tables/images in the JD, revisit (ngx-editor/TipTap).
