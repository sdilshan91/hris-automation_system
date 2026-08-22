---
name: reference-recruitment-module
description: Recruitment module scaffold decisions (US-REC-001 vacancy, US-REC-002 applicant) + where domain rules live
metadata:
  type: reference
---

Recruitment module shared domain rules + FE contract live in the vault at
`docs/vault/modules/recruitment.md`. Read it before any `US-REC-*` story.

US-REC-001 (vacancy) scaffold: `Vacancy` entity (BaseEntity), `VacancyStatus` enum +
`VacancyStatusTransitions`, `VacancySlugGenerator` (pure, in HRM.Domain/Entities), `IVacancyService`/
`VacancyService` + `IPublicCareersService`/`PublicCareersService`. Tenant-scoped recruiter routes
under `api/v1/recruitment/vacancies` (gated `Recruitment.View`/`Recruitment.Manage`); anonymous public
careers under `api/v1/careers/vacancies` (`[AllowAnonymous]`, tenant resolved by subdomain). Status +
EmploymentType stored as varchar(20) via HasConversion<string>(). Ref number `VAC-{year}-{seq:D4}`
unique per tenant via partial index (is_deleted=false).

US-REC-002 (applicant submission) added: `Applicant` entity (BaseEntity), enums `ApplicantStage`
(only `Applied` used now) + `ApplicationSource` (Public/Internal/Referral). `IApplicantService`/
`ApplicantService` + log-only `IRecruitmentNotificationService`/`LogOnlyRecruitmentNotificationService`
(mirrors LogOnlyLeaveNotificationService — FR-5 confirmation + FR-7 new-app notif, no real email).
Ref number `APP-{year}-{seq:D4}` (same gen pattern as VacancyService). Resume handling REUSES the
existing `IFileStorage` (LocalFileStorage) + `IVirusScanner` (AllowWithLogVirusScanner) seams — virus
scan BEFORE persisting the storage key; stored at `{tenantId}/recruitment/{vacancyId}/{applicantId}/{uuidName}`
(UUID-renamed for BR-3, original kept in ResumeFileName). MIME/size/cover-letter limits are PUBLIC
consts on `SubmitApplicationValidator` (`AllowedResumeMimeTypes`, `MaxResumeSizeBytes`=25MB,
`MaxCoverLetterLength`=2000) so the service + tests share them. Duplicate detection (AC-3/BR-1) = unique
partial index on (tenant_id, vacancy_id, email) + case-insensitive service pre-check (409
`duplicate_application`). Deadline check (BR-6): 409 `deadline_passed`; vacancy-not-open: 409
`vacancy_not_open`.

Routes (camelCase DTOs the FE consumes):
- `POST api/v1/careers/vacancies/{vacancyId}/apply` — ANONYMOUS, multipart (`resume` IFormFile +
  form fields firstName/lastName/email/phone?/coverLetter?) → `ApplicationConfirmationDto`
  {applicationReferenceNumber, vacancyTitle, email, appliedAt}.
- `POST api/v1/recruitment/vacancies/{vacancyId}/applicants` — `[Authorize]` (any logged-in user),
  multipart + `linkedEmployeeId` → internal app (isInternal=true, Source=Internal).
- `GET  api/v1/recruitment/vacancies/{vacancyId}/applicants?page=&pageSize=` — `Recruitment.View`,
  returns `{ data, total, page, pageSize }` (ApplicantPageResponse, mirrors VacancyPageResponse).
- `GET  api/v1/recruitment/vacancies/{vacancyId}/applicants/{id}` — `Recruitment.View` → ApplicantDto.

Permissions: REUSED existing `Recruitment.View`/`Recruitment.Manage` (no new catalog entry — Recruiter
role already holds both). Internal submit needs only auth, not a permission (any employee may apply).

DEFERRALS (do NOT build): real blob/ClamAV (seams only); real email/SignalR (log-only notif); EXIF
strip (FR-4) — resumes are PDF/DOC not images, noted TODO in ApplicantService; Postgres RLS (NFR-3) —
separate deferred story, tenant isolation is EF query filter + TenantInterceptor only.

US-REC-003 (pipeline + stage mgmt) added: `ApplicantStageHistory` entity (BaseEntity) — immutable
transition log (applicantId, fromStage, toStage, changedByUserId?, reason?, notes?, changedAt). New
DTOs in `PipelineDtos.cs` (board/column/card/detail/timeline/move requests). New service methods on
`IApplicantService`/`ApplicantService`: GetPipelineBoardAsync (groups by stage, one column per enum
stage incl. empty, counts+total, filters stage/source/appliedFrom/To/search), GetDetailAsync (profile
+ timeline + resumeDownloadUrl ref), MoveStageAsync, BulkMoveStageAsync (all-or-nothing). Move rules in
private `ApplyStageMove`: BR-3 Rejected needs reason, BR-4 backward (lower enum idx) needs reason,
no-op same-stage rejected (`stage_unchanged`), Hired allowed (convert-to-employee = US-REC-010 OOS).
Each move writes a history row + AuditLog (`recruitment.applicant.stage_changed`). New
`ApplicantPipelineController` (`api/v1/recruitment`): GET vacancies/{id}/pipeline (Recruitment.View),
GET applicants/{id}/detail (View), POST applicants/{id}/move-stage (Manage), POST
applicants/bulk-move-stage (Manage). Permissions REUSED View/Manage (no catalog change). DEFERRED:
signed resume URL + live resume download endpoint (IFileStorage has no read-stream method — only the
route ref is returned); interview scores/schedules (no interview module, US-REC-005/006, returns
empty); bulk email + CSV export (FR-8).

US-REC-004 (pipeline gates) EXTENDED the REC-003 move engine (did NOT rebuild it): new `RejectionReason`
enum (NotQualified/PositionFilled/Withdrew/Other). Added nullable `RejectionReason?` to BOTH `Applicant`
(current-state, cleared on reactivation) and `ApplicantStageHistory` (per-transition), varchar(30) via
HasConversion<string>(). `MoveStageAsync`/`BulkMoveStageAsync` now take an optional `RejectionReason?`
param (before the CancellationToken — keeps positional REC-003 callers compiling). New rules in
`ApplyStageMove` (now also takes vacancy + hiredCount + `out warnings`): AC-4/FR-3 reject needs structured
reason (`rejection_reason_required`, fired AFTER the BR-3 free-text `reason_required` so REC-003's
order-sensitive test stays green); FR-8 forward/active move blocked 409 `vacancy_not_active` when vacancy
Closed/Cancelled (rejection still allowed); BR-2 reactivation (Rejected→active) needs reason (treated like
backward). SOFT warnings (move still succeeds) returned on `MoveApplicantStageResultDto.Warnings: string[]`:
BR-4 headcount-filled (hiredCount>=Headcount when→Offer/Hired) + FR-1/BR-1 gate STUB ("No gate criteria
evaluated…US-REC-005/006") on →Interview/Offer. Per-transition notify via NEW
`IRecruitmentNotificationService.NotifyStageChangedAsync` (log-only seam, non-fatal try/catch, fired AFTER
SaveChanges). NEW API fields (camelCase): request `rejectionReason` (enum string) on move + bulk-move;
response `rejectionReason` + `warnings: string[]` on move result; timeline gains `rejectionReason` +
`rejectionReasonName`. DEFERRED: real email/Hangfire queue (NFR-5, still log-only); interview/scorecard
gates (US-REC-005/006, stub always passes); optimistic concurrency token (skipped — would risk REC-003
applicant tests, not added).

US-REC-009 (recruitment dashboard + analytics — read-only aggregation, NO new entities) added:
`IRecruitmentDashboardService`/`RecruitmentDashboardService` (mirrors AttendanceDashboardService) +
`RecruitmentDashboardQuery`/`ExportRecruitmentDashboardQuery` (thin MediatR handlers) + pure
`RecruitmentAnalytics` helper in HRM.Domain/Entities (ConversionRate/AcceptanceRate/TimeToHireDays/
AverageDays/SourceConversionRate/TrendBucket — unit-tested). DTOs in `RecruitmentDashboardDtos.cs`.
New `RecruitmentDashboardController` under `api/v1/recruitment`: GET `/dashboard` + GET
`/dashboard/export`, BOTH gated `Recruitment.View` (story's `Reports.View.*` is NOT a catalog entry —
reused Recruitment.View). Aggregates applicant/applicant_stage_history/vacancy/offer, all tenant-scoped
by EF query filters (AC-5). Funnel "reached stage" = current-stage rank OR a ToStage history row (Rejected
ranks -1, off the funnel). BR-1 time-to-hire = earliest ToStage==Hired history row vs applied_at. BR-2
acceptance = accepted/sent offers SENT in period. Trend bucket = month when range>90d else ISO-week.
Recent activity capped at 20. DEFERRED (do NOT build): PDF export (FR-8 — CSV+XLSX/ClosedXML only; did
NOT reuse QuestPDF), async Hangfire export (NFR-5), Redis pre-agg + mv_recruitment_analytics (NFR-3),
Postgres RLS (NFR-2/US-PLT-002). No migration (read-only). Tests: 12 unit + 14 integration (InMemory).
NOTE: test-integrity-guard FALSE-POSITIVES on the bare word "pending" in test prose — reword to e.g.
"awaiting response"; the `OffersPending` identifier is fine.

US-REC-010 (convert accepted applicant → employee — COMPLETES the module) added: 3 nullable fields on
`Applicant` (`ConvertedToEmployeeId`/`ConvertedAt`/`ConvertedByUserId`) + partial unique index on
(tenant_id, converted_to_employee_id) WHERE not null. New `IApplicantConversionService`/
`ApplicantConversionService`, `ConvertApplicantToEmployeeCommand` + `GetConversionPrefillQuery`,
`ConvertApplicantToEmployeeValidator`, `ConversionDtos.cs` (ConversionPrefillDto + ConversionResultDto).
REUSES `IEmployeeService.CreateAsync` for the employee create (gets email-uniqueness, dept/jobtitle
validation, BR-3 plan-limit via Tenant.MaxEmployees, FR-4 auto employee-no) then patches the 3 fields
CreateEmployeeRequest lacks (EmployeeNo override, LocationId FK, ReportsToEmployeeId) on the tracked
entity in the SAME transaction. ATOMIC (NFR-3): `Database.IsRelational()`-guarded BeginTransaction (no-op
single SaveChanges on InMemory). Eligibility (FR-1): Stage==Hired + an OfferStatus.Accepted offer (highest
version). Dup (FR-10/BR-2): 409 `already_converted`. Vacancy FR-7/BR-5: FilledCount++ and auto-Close when
>=Headcount (Open/OnHold only). New `ApplicantConversionController` (`api/v1/recruitment`): GET
`applicants/{id}/conversion-prefill`, POST `applicants/{id}/convert`. BOTH guarded with TWO RequirePermission
attrs `Recruitment.Manage` AND `Employee.Create` (ANDed; story's `.All` wildcards aren't catalog entries;
this excludes the plain Recruiter who lacks Employee.Create → matches HR Officer persona). DEFERRED: FR-5/BR-7
auto-create user account (NO "auto-create on hire" tenant setting exists → default OFF, UserAccountCreated
always false); FR-8 onboarding (no module); FR-9 welcome email (log-only seam via NotifyStageChangedAsync);
RLS NFR-2 (US-PLT-002). error codes: applicant_not_found(404)/applicant_not_hired(409)/no_accepted_offer(409)/
already_converted(409). Tests: 6 unit + 10 integration (InMemory), full suite 1386 green.

Migrations: `20260615023856_Recruitment_Applicant` (applicant table); `20260615030003_Recruitment_
ApplicantStageHistory` (applicant_stage_history table only — no drift, FK to applicant, ix on
(tenant_id, applicant_id)); `20260615034513_Recruitment_ApplicantStageGates` (adds rejection_reason
varchar(30) nullable to BOTH applicant + applicant_stage_history); `20260615090214_Recruitment_
ApplicantConversion` (adds converted_at/converted_by_user_id/converted_to_employee_id + partial unique ix
on (tenant_id, converted_to_employee_id) to applicant — no drift).
Related: [[feedback-integration-tests-inmemory]].
