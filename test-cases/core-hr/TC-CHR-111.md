---
id: TC-CHR-111
user_story: US-CHR-002
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-111: Employee cannot edit HR-only fields via API (field-level authorization)

## 1. Test Objective
Verify that field-level authorization is enforced at the API level: an Employee attempting to PATCH personal info fields restricted to HR (name, DOB, gender) receives a 403 Forbidden response. This validates AC-5, FR-3, and the data requirements field access table.

## 2. Related Requirements
- User Story: US-CHR-002
- Acceptance Criteria: AC-5
- Functional Requirements: FR-3
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- User "John Smith" is authenticated with Employee role in "acme".
- Employee record for John Smith exists with first_name "John", last_name "Smith", date_of_birth "1990-05-15".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Employee | Limited write |
| Employee ID | {john_smith_id} | Own profile |
| Attempted first_name | Jonathan | HR-only field |
| Attempted date_of_birth | 1991-01-01 | HR-only field |
| Attempted gender | Other | HR-only field |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `PATCH /api/v1/tenant/employees/{john_smith_id}` with body `{ "first_name": "Jonathan", "xmin": "{current_xmin}" }` | Response is 403 Forbidden. Error message indicates `first_name` is not editable by Employee role. |
| 2 | Send `PATCH /api/v1/tenant/employees/{john_smith_id}` with body `{ "date_of_birth": "1991-01-01", "xmin": "{current_xmin}" }` | Response is 403 Forbidden. |
| 3 | Send `PATCH /api/v1/tenant/employees/{john_smith_id}` with body `{ "gender": "Other", "xmin": "{current_xmin}" }` | Response is 403 Forbidden. |
| 4 | Send `PATCH /api/v1/tenant/employees/{john_smith_id}` with body `{ "phone": "555-9999", "xmin": "{current_xmin}" }` | Response is 200 OK. Phone is updated -- this field IS permitted for Employee role. |
| 5 | Verify database state | first_name is still "John", date_of_birth is "1990-05-15", gender unchanged. Phone is "555-9999". |

## 6. Postconditions
- HR-only fields remain unchanged.
- Employee-permitted field (phone) was updated.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
