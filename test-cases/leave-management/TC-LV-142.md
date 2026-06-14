---
id: TC-LV-142
user_story: US-LV-007
module: Leave Management
priority: medium
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-142: Holiday type semantics -- public applies to all; restricted may require apply; optional may count against optional-holiday leave type (BR-2, BR-3)

## 1. Test Objective
Verify the three holiday types are stored and surfaced with their distinct semantics: Public applies to all employees in the tenant/location and is auto-excluded from leave counts; Restricted is informational and may require an employee to apply; Optional may count against a separate optional-holiday leave type if configured (BR-2, BR-3).

## 2. Related Requirements
- User Story: US-LV-007
- Business Rules: BR-2, BR-3
- Functional Requirements: FR-2

## 3. Preconditions
- Tenant "acme" active; HR Officer "Priya" authenticated with `Holiday.Create` / `Holiday.View`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Public | "Independence Day", Public | applies to all, auto-excluded |
| Restricted | "Festival X", Restricted | may require apply |
| Optional | "Floating Holiday", Optional | optional-holiday leave type (if configured) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create one holiday of each type | All persisted with the correct `HolidayType` (stored as string: Public/Restricted/Optional). |
| 2 | Verify Public semantics | Public holiday is auto-excluded from leave day counts (cross-ref TC-LV-131/132) and applies to all employees in scope. |
| 3 | Verify Restricted semantics | Restricted holiday is shown on the calendar but is NOT auto-excluded (employee may need to apply); cross-ref TC-LV-132. |
| 4 | Verify Optional semantics | Optional holiday is NOT auto-excluded; if an optional-holiday leave type is configured (US-LV-001), it may be applied against that type. Mark the leave-type linkage CONDITIONAL on tenant configuration. |

## 6. Postconditions
- The three types are persisted and behave per their BR-2/BR-3 semantics.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
