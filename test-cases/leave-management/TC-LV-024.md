---
id: TC-LV-024
user_story: US-LV-001
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-024: Create all accrual frequency types (monthly, quarterly, yearly, upfront)

## 1. Test Objective
Verify that leave types can be created with each of the four supported accrual frequencies and that the selected frequency is correctly stored and displayed.

## 2. Related Requirements
- User Story: US-LV-001
- Acceptance Criteria: AC-1
- Functional Requirements: FR-2

## 3. Preconditions
- Tenant "acme" exists.
- A user with `Leave.Configure` permission is authenticated.

## 4. Test Data
| Leave Type Name | Code | Accrual Frequency | Entitlement |
|-----------------|------|-------------------|-------------|
| Monthly Accrual Test | MAT | monthly | 12.00 |
| Quarterly Accrual Test | QAT | quarterly | 12.00 |
| Yearly Accrual Test | YAT | yearly | 12.00 |
| Upfront Accrual Test | UAT | upfront | 12.00 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create leave type "Monthly Accrual Test" with accrual_frequency = "monthly" | 201 Created. Response shows `accrual_frequency: "monthly"`. |
| 2 | Create leave type "Quarterly Accrual Test" with accrual_frequency = "quarterly" | 201 Created. Response shows `accrual_frequency: "quarterly"`. |
| 3 | Create leave type "Yearly Accrual Test" with accrual_frequency = "yearly" | 201 Created. Response shows `accrual_frequency: "yearly"`. |
| 4 | Create leave type "Upfront Accrual Test" with accrual_frequency = "upfront" | 201 Created. Response shows `accrual_frequency: "upfront"`. |
| 5 | Retrieve all leave types via `GET /api/v1/leave-types` | All four appear with correct accrual frequencies. |
| 6 | Edit "Monthly Accrual Test" and change accrual_frequency to "quarterly" | 200 OK. Updated value stored correctly. |

## 6. Postconditions
- Four leave types with different accrual frequencies exist.
- Accrual frequency is mutable (can be changed on edit).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
