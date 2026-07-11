---
id: TC-LV-258
user_story: US-LV-007
module: Leave Management
priority: high
type: functional
status: automated
created: 2026-07-04
---

# TC-LV-258: Holiday create/update/deactivate and CSV import write audit rows

## 1. Test Objective
Verify that every mutating operation on the holiday calendar (create, update, deactivate) persists a queryable, tenant-scoped `audit_logs` row keyed on the holiday, attributed to the acting user, with before/after JSON snapshots; and that a CSV import writes a single `Holiday.Imported` summary audit row — not merely a Serilog line (FR-1, BR-4 audit trail).

## 2. Related Requirements
- User Story: US-LV-007
- Functional Requirements: FR-1, FR-4
- Business Rules: BR-4
- Related Finding: **BUG-031** (US-LV-007) — `HolidayService` create/update/deactivate and CSV import persisted no `audit_logs` row (only an `ILogger` line), so holiday-calendar changes had no queryable audit trail. Fix adds the `LeaveTypeService`-style `AuditLogs.Add` writes (`Holiday.Created/.Updated/.Deactivated`, plus a `Holiday.Imported` summary row).

## Automated Coverage
- Runner: xUnit (`HRM.Tests`), EF Core InMemory. Traceability tag: `@TC-LV-258`.
- Bound tests: `HRM.Tests.Unit.HolidayAuditRegressionTests` —
  `CreateHoliday_WritesAuditRow_BUG031`, `UpdateHoliday_WritesAuditRow_BeforeDiffersAfter_BUG031`,
  `DeactivateHoliday_WritesAuditRow_BUG031`, `ImportHolidays_WritesSummaryAuditRow_BUG031`.
  Each drives the real `HolidayService` against a real (InMemory) `AppDbContext`. The create/update/deactivate tests assert a persisted `audit_logs` row keyed on the holiday id with the correct action substring (`Holiday`), `TenantId` == the acting tenant, and `UserId` == the authenticated actor; the update test additionally asserts the **before** and **after** snapshots are present, non-empty, and differ (name/date/type change). The import test asserts exactly one summary row whose action contains `Import`, tenant-scoped and actor-attributed.
- Regression for BUG-031: FAILS pre-fix (no `AuditLogs.Add` in `HolidayService` at HEAD → the "row exists" assertions fail), PASSES post-fix.

## 3. Preconditions
- Tenant "acme" is active with a `Holiday.Edit` / `Holiday.Deactivate`-permissioned user authenticated.

## 4. Test Data
| Field | Before | After |
|-------|--------|-------|
| Holiday name | "Old Name" | "New Name" |
| Holiday date | 2026-01-01 | 2026-01-02 |
| Holiday type | Public | Restricted |
| Import CSV | 3 valid rows | 3 created + 1 summary row |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create a holiday | 200/OK; one `audit_logs` row exists with action containing `Holiday`+`Created`, `ResourceId` = holiday id, `TenantId` = acme, `UserId` = actor, non-null after-snapshot. |
| 2 | Update the holiday (name/date/type) | 200/OK; an `audit_logs` row with action containing `Updated` exists whose before/after snapshots are both present and differ. |
| 3 | Deactivate the holiday | 200/OK; an `audit_logs` row with action containing `Holiday`+`Deactivated` exists (tenant-scoped, actor-attributed). |
| 4 | Import a 3-row valid CSV | 200/OK; exactly one summary `audit_logs` row with action containing `Import` exists, tenant-scoped and actor-attributed. |

## 6. Postconditions
- Every holiday mutation has a corresponding tenant-scoped, actor-attributed `audit_logs` row.
- The update row carries differing before/after JSON snapshots; the CSV import leaves one summary row.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
