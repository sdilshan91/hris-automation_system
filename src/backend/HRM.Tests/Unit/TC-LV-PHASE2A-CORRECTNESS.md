# IEEE-829 Test Cases — Phase 2a correctness (Leave module)

Regression test cases for two landed correctness fixes on `fix/phase2a-correctness`.
Automated in `LeaveReportServiceTests.cs` (Item 1) and `LeavePayrollLockServiceTests.cs`
(Item 2). Real seams only (EF InMemory + real services); no behaviour mocked.

---

## TC-LV-012-COV — Department Leave Calendar Coverage report (US-LV-012 FR-1)

- **Objective:** verify `BuildDepartmentCalendarCoverageAsync` (via
  `GenerateReportAsync(DepartmentCalendarCoverage)`) emits one row per (department, day)
  with people off, with correct On Leave / Headcount / Coverage % and correct scoping.
- **Requirement:** US-LV-012, AC/FR-1 (calendar coverage), BR-1 (tenant isolation).
- **Preconditions:** tenant resolved; a department with N active employees + approved
  `LeaveRequest`s overlapping the report range.
- **Category tags:** [x] Happy path · [x] Boundary · [x] Multi-tenant isolation.

| ID | Scenario | Expected |
|----|----------|----------|
| TC-LV-012-COV-01 (`DeptCoverage_FullDayCountsOne_AndSkipsDaysWithNobodyOff`) | Alice off 1 full day in a 4-day window, Bob not off | Exactly one row (Engineering, 2026-03-10): On Leave `1`, Headcount `2`, Coverage `50`; no rows for days with nobody off |
| TC-LV-012-COV-02 (`DeptCoverage_HalfDayCountsHalf_ISSUE_LV012`) | Alice half-day (`IsHalfDay`) | On Leave `0.5`, Coverage `75` = (2−0.5)/2×100 |
| TC-LV-012-COV-03 (`DeptCoverage_TwoPeopleOffSameDay_SumsOnLeave`) | Alice full + Bob half, same day | On Leave `1.5`, Coverage `25` |
| TC-LV-012-COV-04 (`DeptCoverage_TerminatedExcludedFromHeadcount`) | Terminated Dan added to dept | Headcount stays `2`, Coverage `50` (not 66.67) |
| TC-LV-012-COV-05 (`DeptCoverage_OnlyApprovedLeaveCounts`) | Pending request only | No rows |
| TC-LV-012-COV-06 (`DeptCoverage_CrossTenant_OtherTenantLeaveExcluded`) | Foreign-tenant dept/employee/leave same day | Only Engineering row; foreign dept absent; headcount unaffected |

- **Pre-fix result:** the former stub returned an empty row set (and a Note), so every
  (dept, day) assertion fails — the rows do not exist. (The stale
  `DepartmentCalendarCoverage_IsDocumentedStub_ReturnsNote` test, which pinned that stub's
  non-empty Note, was replaced — the landed code now returns a `null` Note.)

---

## TC-LV-010-LOCK — Payroll-lock on leave approve/cancel (US-LV-010 AC-4 / US-LV-005 BR-4)

- **Objective:** verify `ApproveAsync` / `CancelAsync` reject with **400** when the leave
  range overlaps an active `AttendancePeriodLock`, and proceed otherwise; scoping is per-tenant.
- **Requirement:** US-LV-010 AC-4, US-LV-005 BR-4; tenant isolation via EF query filter.
- **Preconditions:** approved/pending `LeaveRequest`; `AttendancePeriodLock{IsLocked,PeriodStart..PeriodEnd}`.
- **Category tags:** [x] Happy path · [x] Negative · [x] Boundary · [x] Multi-tenant isolation.

| ID | Scenario | Expected |
|----|----------|----------|
| TC-LV-010-LOCK-01 (`LeaveApprove_PayrollLocked_Rejected_LV010`) | Pending leave inside an active lock → Approve | 400 "payroll-locked"; status stays Pending; no `Used` ledger row |
| TC-LV-010-LOCK-02 (`LeaveCancel_PayrollLocked_Rejected`) | Approved leave inside an active lock → Cancel | 400 "payroll-locked"; status stays Approved; no `Adjusted` reversal |
| TC-LV-010-LOCK-03 (`LeaveApprove_NoLock_Succeeds`) | No lock → Approve | Success; Approved; `Used` ledger appended |
| TC-LV-010-LOCK-04 (`LeaveApprove_LockNotOverlapping_Succeeds`) | Lock ends before leave starts → Approve | Success; Approved |
| TC-LV-010-LOCK-05 (`LeaveApprove_LockInactive_Succeeds`) | Overlapping row but `IsLocked=false` → Approve | Success; Approved |
| TC-LV-010-LOCK-06 (`LeaveCancel_NoLock_Succeeds`) | No lock → Cancel approved | Success; Cancelled; `Adjusted` reversal appended |
| TC-LV-010-LOCK-07 (`PayrollLock_CrossTenant_DoesNotBlock`) | Overlapping lock owned by tenant B → Approve tenant A leave | Success; Approved (foreign lock invisible) |
| TC-LV-010-LOCK-08 (`PayrollLock_CrossTenant_DoesNotBlockCancel`) | Overlapping lock owned by tenant B → Cancel tenant A leave | Success; Cancelled |

- **Pre-fix result:** `IsPayrollLockedAsync` returned false (no lock consulted), so
  approve/cancel proceeded even inside a locked period — TC-01/02 fail against that behaviour.
