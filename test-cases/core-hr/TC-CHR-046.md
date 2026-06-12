---
id: TC-CHR-046
user_story: US-CHR-005
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-046: Title name boundary values (max length, whitespace, special characters)

## 1. Test Objective
Verify that the `title_name` field correctly handles boundary values including maximum length (150 characters per schema), leading/trailing whitespace trimming, and valid special characters, while rejecting names that exceed the maximum length.

## 2. Related Requirements
- User Story: US-CHR-005
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1, FR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Authorized role |
| Max-length name | A string of exactly 150 characters | Boundary: at limit |
| Over-limit name | A string of 151 characters | Boundary: exceeds limit |
| Leading/trailing whitespace | "  Lead Developer  " | Should be trimmed |
| Special characters | "Sr. Developer (Full-Stack)" | Parentheses, periods, hyphens |
| Unicode characters | "Entwickler (Senior)" | Non-ASCII |
| Single character | "X" | Minimum valid length |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create a job title with exactly 150 characters in the Title Name field | Response status is 201 Created. The name is stored as provided (at the boundary). |
| 2 | Attempt to create a job title with 151 characters in the Title Name field | Response status is 400 Bad Request with validation error indicating maximum length exceeded. |
| 3 | Create a job title with "  Lead Developer  " (leading and trailing spaces) | Response status is 201 Created. The stored name is trimmed to "Lead Developer" (or the system rejects whitespace-padded input consistently). |
| 4 | Create a job title with "Sr. Developer (Full-Stack)" | Response status is 201 Created. Special characters (period, parentheses, hyphen) are accepted. |
| 5 | Create a job title with Unicode characters, e.g., "Entwickler (Senior)" | Response status is 201 Created. Unicode is stored correctly. |
| 6 | Create a job title with a single character "X" | Response status is 201 Created (or rejected if a minimum length is enforced). |
| 7 | Verify all successfully created titles appear correctly in the list and via GET API | Names render correctly, including special and Unicode characters. |

## 6. Postconditions
- Valid boundary-value titles are created and retrievable.
- Over-limit names are rejected.
- Whitespace handling is consistent.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
