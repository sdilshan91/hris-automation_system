---
id: TC-CHR-177
user_story: US-CHR-007
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-177: Required field validation -- name and time zone missing triggers error

## 1. Test Objective
Verify that the location creation form enforces required-field validation: submitting without a Location Name or Time Zone displays inline validation errors and prevents the API call. This validates FR-4 and the form requirements in AC-1.

## 2. Related Requirements
- User Story: US-CHR-007
- Acceptance Criteria: AC-1
- Functional Requirements: FR-3, FR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Full access |
| Location Name | (empty) | Required field left blank |
| Time Zone | (not selected) | Required field left blank |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Locations management page and click "Add Location" | Location creation form opens. |
| 2 | Leave the Location Name field empty | No input entered. |
| 3 | Leave the Time Zone dropdown unselected | No time zone selected. |
| 4 | Fill in optional fields (City: "Colombo", Phone: "+94111111111") | Optional fields accept input. |
| 5 | Click "Save" | Form validation triggers. No API request is sent. |
| 6 | Verify inline error on Location Name | An error message appears below the Location Name field (e.g., "Location name is required"). The field is highlighted in red. |
| 7 | Verify inline error on Time Zone | An error message appears below the Time Zone field (e.g., "Time zone is required"). The field is highlighted in red. |
| 8 | Enter "Test Location" in the Location Name field | Inline error for Location Name clears. |
| 9 | Click "Save" again without selecting Time Zone | Only Time Zone error remains. No API request sent. |
| 10 | Select "Asia/Colombo" from the Time Zone dropdown | Inline error for Time Zone clears. |
| 11 | Click "Save" | Form submits successfully. API request is sent and returns 201 Created. |

## 6. Postconditions
- Location "Test Location" was created only after both required fields were provided.
- No invalid/incomplete location records exist in the database.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
