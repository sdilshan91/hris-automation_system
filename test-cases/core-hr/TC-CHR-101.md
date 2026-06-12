---
id: TC-CHR-101
user_story: US-CHR-001
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-101: Wizard Save as Draft and Save & Continue functionality

## 1. Test Objective
Verify that the "Save as Draft" button at each wizard step persists the partially filled form data so the user can return later, and that "Save & Continue" advances to the next step while preserving entered data.

## 2. Related Requirements
- User Story: US-CHR-001
- UI/UX Notes: Section 8 ("Save as Draft" and "Save & Continue" buttons)

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated.
- Department and job title exist in the tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| first_name | John | Partial data |
| last_name | Doe | Partial data |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Add Employee wizard | Step 1 (Personal Info) is displayed. |
| 2 | Fill in first_name = "John", last_name = "Doe" but leave email empty | Partial data entered. |
| 3 | Click "Save as Draft" | Data is saved as a draft. A success indication is shown (e.g., toast or inline message "Draft saved"). |
| 4 | Navigate away from the wizard (e.g., back to employee list) | Wizard closes. |
| 5 | Return to the Add Employee wizard (or drafts list) and open the saved draft | The wizard reopens with first_name = "John" and last_name = "Doe" pre-populated. Email is still empty. |
| 6 | Fill in email and click "Save & Continue" | Wizard advances to Step 2 (Contact Details) with smooth transition. Data from Step 1 is preserved. |
| 7 | Go back to Step 1 | Previously entered data (first_name, last_name, email) is still present. |
| 8 | Complete all steps and submit | Employee created successfully from the draft. |

## 6. Postconditions
- Drafts are persisted and recoverable.
- "Save & Continue" preserves data across steps.
- Final submission creates the employee record.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
