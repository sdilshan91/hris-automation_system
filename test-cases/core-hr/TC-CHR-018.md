---
id: TC-CHR-018
user_story: US-CHR-004
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-018: Department name boundary values (max length, whitespace, special characters)

## 1. Test Objective
Verify department name handling at boundary conditions: maximum length (150 chars per schema), name exceeding maximum, whitespace-only names, and names with special characters.

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1, FR-2
- Data Requirements: Section 7 (name varchar(150))

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Name at max length | "A" repeated 150 times | Exactly 150 characters |
| Name over max length | "A" repeated 151 times | Exceeds varchar(150) |
| Whitespace-only name | "   " (spaces) | Should be rejected |
| Special characters | "R&D / Engineering - Phase 2 (Core)" | Valid name with specials |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create a department with name = 150 "A" characters | API returns 201 Created. Name is stored fully (150 chars). |
| 2 | Attempt to create a department with name = 151 "A" characters | API returns 400 Bad Request or 422 Unprocessable Entity with message indicating name exceeds maximum length. |
| 3 | Attempt to create a department with name = "   " (whitespace only) | API returns 400/422 with message indicating name is required (after trimming). |
| 4 | Create a department with name = "R&D / Engineering - Phase 2 (Core)" | API returns 201 Created. Special characters are stored and displayed correctly. |
| 5 | Verify the department with 150-char name is displayed correctly in the list and tree view | Name is not truncated in data storage; UI may truncate with ellipsis for display. |

## 6. Postconditions
- Departments with valid boundary names exist.
- Invalid boundary names were rejected.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
