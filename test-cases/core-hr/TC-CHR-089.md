---
id: TC-CHR-089
user_story: US-CHR-001
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-089: Email field boundary value (max 150 chars)

## 1. Test Objective
Verify that the email field enforces its maximum length of 150 characters. Emails at or below 150 characters are accepted; emails exceeding 150 characters are rejected.

## 2. Related Requirements
- User Story: US-CHR-001
- Data Requirements: email varchar(150)

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- Department and job title exist in the tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| email (at max) | a{138}@example.com | 150 chars total (138 + 12) |
| email (over max) | a{139}@example.com | 151 chars total (139 + 12) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Submit with email = 150-character valid email address | Employee created successfully. |
| 2 | Submit with email = 151-character email address | Validation error: "Email must not exceed 150 characters." |

## 6. Postconditions
- Email length validation is enforced at both client and server levels.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
