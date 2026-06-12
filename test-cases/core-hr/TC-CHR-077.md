---
id: TC-CHR-077
user_story: US-CHR-001
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-077: Date of joining not more than 90 days in the future (BR-4)

## 1. Test Objective
Verify that the system rejects a date_of_joining that is more than 90 days in the future, per BR-4. A date exactly 90 days in the future should be accepted; a date 91 days in the future should be rejected.

## 2. Related Requirements
- User Story: US-CHR-001
- Business Rules: BR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- Department and job title exist in the tenant.
- Current date is known for calculating the 90-day boundary.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Valid future date | today + 90 days | Exactly at boundary (accepted) |
| Invalid future date | today + 91 days | Exceeds boundary (rejected) |
| Past date | 2026-01-15 | In the past (accepted) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Fill in all mandatory fields with date_of_joining = today + 91 days | Fields populated. |
| 2 | Submit the form | Validation error is displayed. |
| 3 | Verify error message indicates date_of_joining cannot be more than 90 days in the future | Clear error message displayed inline below the field. |
| 4 | Change date_of_joining to today + 90 days (exactly at boundary) | Field accepts the value. |
| 5 | Submit the form | Employee created successfully (201 Created). Boundary value is accepted. |
| 6 | Create another employee with date_of_joining in the past (e.g., 2026-01-15) | Employee created successfully. Past dates are accepted. |
| 7 | Create another employee with date_of_joining = today | Employee created successfully. Current date is accepted. |

## 6. Postconditions
- Employees with date_of_joining within the 90-day window are created.
- Employees with date_of_joining beyond 90 days are rejected.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
