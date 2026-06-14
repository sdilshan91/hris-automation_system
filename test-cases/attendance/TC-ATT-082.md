---
id: TC-ATT-082
user_story: US-ATT-006
module: Attendance
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-ATT-082: Overtime endpoints require authentication and the correct permission; employees cannot approve, only act on their own data (security)

## 1. Test Objective
Verify the authn/authz contract for the overtime endpoints: all require a valid authenticated session and tenant context; the approve/reject/pending-queue/report endpoints require the appropriate manager/HR permission (enforced server-side, not just UI-hidden); an employee can submit pre-approval and read their own overtime but cannot approve/reject or read another employee's records; input is sanitised.

## 2. Related Requirements
- User Story: US-ATT-006
- Preconditions: Section 2 (authenticated, `Attendance.Clock.Self`; manager/HR permissions for approval/report)
- Functional Requirements: FR-4 (pre-approval self-service), FR-5 (manager approval), FR-6 (approve/reject), AC-5 (HR report)

## 3. Preconditions
- Tenant "acme". Employee "Asha" (`Attendance.Clock.Self`), Manager "Ben" (overtime-approve), HR "Priya" (overtime-report). A PENDING overtime_record exists for Asha.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Endpoints | pre-approval, my, pending, approve, reject, report | full surface |
| Roles | Employee, Manager, HR | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call each overtime endpoint with NO token | 401 Unauthorized on all. |
| 2 | Call with a valid token but no resolved tenant context | Rejected (tenant context required) -- consistent with TC-ATT-ISO-002. |
| 3 | As Asha (Employee), `POST /overtime/{id}/approve` or `/reject` | 403 Forbidden -- employees cannot approve/reject (server-side authz, not just a hidden button). |
| 4 | As Asha, `GET /overtime/my` | 200, returns only Asha's own overtime; `POST /overtime/pre-approval` succeeds for herself. |
| 5 | As Asha, attempt to read another employee's overtime (id or body-injected employeeId) | Denied/404 -- self-scope enforced; body-injected employee_id ignored. |
| 6 | As Asha, `GET /overtime/report` | 403 -- the monthly report is HR-permissioned. |
| 7 | As Ben (Manager) | Approve/reject and pending queue succeed for direct reports; cannot approve out-of-team (per TC-ATT-073/077). |
| 8 | Input sanitisation -- XSS/SQL payload in reason/comment/pre-approval reason | Stored/escaped safely; no script execution, no injection; round-trips as inert text. |

## 6. Postconditions
- Every overtime endpoint enforces authentication, tenant context, and role-appropriate authorization server-side; self-scope and input sanitisation hold.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- The concrete permission names should be confirmed against `PermissionCatalog`. Per the attendance vault, prior ATT stories added concrete permission strings (`Attendance.Regularize.Self`, `Attendance.Shift.Manage`) rather than wildcards when the story named one not in the catalog; the overtime approve/report permissions are expected to follow the same pattern. This TC asserts the authz behaviour, not a specific permission string. **Reported to caller.**
