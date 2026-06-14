---
id: TC-ATT-033
user_story: US-ATT-003
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-033: Regularization submission is recorded in the audit log (NFR-3)

## 1. Test Objective
Verify NFR-3: submitting a regularization writes an audit entry capturing the action (regularization submitted), the actor (`created_by` = the submitting employee/user), the tenant, the target `regularization_id` and date, and a timestamp. The `attendance_regularization` row's audit fields (`created_at`, `created_by`, `updated_at`, `updated_by`) are populated by the AuditInterceptor. (Approve/reject audit entries are covered under US-ATT-004; this TC covers the submit action.)

## 2. Related Requirements
- User Story: US-ATT-003
- Non-Functional Requirements: NFR-3
- Data Requirements: S7 (created_at/created_by/updated_at/updated_by audit fields)

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, regularization workflow configured, lookback = 7 days.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Regularize.Self`.
- Audit logging is enabled for the tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Action | regularization submitted | Audited event |
| Actor | Jordan Lee's user/employee id | created_by |
| date | today - 3 days | Target date |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Jordan Lee, submit a valid regularization (per TC-ATT-025) | Response 201 Created; `regularization_id` returned. |
| 2 | Query the audit log for the new regularization | An audit entry exists for the submit action, scoped to tenant acme, with the actor = Jordan Lee, the target `regularization_id`, the regularized date, and a server timestamp. |
| 3 | Verify the row's audit fields | The `attendance_regularization` row has `created_at` (UTC), `created_by` = Jordan Lee, `updated_at`/`updated_by` set on insert, stamped by the AuditInterceptor. |
| 4 | Verify the audit entry is tenant-scoped | The audit entry carries acme's `tenant_id`; it is not visible from another tenant's audit view (consistent with TC-ATT-ISO-006). |
| 5 | Verify no sensitive over-capture | The audit entry records the action/metadata (who/what/when) without leaking the full reason text beyond what the audit policy specifies; confirm against the implemented audit schema. |

## 6. Postconditions
- A tenant-scoped audit entry for the submit action exists; the regularization row's audit fields are populated.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
