---
id: TC-CHR-106
user_story: US-CHR-002
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-106: Employee views own profile via self-service portal (happy path)

## 1. Test Objective
Verify that an Employee can view their own full profile in read-only mode via the self-service portal, and that only permitted editable fields (phone, personal email, address, emergency contacts) show an "Edit" button. This validates AC-4, FR-3, and BR-1.

## 2. Related Requirements
- User Story: US-CHR-002
- Acceptance Criteria: AC-4
- Functional Requirements: FR-3
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- User "John Smith" is authenticated with Employee role in the "acme" tenant.
- Employee record for "John Smith" exists with all sections populated.
- John Smith is linked to this user account via `user_id` FK.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Employee | Limited access |
| Employee ID | {john_smith_id} | Own profile |
| Department | Marketing | Read-only for Employee |
| Job Title | Marketing Coordinator | Read-only for Employee |
| Salary | 55000 | Read-only, no edit affordance |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to self-service profile page `https://acme.yourhrm.com/my-profile` | Profile page loads showing John Smith's full profile. |
| 2 | Verify Summary Header | Displays avatar, name, employee_no, department tag, status badge -- all read-only. No edit icon on header. |
| 3 | Verify Personal Info section | Fields (name, DOB, gender) are displayed as read-only text. No edit icon visible on this card. |
| 4 | Verify Contact section | Phone, personal email, and address are displayed. An "Edit" button IS visible on this card. |
| 5 | Verify Emergency Contacts section | Emergency contacts are listed. An "Edit" button IS visible on this card. |
| 6 | Verify Employment Details section | Department, job title, employment type, salary, status are displayed as read-only. No edit icon on this card. |
| 7 | Verify Education section | Education records are displayed. An "Edit" button IS visible (per data requirements table, Employee has Read/Write on Education). |
| 8 | Verify Work History section | Work history entries are displayed. An "Edit" button IS visible (Employee has Read/Write on Work History). |
| 9 | Verify Dependents section | Dependents are displayed. An "Edit" button IS visible (Employee has Read/Write on Dependents). |
| 10 | Verify Custom Fields section | Custom fields are displayed as read-only text. No edit icon visible (Employee has Read only by default). |
| 11 | Confirm that salary, department, and job title fields have no edit affordance -- no pencil icon, no click-to-edit, no inline edit capability | Fields render as static, non-interactive text. |

## 6. Postconditions
- No data was modified.
- PII access audit logged for the employee viewing their own profile.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
