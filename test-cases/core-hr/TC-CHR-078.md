---
id: TC-CHR-078
user_story: US-CHR-001
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-078: Date of birth age validation -- must be >= 16 years old

## 1. Test Objective
Verify that the system validates date_of_birth so that the employee's age is at least 16 years old. A date_of_birth resulting in age < 16 should be rejected.

## 2. Related Requirements
- User Story: US-CHR-001
- Data Requirements: date_of_birth validation ("Must be in the past, age >= 16")

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- Department and job title exist in the tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| DOB (too young) | today - 15 years | Age = 15, rejected |
| DOB (boundary, exactly 16) | today - 16 years | Age = 16, accepted |
| DOB (valid adult) | 1990-05-20 | Age = 36, accepted |
| DOB (future date) | today + 1 day | In the future, rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Fill in mandatory fields; set date_of_birth = today - 15 years | Field accepts the typed value. |
| 2 | Submit the form | Validation error: date_of_birth indicates employee must be at least 16 years old. |
| 3 | Change date_of_birth to today - 16 years (exactly 16th birthday) | Field accepts the value. |
| 4 | Submit the form | Employee created successfully. Boundary value is accepted. |
| 5 | Create another employee with date_of_birth = a future date | Validation error: date_of_birth must be in the past. |
| 6 | Create another employee with date_of_birth = 1990-05-20 | Employee created successfully. Standard adult age accepted. |

## 6. Postconditions
- Employees with age >= 16 are created.
- Employees with age < 16 or future DOB are rejected.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
