---
id: TC-CHR-128
user_story: US-CHR-003
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-128: Search directory by partial name, email, and employee_no (happy path)

## 1. Test Objective
Verify that the directory search bar filters employees in real-time (after a 300ms debounce) by partial match (case-insensitive) against name, email, employee_no, and phone fields. This validates AC-2, FR-1.

## 2. Related Requirements
- User Story: US-CHR-003
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1
- Business Rules: BR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- 50 employees exist including: "John Smith" (john.smith@acme.com, EMP-0042, phone: +1-555-0142), "Johnathan Doe" (jdoe@acme.com, EMP-0099), "Sarah Johnson" (sarah.j@acme.com, EMP-0001).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Search term 1 | John | Should match "John Smith", "Johnathan Doe", "Sarah Johnson" |
| Search term 2 | EMP-0042 | Should match "John Smith" only |
| Search term 3 | john.smith@acme | Should match "John Smith" only |
| Search term 4 | 555-0142 | Should match "John Smith" by phone |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Employee Directory page | Full directory loads with all 50 employees paginated. |
| 2 | Type "John" in the search bar | After 300ms debounce, the API call is made with `?search=John`. |
| 3 | Verify search results | Directory shows 3 employees: "John Smith", "Johnathan Doe", "Sarah Johnson" (partial match on first/last name). Total count updates to 3. |
| 4 | Clear the search bar and type "EMP-0042" | After debounce, API is called with `?search=EMP-0042`. |
| 5 | Verify search results | Directory shows 1 employee: "John Smith". |
| 6 | Clear and type "john.smith@acme" | After debounce, directory filters to 1 result: "John Smith" (email partial match). |
| 7 | Clear and type "555-0142" | After debounce, directory filters to 1 result: "John Smith" (phone partial match). |
| 8 | Verify case insensitivity: type "jOhN" | Results match the same 3 employees as step 3. |
| 9 | Verify URL updates with search param | URL includes `?search=jOhN` (or the current search term). |
| 10 | Verify no API call during the 300ms debounce | Typing rapidly does not trigger multiple API calls; only one call is made after the user stops typing for 300ms. |

## 6. Postconditions
- No data was modified.
- Search state is reflected in URL query parameters.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
