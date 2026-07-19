---
id: TC-ATT-161
user_story: US-ATT-008
module: Attendance
priority: high
type: functional
status: automated
created: 2026-07-19
defect:
  - ISSUE-087
  - DF-33
---

# TC-ATT-161: Chronic-lateness escalation fires exactly once at the monthly threshold crossing and dispatches to the line manager ∪ attendance-admin pool (de-duped, never the late employee) — US-ATT-008 FR-7 (DF-33 / ISSUE-087)

## 1. Test Objective
Verify the DF-33 / ISSUE-087 fix on US-ATT-008 FR-7: when an employee clocks in late and their **distinct late-day count in the current calendar month** crosses `LatePolicy.ChronicThreshold`, the system dispatches an `attendance_chronic_lateness` escalation to **both** the employee's line manager **and** the attendance-admin pool (users holding `Attendance.Edit`), de-duplicated when a recipient is in both sets. The escalation fires **exactly once, at the crossing** (`monthToDate distinct late days == ChronicThreshold + 1` AND the first late punch of that local day), only when `ChronicThreshold > 0`, and **never** notifies the late employee themselves. The notification legs must never throw and never break the committed attendance write. Prior to this fix FR-5/FR-7 were a no-op TODO seam (ISSUE-087).

## 2. Related Requirements
- User Story: US-ATT-008
- Acceptance Criteria: AC-4 (accumulated lates in a month flag the employee and trigger a notification)
- Functional Requirement: FR-7 (configurable chronic-lateness threshold triggers an escalation notification to HR)
- Business Rule: BR-4 (late thresholds are tenant-configurable via `LatePolicy`)
- Finding: ISSUE-087 / DF-33 (chronic-escalation dispatch seam, previously a TODO)

## 3. Preconditions
- A tenant with an active `LatePolicy` whose `ChronicThreshold` is configured (default 5).
- An active employee linked to a user, with a fixed shift that makes the punch under test late (zero-grace all-day shift in the automated arm), and prior distinct late days seeded on other day-numbers of the current month.
- An attendance-admin pool: users holding the `Attendance.Edit` permission, plus a decoy tenant user without it. The employee's `ReportsToEmployeeId` line manager (with a linked user) is resolvable.
- The escalation is dispatched through `IAttendanceNotificationService` (real `RealAttendanceNotificationService` for the recipient/payload arms; an NSubstitute fake for the crossing-trigger arms so the exact fire count is observable).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| `LatePolicy.ChronicThreshold` | 5 (default) | 0 disables the escalation entirely |
| Prior distinct late days (this month) | 4 / 5 / 6 | drives below-crossing / at-crossing / above-crossing |
| `mtdLateDays` at crossing | `ChronicThreshold + 1` = 6 | the exact fire condition |
| Escalation event key | `attendance_chronic_lateness` | `AttendanceAlerts` category, non-mandatory |
| Payload tokens | `attendance.lateCount=6`, `attendance.threshold=5`, `attendance.month="July 2026"`, `employee.firstName` | declared placeholders, must be populated |
| Recipients | line manager ∪ `Attendance.Edit` holders (de-duped) | never the late employee, never a non-holder |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Employee clocks in late with 4 prior distinct late days (4 + today = 5 == threshold, not threshold+1). | No escalation dispatched — below the crossing. `ClockIn_BelowThreshold_DoesNotEscalate`. |
| 2 | Employee clocks in late with 5 prior distinct late days (5 + today = 6 == threshold+1). | Escalation fires **exactly once**, carrying `lateCount=6`, `threshold=5`. `ClockIn_AtCrossing_EscalatesExactlyOnce`. |
| 3 | After the crossing punch, the employee clocks out and clocks in late a **second time the same local day**. | No re-fire — distinct-day count unchanged and this is not the first late log of the day. `ClockIn_SecondLatePunchSameDay_DoesNotReEscalate`. |
| 4 | Employee clocks in late with 6 prior distinct late days (6 + today = 7 > threshold+1). | No escalation — already above the crossing. `ClockIn_AlreadyAboveCrossing_DoesNotEscalate`. |
| 5 | Employee clocks in late as the first late day of the month with `ChronicThreshold == 0` (arithmetic crossing 1 == 0+1 satisfied). | No escalation — the `ChronicThreshold > 0` disable-guard suppresses the fire. `ClockIn_ChronicThresholdZero_DoesNotEscalate`. |
| 6 | Dispatch a chronic-lateness escalation with a seeded manager + two-holder admin pool + a decoy non-holder. | Both legs (in-app + email) go to exactly the two `Attendance.Edit` holders **plus** the line manager; key `attendance_chronic_lateness`; the non-holder and the late employee are NOT recipients; payload populates `attendance.lateCount/threshold/month` + `employee.firstName`. `NotifyChronicLatenessAsync_DispatchesToBothManagerAndAdminPool_BothLegs`. |
| 7 | Dispatch when the line manager is ALSO in the admin pool. | De-dup: exactly three distinct recipients, the manager appears exactly once. `NotifyChronicLatenessAsync_ManagerAlsoInAdminPool_IsNotNotifiedTwice`. |
| 8 | Dispatch for an employee with no manager and no seeded admin pool (empty recipient set). | Does not throw; nothing is dispatched. `NotifyChronicLatenessAsync_NoManagerAndNoAdminPool_DoesNotThrow_AndSendsNothing`. |
| 9 | Dispatch when the underlying dispatcher throws. | Does not throw — the notification leg never breaks the committed attendance write. `NotifyChronicLatenessAsync_DispatcherThrows_DoesNotThrow`. |
| 10 | Resolve the escalation through the log-only seam sibling. | Log-only no-op that never throws. `NotifyChronicLatenessAsync_IsANoOp_ThatDoesNotThrow`. |
| 11 | Inspect the notification catalog for `attendance_chronic_lateness`. | Present via `Get`/`All`, `AttendanceAlerts` category, non-mandatory, non-empty default templates, and every template `{{token}}` is a declared placeholder. `Phase6Event_IsPresent_WithDefaultTemplate_AttendanceAlerts_NotMandatory` / `AllEightPhase6Events_AreListedInTheCatalog` / `Phase6Event_TemplateTokens_AreAllDeclaredPlaceholders`. |

## 6. Postconditions
- Exactly one escalation is dispatched per employee per month, at the threshold crossing, to the manager ∪ admin pool (de-duped); the late employee is never notified; no dispatcher fault can break the attendance write.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test (below/above crossing, disabled-when-zero, no-re-fire same day)
- [x] Boundary test (exact `ChronicThreshold + 1` crossing; first-late-log-of-day guard)
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite), all carrying `[Trait("TC", "TC-ATT-161")]`:**
  - `HRM.Tests/Unit/AttendanceChronicLatenessTriggerTests.cs` — the crossing logic through the real `ClockInAsync`
    path: `ClockIn_BelowThreshold_DoesNotEscalate`, `ClockIn_AtCrossing_EscalatesExactlyOnce`,
    `ClockIn_SecondLatePunchSameDay_DoesNotReEscalate`, `ClockIn_AlreadyAboveCrossing_DoesNotEscalate`,
    `ClockIn_ChronicThresholdZero_DoesNotEscalate`.
  - `HRM.Tests/Unit/RealAttendanceNotificationServiceTests.cs` (chronic arms) — recipient resolution + payload +
    never-throw: `NotifyChronicLatenessAsync_DispatchesToBothManagerAndAdminPool_BothLegs`,
    `NotifyChronicLatenessAsync_ManagerAlsoInAdminPool_IsNotNotifiedTwice`,
    `NotifyChronicLatenessAsync_NoManagerAndNoAdminPool_DoesNotThrow_AndSendsNothing`,
    `NotifyChronicLatenessAsync_DispatcherThrows_DoesNotThrow`.
  - `HRM.Tests/Unit/LogOnlyAttendanceNotificationServiceTests.cs` — `NotifyChronicLatenessAsync_IsANoOp_ThatDoesNotThrow`.
  - `HRM.Tests/Unit/NotificationEventCatalogPhase6Tests.cs` — catalog integrity for `attendance_chronic_lateness`:
    `Phase6Event_IsPresent_WithDefaultTemplate_AttendanceAlerts_NotMandatory`,
    `AllEightPhase6Events_AreListedInTheCatalog`, `Phase6Event_TemplateTokens_AreAllDeclaredPlaceholders` (these are
    multi-event `[Theory]`/`[Fact]` methods; the `attendance_chronic_lateness` `[InlineData]` / array entry is the
    row this TC binds to).
- These arms pre-existed and are already green; this backfill only adds the `[Trait("TC", "TC-ATT-161")]`
  binding — no test was renamed, weakened, or restructured.
