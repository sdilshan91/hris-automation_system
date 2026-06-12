---
id: TC-CHR-103
user_story: US-CHR-001
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-103: Gender field accepts defined values including Prefer Not To Say

## 1. Test Objective
Verify that the gender field accepts all defined enum values (Male, Female, Non-Binary, Prefer Not To Say) and that it is optional. The field should be a dropdown with only the defined values available.

## 2. Related Requirements
- User Story: US-CHR-001
- Data Requirements: gender varchar(20) "Enum: Male, Female, Non-Binary, Prefer Not To Say"

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated.
- Department and job title exist in the tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| gender value 1 | Male | Valid |
| gender value 2 | Female | Valid |
| gender value 3 | Non-Binary | Valid |
| gender value 4 | Prefer Not To Say | Valid |
| gender value 5 | (not selected) | Optional, should be accepted |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Add Employee form and locate the gender dropdown | Dropdown shows options: Male, Female, Non-Binary, Prefer Not To Say. |
| 2 | Create employee with gender = "Male" | Accepted. |
| 3 | Create employee with gender = "Non-Binary" | Accepted. |
| 4 | Create employee with gender = "Prefer Not To Say" | Accepted. |
| 5 | Create employee without selecting any gender | Accepted. Gender is null/empty in the database. |
| 6 | Send API request with gender = "Other" (not in the enum) | 400/422 error: invalid gender value. |

## 6. Postconditions
- All defined gender values are accepted.
- Gender is optional and can be left unset.
- Invalid values are rejected at the API level.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
