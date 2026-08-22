---
name: reference-validation-cluster-seams
description: Where graceDays/grade/goal-weight validation actually lives + two blocked findings (no Grade entity, contradictory JobTitle tests)
metadata:
  type: reference
---

Seam locations + blockers found while fixing the `fix/validation-logic-cluster` findings (2026-07).

- **Tenant terminate `graceDays` default (BUG-002) lives in `TenantLifecycleService.TerminateAsync` (US-ADM-004), NOT employee status (US-CHR-009).** `graceDays` exists ONLY in the tenant-lifecycle chain (`TerminateTenantRequest`/`Command`/`Input` → `TenantLifecycleService`). The employee `EmployeeStatusService` terminate has no grace concept. US-ADM-004 AC-3/FR-2 default = **30 days** ("plan-configurable" but no per-plan grace column is wired). Pattern used: make `GraceDays` `int?`; validator allows `null` (`Must(g => g is null or (>= 7 and <= 90))`); service treats `null OR 0` as omitted (`input.GraceDays is > 0 ? .Value : 30`). The regression test `TenantTerminateGraceDaysDefaultTests` passes `GraceDays: 0` (not null), so the service MUST accept 0 as "omitted", while `TenantLifecycleValidatorTests` still requires a supplied `0` to be rejected at the validator layer — both hold because they hit different layers.

- **No `Grade`/`SalaryGrade` entity or DbSet exists anywhere (deferred to the Payroll module).** `JobTitle.GradeId` is an FK-less nullable UUID (see `JobTitleConfiguration` TODO). So ISSUE-021 ("validate gradeId against the tenant grades set") is **BLOCKED** — there is nothing to `AnyAsync` against. Worse: the new `JobTitleGradeValidationTests` (random gradeId → REJECT) **directly contradicts** the pre-existing `JobTitleServiceTests.Create_WithGradeId_ShouldSucceed` / `Update_ChangeGradeId_ShouldSucceed` (random gradeId → SUCCEED). Cannot satisfy both without editing `HRM.Tests/`. Fixing needs a real Grade entity + reconciling the old tests — a caller decision, not a surgical validation add.

- **Goal weights "exactly 100%" (BUG-056) has no enforcement seam.** `GoalService` persists goals one-at-a-time as `Draft`; there is no batch "Save Goals" or submit/finalize endpoint (no status-transition method despite `GoalStatus.Submitted/Acknowledged`). Only the **over-100%** case is enforceable at per-goal create (already done: `newTotal > 100 → 422`). Under-allocation (e.g. 95%) can't be rejected mid-build. Correct home for `==100` is a future submit endpoint. No regression test forces it.

- **Localization validation (BUG-005) seam is in-service** in `TenantSettingsService.UpdateLocalizationAsync` (where the language check already lived), not a FluentValidation validator. FE↔BE contract (`company-settings.models.ts`): exactly 4 date-format tokens (`dd MMM yyyy`, `MM/dd/yyyy`, `dd/MM/yyyy`, `yyyy-MM-dd`), IANA time zones (validate via `TimeZoneInfo.TryFindSystemTimeZoneById` — works on .NET 10 ICU), ISO-4217 currencies. Added helpers `SupportedDateFormats` + `IsoCurrencyCodes` (RegionInfo-derived, seeded with the 6 FE currencies) under `HRM.Application/Common/Security` mirroring `SupportedLanguages`.

- **Pre-existing (not-mine) failing unit tests on this branch:** `AccountLockoutTests.ResetPasswordAsync_ClearsLockout`, `EmployeeProfileServiceTests.UpdateProfile_AsEmployee_ContactInfo/EmergencyContacts_ShouldSucceed`, `LeaveDashboardServiceTests.GetMyBalances_NoLedgerOrRequests_...` — fail in isolation, touch auth/employee/leave code unrelated to tenant/attendance/settings work.
