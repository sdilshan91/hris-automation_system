---
id: TC-ATT-052
user_story: US-ATT-005
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-052: Duplicate shift name within the same tenant is rejected; the same name is allowed in a different tenant (negative + tenant-scoped uniqueness)

## 1. Test Objective
Verify the per-tenant name-uniqueness constraint on `shift` (AC-1, FR-2, Data: name unique per tenant): creating a second shift with a name that already exists in the tenant is rejected, while the identical name remains valid in a different tenant (uniqueness is scoped by tenant_id, not global).

## 2. Related Requirements
- User Story: US-ATT-005
- Acceptance Criteria: AC-1
- Functional Requirements: FR-2 (name parameter)
- Data: `shift.name` unique per tenant

## 3. Preconditions
- Tenants "acme" and "globex" `active`, Attendance module enabled.
- HR Officers authenticated in each with `Attendance.Shift.Manage`.
- A shift "Day Shift" already exists in acme (e.g. created by TC-ATT-051).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Existing name (acme) | "Day Shift" | Already taken |
| Duplicate attempt (acme) | "Day Shift" | Should be rejected |
| Same name in globex | "Day Shift" | Should be allowed |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme HR, `POST /api/v1/attendance/shifts` with name "Day Shift" again | Response 409/400 with a duplicate-name validation error; no second acme row is created. |
| 2 | Verify the acme `shift` table | Exactly one shift named "Day Shift" exists for acme. |
| 3 | Confirm case/whitespace handling | A name differing only by trailing whitespace or case (e.g. "day shift ") is treated per the documented normalization rule; assert the implemented behavior is consistent (no accidental duplicate). |
| 4 | As globex HR, `POST /api/v1/attendance/shifts` with name "Day Shift" | Response 201; globex now has its own "Day Shift" -- uniqueness is per tenant. |

## 6. Postconditions
- acme has exactly one "Day Shift"; globex has its own independent "Day Shift"; no cross-tenant collision.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
