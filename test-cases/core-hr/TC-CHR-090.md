---
id: TC-CHR-090
user_story: US-CHR-001
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-090: Invalid employment_type value rejected

## 1. Test Objective
Verify that only the defined employment types (Full-Time, Part-Time, Contract, Intern) are accepted during employee creation. Any other value should be rejected.

## 2. Related Requirements
- User Story: US-CHR-001
- Data Requirements: employment_type "Full-Time, Part-Time, Contract, Intern"

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- Department and job title exist in the tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Valid types | Full-Time, Part-Time, Contract, Intern | All accepted |
| Invalid type 1 | Freelance | Not in the enum |
| Invalid type 2 | Temporary | Not in the enum |
| Invalid type 3 | (empty string) | Missing value |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Submit with employment_type = "Full-Time" | Accepted. Employee created. |
| 2 | Submit with employment_type = "Part-Time" | Accepted. Employee created. |
| 3 | Submit with employment_type = "Contract" | Accepted. Employee created. |
| 4 | Submit with employment_type = "Intern" | Accepted. Employee created. |
| 5 | Send API request with employment_type = "Freelance" | 400/422 error: invalid employment type. |
| 6 | Send API request with employment_type = "" (empty) | 400/422 error: employment type is required. |
| 7 | Verify the UI dropdown only offers the 4 valid options | No free-text input for employment type; only the defined values are selectable. |

## 6. Postconditions
- Only valid employment types are persisted in the database.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
