---
id: TC-CHR-115
user_story: US-CHR-002
module: Core HR
priority: high
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-115: Employee cannot view another employee's profile -- access denied

## 1. Test Objective
Verify that an Employee can only view their own profile and cannot access another employee's profile. Attempting to fetch another employee's profile returns 403 or 404. This validates BR-1 and FR-3.

## 2. Related Requirements
- User Story: US-CHR-002
- Functional Requirements: FR-3
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- User "John Smith" is authenticated with Employee role in "acme".
- Employee "Jane Doe" also exists in the "acme" tenant.
- John Smith is NOT Jane Doe's manager.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Employee | Limited access |
| Authenticated Employee | John Smith ({john_id}) | Own profile only |
| Target Employee | Jane Doe ({jane_id}) | Another employee |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as John Smith (Employee role) in "acme" tenant | JWT contains employee role and user_id. |
| 2 | Send `GET /api/v1/tenant/employees/{jane_id}` | Response is 403 Forbidden (or 404 to avoid leaking existence). Employee role can only access own profile. |
| 3 | Send `GET /api/v1/tenant/employees/{john_id}` | Response is 200 OK with John Smith's own profile data. |
| 4 | Verify no data from Jane Doe was exposed | Response body in step 2 contains no employee data. |

## 6. Postconditions
- No cross-employee data exposure within the same tenant.
- Employee can only access their own profile.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
