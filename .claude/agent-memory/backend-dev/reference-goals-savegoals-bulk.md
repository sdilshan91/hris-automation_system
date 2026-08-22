---
name: reference-goals-savegoals-bulk
description: BUG-243 bulk full-replace SaveGoals endpoint — route, semantics, reused GoalService rules, one-notification rule
metadata:
  type: reference
---

BUG-243 BE half: bulk full-replace goals endpoint for Performance module.

- Route: `PUT api/v1/tenant/performance/employees/{employeeId:guid}/cycles/{cycleId:guid}/goals`
  on `GoalsController`; auth `[RequirePermission("Performance.SetGoal.Team","Performance.SetGoal.All")]`.
- Chain: `SaveGoalsRequest{List<SaveGoalItem>}` → `SaveGoalsCommand` → `IGoalService.SaveGoalsAsync` →
  `GoalService.SaveGoalsAsync`. Returns `ApiResponse<EmployeeGoalsDto>` (reuses existing DTO for TotalWeight).
- Full-replace semantics for (employeeId,cycleId): item WITH matching Id → update; WITHOUT/unknown Id →
  create (Status=Draft, fresh UuidV7 — provided-but-unknown Ids are NOT honored); existing goal absent
  from set → soft-delete (IsDeleted=true). One `SaveChangesAsync` (no manual tx — avoids BUG-068).
- Reuses GoalService seams verbatim: `AuthorizeForEmployeeAsync` (BR-4/403 not_direct_report),
  `GetOpenCycleAsync` (BR-1/409 goal_setting_closed), count ≤ 10 (BR-2/409 goal_limit_reached, 0..10 ok),
  total weight ≤ 100 (FR-3/422 weight_exceeds_100 — NOT ==100, that's submit-time), parent-goal existence
  (batched single query on distinct ParentGoalIds; self-parent rejected).
- Audit (FR-6): per-change `Goal.Created`/`Goal.Updated`/`Goal.Deleted` via existing `AddGoalAudit` +
  `SnapshotGoal`. Note: `SnapshotGoal` JSON-escapes `&` → `&`; don't assert raw `&` in audit-body tests.
- Notification (FR-7): exactly ONE `NotifyGoalChangedAsync("goal-assigned", resulting[0].Id, emp, cycle)`
  after success; skip entirely when the set is emptied. NOT one-per-goal.
- `_dbContext.Goals` already excludes IsDeleted (global soft-delete+tenant filter), so existing-set load
  needs no explicit `!IsDeleted`; assert soft-deletes via `.IgnoreQueryFilters()`.
- Weight multiple-of-5 (BR-3) lives in `GoalValidators` (`SaveGoalItemValidator` reusing `GoalFieldRules`,
  wired via `RuleForEach` in `SaveGoalsValidator`) — NOT re-checked in the service (matches Create/Update).
- Tests: `HRM.Tests/Unit/GoalServiceSaveGoalsTests.cs`, InMemory-through-real-EF, mirrors GoalServiceAuditTests.
