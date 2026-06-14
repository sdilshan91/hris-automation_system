---
id: TC-LV-ISO-033
user_story: US-LV-009
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-033: Calendar data from Tenant A must not appear in Tenant B (cross-tenant data visibility) (NFR-2, Test Hint)

## 1. Test Objective
Verify the Team Leave Calendar is tenant-isolated: a manager/HR user in Tenant B never sees any leave entry belonging to Tenant A's employees, even for identical names, dates, or leave types.

## 2. Related Requirements
- User Story: US-LV-009
- Non-Functional Requirements: NFR-2
- Test Hint: "Calendar data from Tenant A must not appear in Tenant B."

## 3. Preconditions
- Tenant "acme" (manager Maya, report Sam: Annual Approved 2026-06-08..10).
- Tenant "globex" (manager Dana, report Kofi: Annual Approved 2026-06-08..10 -- same dates/type).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme leave | Sam, 2026-06-08..10 | must be invisible to globex |
| globex leave | Kofi, 2026-06-08..10 | must be invisible to acme |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Dana (globex) loads the team-calendar for June | Only globex employees (Kofi) appear; Sam (acme) does not appear despite identical dates/type. |
| 2 | Maya (acme) loads the team-calendar for June | Only acme employees (Sam) appear; Kofi (globex) is absent. |
| 3 | Dana filters by `?employeeId={Sam's acme id}` | Returns empty; the cross-tenant employee id resolves to nothing under globex's tenant filter. |
| 4 | HR with Leave.ViewAll in each tenant loads org-wide | Each HR sees only their own tenant's whole org; never the other tenant's calendar. |

## 6. Postconditions
- Calendar data is strictly tenant-isolated; no cross-tenant visibility via default load, filter, or HR all-access.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
