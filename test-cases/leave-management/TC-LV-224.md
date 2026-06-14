---
id: TC-LV-224
user_story: US-LV-011
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-224: Authorization — a user without Leave.Manage/HR.Officer permission cannot assign or override LOP (403)

## 1. Test Objective
Verify role-based access control on the LOP management surface: assign-lop, compulsory-leave bulk-assign, and override/remove actions require `Leave.Manage`/`HR.Officer` permission. A regular employee or other unauthorized role attempting these is denied with 403 and no state change.

## 2. Related Requirements
- User Story: US-LV-011
- Preconditions §2 (permission required)
- Cross-ref: US-AUTH-* (RBAC), PermissionCatalog

## 3. Preconditions
- Tenant "acme"; LOP type exists; employee "Mark Otieno" (regular employee, no LOP permission); employee "Ben".
- HR Officer "Asha" (has permission) available as the positive control.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Unauthorized actor | Mark (Employee) | no Leave.Manage |
| Target | Ben | another employee |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Mark, call `POST /api/v1/leaves/assign-lop` for Ben | 403 Forbidden; no LOP request/ledger entry created. |
| 2 | As Mark, call the compulsory-leave bulk-assign endpoint | 403 Forbidden; no compulsory_leave/LOP rows created. |
| 3 | As Mark, attempt to override/remove an existing LOP entry | 403 Forbidden; the entry is unchanged. |
| 4 | As Asha (positive control), perform assign-lop for Ben | Succeeds — confirms the deny is permission-based, not a broken endpoint. |

## 6. Postconditions
- LOP management actions are gated by Leave.Manage/HR.Officer; unauthorized users are blocked with no side effects.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
