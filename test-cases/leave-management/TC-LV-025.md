---
id: TC-LV-025
user_story: US-LV-001
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-025: Negative balance configuration -- allowed with limit and disallowed

## 1. Test Objective
Verify that the negative balance feature can be configured (enabled with a limit, or disabled), and that the configuration is correctly stored and enforced.

## 2. Related Requirements
- User Story: US-LV-001
- Functional Requirements: FR-2
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" exists.
- A user with `Leave.Configure` permission is authenticated.

## 4. Test Data
| Scenario | Negative Balance Allowed | Limit | Expected |
|----------|------------------------|-------|----------|
| Disabled | false | N/A | negative_balance_limit should be null or 0 |
| Enabled with limit | true | 5.00 | negative_balance_limit = 5.00 |
| Enabled with zero limit | true | 0.00 | Should be rejected (if allowed=true, limit must be > 0) or accepted depending on design |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create leave type with negative_balance_allowed = false | 201 Created. `negative_balance_allowed: false`, `negative_balance_limit: null` or 0. |
| 2 | Create leave type with negative_balance_allowed = true, negative_balance_limit = 5.00 | 201 Created. Both fields stored correctly. |
| 3 | Edit the first type: toggle negative_balance_allowed to true, set limit to 3.00 | 200 OK. Updated values stored. |
| 4 | Verify the negative_balance_limit field is hidden/disabled when negative_balance_allowed is false in the UI | Toggling off hides or disables the limit input. |
| 5 | Verify negative_balance_limit cannot be negative | API rejects `{ negative_balance_limit: -1 }` with validation error. |

## 6. Postconditions
- Negative balance configuration is correctly stored for each leave type.
- Conditional field visibility works in the UI.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
