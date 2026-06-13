---
id: TC-LV-007
user_story: US-LV-001
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-007: Invalid color, gender, and accrual frequency values rejected

## 1. Test Objective
Verify that the system rejects leave type creation or update with invalid field values: non-hex color codes, invalid gender options, and invalid accrual frequency values.

## 2. Related Requirements
- User Story: US-LV-001
- Acceptance Criteria: AC-1
- Functional Requirements: FR-2

## 3. Preconditions
- Tenant "acme" exists.
- A user with `Leave.Configure` permission is authenticated.

## 4. Test Data
| Field | Invalid Value | Expected Error | Notes |
|-------|---------------|----------------|-------|
| Color | "red" | Color must be a valid hex code (#RRGGBB) | Not a hex code |
| Color | "#GGG000" | Color must be a valid hex code (#RRGGBB) | Invalid hex chars |
| Color | "#FFF" | Color must be a valid hex code (#RRGGBB) | Too short (3-char shorthand) |
| Gender | "other" | Gender must be one of: all, male, female | Invalid option |
| Accrual Frequency | "weekly" | Accrual frequency must be one of: monthly, quarterly, yearly, upfront | Invalid option |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/leave-types` with `{ color: "red", ...valid fields }` | API returns 400 Bad Request. Validation error: "Color must be a valid hex code (#RRGGBB)." |
| 2 | Send `POST /api/v1/leave-types` with `{ color: "#GGG000", ...valid fields }` | API returns 400 Bad Request. Same validation error. |
| 3 | Send `POST /api/v1/leave-types` with `{ gender: "other", ...valid fields }` | API returns 400 Bad Request. Validation error: "Gender must be one of: all, male, female." |
| 4 | Send `POST /api/v1/leave-types` with `{ accrual_frequency: "weekly", ...valid fields }` | API returns 400 Bad Request. Validation error: "Accrual frequency must be one of: monthly, quarterly, yearly, upfront." |
| 5 | In the UI, verify that Color uses a color picker (restricting to valid hex), Gender is a dropdown with only "All", "Male", "Female", and Accrual Frequency is a dropdown with only valid options | UI controls prevent invalid selections. |

## 6. Postconditions
- No leave type records were created with invalid field values.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
