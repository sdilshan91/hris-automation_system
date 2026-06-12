---
id: TC-CHR-256
user_story: US-CHR-010
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-256: Import does not create user accounts -- employees exist without portal login

## 1. Test Objective
Verify that bulk import creates employee records only and does NOT create corresponding user accounts. Imported employees cannot log in to the portal until separately invited. This validates BR-5.

## 2. Related Requirements
- User Story: US-CHR-010
- Business Rules: BR-5

## 3. Preconditions
- Tenant "acme" exists with status `active` and sufficient capacity.
- An HR Officer user is authenticated in the "acme" tenant context.
- No user accounts exist for the emails in the import file.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |
| File Name | no_accounts.csv | 3 valid rows |
| Emails | noaccount1@acme.test, noaccount2@acme.test, noaccount3@acme.test | Fresh emails |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload `no_accounts.csv` and click "Import". | 3 employees created successfully. |
| 2 | Query the `users` table for the 3 imported email addresses. | No user records exist for these emails. The `employee.user_id` FK is null for all 3. |
| 3 | Attempt to log in at `https://acme.yourhrm.com/login` with email `noaccount1@acme.test` and any password. | Login fails -- no user account exists. Response is the standard "invalid credentials" error. |

## 6. Postconditions
- 3 employee records created. No user accounts created.
- Employees must be separately invited to the portal (BR-5).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
