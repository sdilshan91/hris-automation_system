---
id: TC-CHR-318
user_story: US-CHR-012
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-318: All supported field types can be created and rendered

## 1. Test Objective
Verify that the system supports creating custom fields for each of the 10 supported field types (text, textarea, number, date, dropdown, multi_select, checkbox, email, phone, url) and that each renders correctly on employee forms with appropriate input controls. This validates FR-2.

## 2. Related Requirements
- User Story: US-CHR-012
- Functional Requirements: FR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Tenant Admin is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Type 1 | Text (short) | Single-line text input |
| Type 2 | Text (long/multiline) | Textarea |
| Type 3 | Number | Numeric input |
| Type 4 | Date | Date picker |
| Type 5 | Dropdown (single select) | Select with options |
| Type 6 | Dropdown (multi-select) | Multi-select with chips |
| Type 7 | Checkbox (boolean) | Toggle or checkbox |
| Type 8 | Email | Email input with validation |
| Type 9 | Phone | Phone input |
| Type 10 | URL | URL input with validation |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create a Text (short) field "Nickname". | Created successfully. Renders as a single-line text input on employee form. |
| 2 | Create a Text (long/multiline) field "Bio". | Created successfully. Renders as a textarea on employee form. |
| 3 | Create a Number field "Badge Number". | Created successfully. Renders as a numeric input. |
| 4 | Create a Date field "Certification Date". | Created successfully. Renders as a date picker. |
| 5 | Create a Dropdown field "Preferred Language" with options ["English", "Spanish", "French"]. | Created successfully. Renders as a select dropdown. |
| 6 | Create a Multi-select field "Skills" with options ["JavaScript", "Python", "SQL", "Java"]. | Created successfully. Renders as a multi-select with checkable options or chips. |
| 7 | Create a Checkbox field "Has Parking Pass". | Created successfully. Renders as a toggle/checkbox. |
| 8 | Create an Email field "Personal Email 2". | Created successfully. Renders as an email input with email format validation. |
| 9 | Create a Phone field "Home Phone". | Created successfully. Renders as a phone input. |
| 10 | Create a URL field "LinkedIn Profile". | Created successfully. Renders as a URL input with URL format validation. |
| 11 | Navigate to an employee form and verify all 10 fields render in the Custom Fields section. | All 10 fields are present with correct input types. |

## 6. Postconditions
- All 10 field types are creatable and renderable on employee forms.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
