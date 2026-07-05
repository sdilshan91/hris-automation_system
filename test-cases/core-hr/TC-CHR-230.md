---
id: TC-CHR-230
user_story: US-CHR-009
module: Core HR
priority: high
type: functional
status: fail
created: 2026-06-12
---

# TC-CHR-230: Audit log records before/after snapshot for status change (NFR-5)

## 1. Test Objective
Verify that every status change operation is fully audited with before and after snapshots, including the previous status, new status, reason, effective date, and the identity of the actor who performed the change. This validates NFR-5.

## 2. Related Requirements
- User Story: US-CHR-009
- Non-Functional Requirements: NFR-5
- Functional Requirements: FR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user (`hr-officer-uuid`, name "Sarah HR") is authenticated in the "acme" tenant context.
- Employee "Dave Brown" (`emp-006-uuid`) exists with status `active`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee | Dave Brown (emp-006-uuid) | Status: active |
| New Status | suspended | Valid transition |
| Reason | Compliance review | Required |
| Effective Date | 2026-06-12 | Today |
| Actor | Sarah HR (hr-officer-uuid) | HR Officer |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/tenant/employees/emp-006-uuid/status` with the status change data using HR Officer credentials. | Response 200 OK. Status changed. |
| 2 | Query the audit log (either `employee_field_audit_logs` or general audit log table). | An audit entry exists with: `employee_id` = emp-006-uuid, `before_snapshot` containing `{ "status": "active" }` (or equivalent JSONB), `after_snapshot` containing `{ "status": "suspended" }`, `section` or `change_type` = "status_change", `changed_by` / `actor` = hr-officer-uuid, `timestamp` ~ now(), `tenant_id` = acme tenant UUID. |
| 3 | Verify the before_snapshot contains the previous status value. | `before_snapshot.status` = "active". |
| 4 | Verify the after_snapshot contains the new status value and reason. | `after_snapshot.status` = "suspended". The reason and effective date are either in the snapshot or in a parallel field. |
| 5 | Verify the actor identity is recorded. | `changed_by` = hr-officer-uuid. |
| 6 | Perform a second status change (suspended -> active). Query the audit log again. | A second audit entry exists with `before_snapshot.status` = "suspended" and `after_snapshot.status` = "active". Both entries exist independently. |

## 6. Postconditions
- Two audit log entries exist with correct before/after snapshots.
- Actor identity is recorded in both entries.

## 6a. Automated Regression Coverage
- **Finding:** ISSUE-025 — a status change wrote only the `employee_field_audit_logs` row; nothing reached the central `audit_logs` table, so the change was not queryable via the standard audit-log API (step 2 "general audit log table" gap).
- **Bound test:** `HRM.Tests.Unit.EmployeeStatusServiceTests.StatusChange_WritesCentralAuditRow_ISSUE025` (`src/backend/HRM.Tests/Unit/EmployeeStatusServiceTests.cs`).
- **Covers:** step 2 (central-audit-log branch) — asserts a single central `audit_logs` row (`Employee.StatusChanged`, `ResourceId` == employee id, `Before` = Active / `After` = Suspended, actor stamped) is written alongside the still-present `employee_field_audit_log` row. Fails pre-fix (central row absent), passes post-fix.
- **Scope note:** unit test covers the central-row acceptance only; the second-transition timeline check (step 6) and the E2E API path (step 1) still require `@test-runner` re-verification before the TC status flips.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
