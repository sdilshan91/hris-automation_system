---
id: TC-CHR-088
user_story: US-CHR-001
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-088: First name and last name boundary values (min 1, max 100 chars)

## 1. Test Objective
Verify that first_name and last_name fields enforce their length constraints: minimum 1 character, maximum 100 characters. Empty values are rejected, values at or below 100 characters are accepted, and values exceeding 100 characters are rejected.

## 2. Related Requirements
- User Story: US-CHR-001
- Data Requirements: first_name varchar(100) Min 1 Max 100, last_name varchar(100) Min 1 Max 100

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- Department and job title exist in the tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| first_name (min) | "A" | 1 char, at minimum boundary |
| first_name (max) | 100 "A" characters | At maximum boundary |
| first_name (over max) | 101 "A" characters | Exceeds maximum |
| first_name (empty) | "" | Below minimum |
| first_name (whitespace) | "   " | Whitespace only |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Submit with first_name = "" (empty) | Validation error: "First name is required." |
| 2 | Submit with first_name = "   " (whitespace only) | Validation error: treated as empty after trimming. |
| 3 | Submit with first_name = "A" (1 char) | Accepted. Employee created successfully. |
| 4 | Submit with first_name = 100 "A" characters | Accepted. Employee created successfully. |
| 5 | Submit with first_name = 101 "A" characters | Validation error: "First name must not exceed 100 characters." |
| 6 | Repeat steps 1-5 for last_name with equivalent values | Same boundary behavior as first_name. |

## 6. Postconditions
- Employees with valid name lengths are created.
- Employees with invalid name lengths are rejected.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
