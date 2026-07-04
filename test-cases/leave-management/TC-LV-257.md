---
id: TC-LV-257
user_story: US-LV-002
module: Leave Management
priority: high
type: functional
status: automated
created: 2026-07-04
---

# TC-LV-257: Entitlement rule & override mutations write before/after audit rows

## 1. Test Objective
Verify that every mutating operation on the leave-entitlement configuration (rule create/update/delete, bulk rule create, override upsert create/update, override delete) persists a queryable, tenant-scoped `audit_logs` row keyed on the mutated entity, attributed to the acting user, with before/after JSON snapshots — not merely a Serilog line (AC-5, NFR-1 audit trail).

## 2. Related Requirements
- User Story: US-LV-002
- Acceptance Criteria: AC-5
- Functional Requirements: FR-5
- Related Finding: **BUG-028** (US-LV-002) — `LeaveEntitlementService` rule create/update/delete, bulk-create, and override upsert/delete persisted no `audit_logs` row (only an `ILogger` line), so entitlement changes had no queryable audit trail. Fix adds the `LeaveTypeService`-style `AuditLogs.Add` writes (`LeaveEntitlementRule.Created/.Updated/.Deleted`, `LeaveEntitlementOverride.Upserted/.Deleted`).

## Automated Coverage
- Runner: xUnit (`HRM.Tests`), EF Core InMemory. Traceability tag: `@TC-LV-257`.
- Bound tests: `HRM.Tests.Unit.LeaveEntitlementAuditRegressionTests` —
  `CreateRule_WritesAuditRow_BUG028`, `UpdateRule_WritesAuditRow_BeforeDiffersAfter_BUG028`,
  `DeleteRule_WritesAuditRow_BUG028`, `BulkCreateRules_WritesAuditRowPerRule_BUG028`,
  `UpsertOverride_Create_WritesAuditRow_BUG028`,
  `UpsertOverride_Update_WritesAuditRow_BeforeDiffersAfter_BUG028`,
  `DeleteOverride_WritesAuditRow_BUG028`.
  Each drives the real `LeaveEntitlementService` against a real (InMemory) `AppDbContext` and asserts a persisted `audit_logs` row keyed on the entity id with the correct action substring (`Rule` / `Override`), `TenantId` == the acting tenant, and `UserId` == the authenticated actor; the update tests additionally assert the **before** and **after** snapshots are present, non-empty, and differ (entitlement 20→25 for rules, 30→35 for overrides).
- Regression for BUG-028: FAILS pre-fix (no `AuditLogs.Add` in `LeaveEntitlementService` at HEAD → the "row exists" assertions fail), PASSES post-fix.

## 3. Preconditions
- Tenant "acme" is active with a "Leave.Configure"-permissioned user authenticated.
- An active leave type "Annual Leave", a department "Engineering", and an active employee exist in the tenant.

## 4. Test Data
| Field | Before | After |
|-------|--------|-------|
| Rule Entitlement Days | 20.00 | 25.00 |
| Override Entitlement Days | 30.00 | 35.00 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create an entitlement rule (20 days) | 200/OK; one `audit_logs` row exists with action containing `Rule`+`Created`, `ResourceId` = rule id, `TenantId` = acme, `UserId` = actor, non-null after-snapshot, null before-snapshot. |
| 2 | Update the rule (20 → 25 days) | 200/OK; an `audit_logs` row with action containing `Updated` exists whose before/after snapshots are both present and differ. |
| 3 | Delete (soft) the rule | 200/OK; an `audit_logs` row with action containing `Deleted` exists with a non-null before-snapshot. |
| 4 | Bulk-create two rules | 200/OK; each created rule id has its own create `audit_logs` row (tenant-scoped, actor-attributed). |
| 5 | Upsert a new override (30 days) | 200/OK; one `audit_logs` row with action containing `Override`, `ResourceId` = override id, non-null after-snapshot. |
| 6 | Upsert the same override again (30 → 35 days) | 200/OK; the override's update `audit_logs` row (the one carrying a before-snapshot) has before ≠ after. |
| 7 | Delete (soft) the override | 200/OK; an `audit_logs` row with action containing `Override`+`Deleted` exists with a non-null before-snapshot. |

## 6. Postconditions
- Every entitlement rule/override mutation has a corresponding tenant-scoped, actor-attributed `audit_logs` row.
- Update rows carry differing before/after JSON snapshots.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
