---
id: TC-CHR-086
user_story: US-CHR-001
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-086: Invalid email format rejected

## 1. Test Objective
Verify that the system rejects email addresses that do not conform to a valid email format, displaying an inline validation error.

## 2. Related Requirements
- User Story: US-CHR-001
- Acceptance Criteria: AC-2 (negative path)
- Data Requirements: email must be valid email format

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Invalid email 1 | not-an-email | No @ symbol |
| Invalid email 2 | @nodomain.com | No local part |
| Invalid email 3 | user@.com | No domain name |
| Invalid email 4 | user@ | Incomplete domain |
| Invalid email 5 | user@domain | No TLD (may or may not be valid depending on strictness) |
| Valid email | john.doe@example.com | Standard valid email |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Enter email = "not-an-email" and attempt to leave the field (blur) or submit | Validation error: "Please enter a valid email address." |
| 2 | Enter email = "@nodomain.com" | Validation error displayed. |
| 3 | Enter email = "user@.com" | Validation error displayed. |
| 4 | Enter email = "user@" | Validation error displayed. |
| 5 | Enter email = "john.doe@example.com" | No validation error. Field accepts the value. |
| 6 | Submit with a valid email and all other mandatory fields | Employee created successfully. |

## 6. Postconditions
- Only employees with valid email formats can be created.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
