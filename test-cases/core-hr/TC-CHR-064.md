---
id: TC-CHR-064
user_story: US-CHR-001
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-064: Multi-step wizard renders all sections (AC-1)

## 1. Test Objective
Verify that clicking "Add Employee" opens a card-based multi-step wizard with all required sections: Personal Info, Contact, Emergency Contact, Employment Details, and optional sections (Education, Work History, Dependents), each rendered as a separate card with a progress indicator.

## 2. Related Requirements
- User Story: US-CHR-001
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- At least one department and one job title exist in the tenant.
- Tenant subscription plan has not exceeded the maximum employee limit.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized role |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Employee module (e.g., /employees) | Employee list page loads. |
| 2 | Click the "Add Employee" button | A multi-step form (card-based wizard) opens. |
| 3 | Verify a progress indicator is visible at the top | Step dots or breadcrumb trail shows all wizard steps. |
| 4 | Verify Step 1 "Personal Info" card is displayed | Card with shadow-sm and rounded-xl styling is visible. Fields include: first_name, last_name, email, date_of_birth, gender, profile_photo upload area. |
| 5 | Click "Save & Continue" (or the next step indicator) | Smooth slide or fade transition (200-300ms ease-in-out) to Step 2. |
| 6 | Verify Step 2 "Contact Details" card is displayed | Card with phone, address, and other contact fields is visible. |
| 7 | Navigate to Step 3 "Emergency Contact" | Card with emergency contact fields (name, relationship, phone) is visible. |
| 8 | Navigate to Step 4 "Employment Details" | Card with date_of_joining, department_id, job_title_id, employment_type, status fields is visible. |
| 9 | Navigate to optional section "Education History" | Card is visible and all fields are optional. |
| 10 | Navigate to optional section "Work History" | Card is visible and all fields are optional. |
| 11 | Navigate to optional section "Dependents" | Card is visible and all fields are optional. |
| 12 | Verify "Save as Draft" button is present on each step | Button is visible at the bottom of each card alongside "Save & Continue". |

## 6. Postconditions
- No employee record is created (wizard was only navigated, not submitted).
- UI correctly renders all sections and transitions.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
