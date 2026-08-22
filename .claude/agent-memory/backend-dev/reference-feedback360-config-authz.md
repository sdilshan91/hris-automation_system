---
name: reference-feedback360-config-authz
description: BUG-244 Feedback360 trio (#1 save/#2 form/#3 tracker) + tenant-configurable team-manager reviewer-config authz seam
metadata:
  type: reference
---

BUG-244 Feedback360 trio + tenant-configurable, team-scoped manager access to 360 reviewer config (US-PRF-005). Built on `fix/bug244-feedback360`.

**Three FE routes the BE lacked** (FE baseUrl `/api/v1/tenant/performance/feedback-360`, NO cycleId — controller `Feedback360Controller` prefix `api/v1/tenant/performance`):
- #1 `PUT feedback-360/employees/{employeeId}/reviewers` — full-replace of Peer/DirectReport rows only (Self/Manager server-owned, untouched). `SaveReviewersCommand`→`IReviewerAssignmentService.SaveReviewersAsync`. Returns `ReviewerConfigurationDto`.
- #2 `GET feedback-360/assignments/{assignmentId}/form` — `GetFeedbackFormQuery`→`IFeedback360Service.GetFeedbackFormAsync`. NO `[RequirePermission]`; in-service RLS = caller employee must == `assignment.ReviewerEmployeeId` else 403 `not_assigned` (mirrors SubmitFeedback). Questions projected from reviewee's **Goal** rows for `assignment.CycleId` (questionId=goal id, kind="Goal"); hydrate rating/comment from persisted `Feedback360Item`s when submitted.
- #3 `GET feedback-360/employees/{employeeId}/tracker` — `GetTrackerQuery`→`GetTrackerAsync`. overdue always 0 (no 360 window entity); minimum = `Min360PeerReviewers` for Peer else 0.

**Cycle resolution:** #1/#3 use `ResolveActive360CycleAsync` (private helper replicating `AppraisalCycleService.GetActiveAsync` query inline: most-recent Active by StartDate → 404 `no_active_cycle`, + `Is360Enabled` gate → 409 `not_360_enabled`). #2 uses `assignment.CycleId` directly.

**Tenant toggle:** `Tenant.AllowManagerReviewerConfig` bool default **true** — plain typed column (mirrors `PayslipYtdEnabled`/`PublicCareersEnabled`), NOT surfaced through the `TenantSettingsController` write surface yet (deferred TODO(admin-console), same as siblings). Migration `20260708143609_AllowManagerReviewerConfigTenantFlag` (default true so existing rows opt in). `HasDefaultValue(true)` in `TenantConfiguration`.

**Authz seam** `ReviewerAssignmentService.AuthorizeConfigureAsync(targetEmployeeId)` — 4 outcomes: (1) `Performance.Review.All` → allow unrestricted; (2) toggle-on + `Performance.Review.Team` + caller-is-target's-manager (`target.ReportsToEmployeeId == callerEmployee.Id`) → allow; (3) toggle-on + Review.Team + not-manager → 403 `not_team_manager`; (4) else 403 `forbidden`. Applied to GetConfiguration/Add/Remove/Save(#1)/Tracker(#3). Controller gates on those relaxed to `[RequirePermission("Performance.Review.All","Performance.Review.Team")]` (OR). NOT touched: Notify (stays Review.All HR-only), submit/results/report.

**Manager→direct-report model:** `Employee.ReportsToEmployeeId` (self-FK). Caller employee resolved via `Employees.UserId == _currentUser.UserId` (same as `Feedback360Service.GetCurrentEmployeeAsync`).

**Shared helper:** `HRM.Domain.Performance.ThreeSixtyCompletion.ByCategory(assignments)` — per-category Assigned/Completed tally, reused by both `Feedback360Service.Aggregate` and tracker #3.

**GOTCHA:** Do NOT add DI ctor params to `ReviewerAssignmentService` — `HRM.Tests/Unit/ReviewerAssignmentServiceTests.cs` + `Integration/Feedback360IntegrationTests.cs` hard-`new` it with a fixed arg list; a signature change breaks the build and I'm barred from touching tests. That's why active-cycle resolution is replicated inline instead of injecting `IAppraisalCycleService`.

**Also refactored** `GetConfigurationAsync` tail into private `BuildConfigurationAsync` (no auto-seed) so #1's save response reflects the just-saved state — `GetConfigurationAsync` still seeds first then calls it. Prevents removed peers from being re-seeded on the save response.

**Follow-up (2 sibling endpoints folded in to make the screens functional — the flagged BUG-243 GAP):**
- **Config LOAD** `GET feedback-360/employees/{employeeId}/config` (segment is `/config`, NOT `/reviewers`) → `GetActiveReviewerConfigurationQuery`→`GetConfigurationForActiveCycleAsync`. Same OR gate + `AuthorizeConfigureAsync` first (authz-before-cycle-probe), then `ResolveActive360CycleAsync`, then delegates to the seeding `GetConfigurationAsync(cycleId, employeeId)` (WITH Self/Manager auto-seed). Returns `ReviewerConfigurationDto`.
- **Submit BY ASSIGNMENT** `POST feedback-360/assignments/{assignmentId}/submit`, body `{ answers:[{questionId,rating,comment}] }` (`SubmitFeedbackByAssignmentRequest`/`FeedbackAnswerInput`) → `SubmitFeedbackByAssignmentCommand`→`SubmitFeedbackByAssignmentAsync`. NO `[RequirePermission]`; same RLS as #2 (caller==assignment.ReviewerEmployeeId else 403 not_assigned). Maps `questionId→GoalId` (exact inverse of #2 form projection, rating null→0 so reused validator rejects), then REUSES existing `SubmitFeedbackAsync(new SubmitFeedback360Input(assignment.CycleId, assignment.RevieweeEmployeeId, null, items))` for all guards (Is360Enabled/Pending/BR-3-dup/rating-range), then returns `GetFeedbackFormAsync(assignmentId)` (FE expects `IFeedbackForm` back, not the result-entry). Did NOT touch the existing cycle+employee-keyed submit action.
- Round-trip: #2 form question `QuestionId=goal.Id, Kind="Goal"` → submit `questionId→GoalId` → stored `Feedback360Item.GoalId` → #2 hydrates back by GoalId. Form currently emits Goal-kind questions only, so questionId→GoalId is exact; if Competency questions are ever added to the form, the submit mapper needs kind info.
