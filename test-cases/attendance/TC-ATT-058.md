---
id: TC-ATT-058
user_story: US-ATT-005
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-058: Default-shift fallback -- an employee with no explicit assignment resolves to the tenant default shift (FR-5, BR-1)

## 1. Test Objective
Verify the default-shift mechanism (FR-5, BR-1): every tenant has at least one shift flagged `is_default = true`; an employee with no explicit `employee_shift` record (for the queried date) resolves to that tenant default shift. Once an explicit assignment exists for the date, the explicit shift takes precedence over the default.

## 2. Related Requirements
- User Story: US-ATT-005
- Functional Requirements: FR-5 (tenant default shift for unassigned employees)
- Business Rules: BR-1 (every tenant has >= 1 default shift, created at provisioning)
- Data: `shift.is_default`

## 3. Preconditions
- Tenant "acme" `active`, Attendance module enabled, with a shift flagged is_default = true (the provisioning default).
- HR Officer authenticated with `Attendance.Shift.Manage`.
- Employee E9 has NO explicit employee_shift assignment.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant default | "Standard 9-5" (is_default true) | Provisioned default |
| E9 | no explicit assignment | Should inherit default |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `GET /api/v1/attendance/employees/E9/shift?date=today` | Returns the tenant default shift "Standard 9-5" (fallback applied; no error for the missing explicit assignment). |
| 2 | Confirm the default invariant (BR-1) | acme has exactly one shift with is_default = true; the resolver uses it for any unassigned employee. |
| 3 | Assign E9 explicitly to a different shift "Day Shift" effective today | An employee_shift row is created for E9 -> Day Shift. |
| 4 | `GET .../employees/E9/shift?date=today` again | Now returns "Day Shift" -- the explicit assignment overrides the default. |
| 5 | `GET .../employees/E9/shift?date=(before the explicit effective_from)` | Returns the tenant default (the explicit assignment is not yet effective on that earlier date). |
| 6 | Boundary -- attempt to clear/unset the only default | Rejected: a tenant must retain at least one default (BR-1); setting a new default first transfers the flag (exactly one is_default per tenant). |

## 6. Postconditions
- Unassigned employees resolve to the tenant default; explicit assignments override it from their effective date; the tenant always retains exactly one default.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- BR-1 states the default shift is created during tenant provisioning (Tenant Admin module). If provisioning auto-seeding of the default is not yet wired, Step 2 is verified against a manually-flagged default and the provisioning-seed call site is **reported to caller** as a cross-module (Tenant Admin) dependency -- consistent with how leave-management seeded system types were handled.
