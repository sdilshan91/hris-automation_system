---
id: TC-CHR-110
user_story: US-CHR-002
module: Core HR
priority: high
type: functional
status: blocked
exec_note: "2026-07-01 (API, fntest): BLOCKED — Manager (Employee.View.Team) gets 403 on /profile, /{id}, /directory and /direct-reports; AND fntest has no manager→report linkage seeded (all managerId null), so the direct-report precondition cannot be established. Read-only edit-button-visibility is also a FE-UI arm. Needs seeded reporting chain + browser."
created: 2026-06-12
---

# TC-CHR-110: Manager views direct report profile in read-only mode

## 1. Test Objective
Verify that a Manager can view the profile of their direct report in read-only mode. No edit buttons should be visible for any section. This validates FR-3 and BR-3.

## 2. Related Requirements
- User Story: US-CHR-002
- Acceptance Criteria: AC-1 (partial -- Manager perspective)
- Functional Requirements: FR-3
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- User "Maria" is authenticated with Manager role in the "acme" tenant.
- Employee "Jane Doe" is a direct report of Maria (Maria is the reporting manager for Jane Doe's department or direct assignment).
- Jane Doe has populated data in all visible sections.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Manager | Read-only for direct reports |
| Manager | Maria | Jane Doe's reporting manager |
| Employee ID | {jane_doe_id} | Direct report |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Maria (Manager role) in "acme" tenant | JWT contains manager role and tenant_id for acme. |
| 2 | Navigate to Jane Doe's profile page | Profile loads successfully with all visible sections. |
| 3 | Verify Summary Header, Personal Info, Contact sections | All data is displayed as read-only text. No edit icons visible. |
| 4 | Verify Employment Details section | Department, job title, status are visible and read-only. No edit icon. |
| 5 | Verify Emergency Contacts section | Section is NOT visible to Manager (per data requirements: "No access"). |
| 6 | Verify Education & Work History sections | Sections are NOT visible to Manager (per data requirements: "No access"). |
| 7 | Verify Dependents section | Section is NOT visible to Manager (per data requirements: "No access"). |
| 8 | Verify Custom Fields section | Custom fields are visible and read-only. |
| 9 | Inspect the page source / DOM for hidden edit forms or buttons | No hidden edit affordances exist in the DOM for any section. |

## 6. Postconditions
- No data was modified.
- Manager accessed only permitted sections.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

> **Execution 2026-06-30 (FE, acme):** STILL BLOCKED — the employee profile/detail page is reached in-app by clicking a row in the Employee Directory, which is crashed (**BUG-099**: list never renders), so no profile can be opened by navigation. (TC-106 also needs the employee@acme self-service persona / portal route.) Not runnable until the directory list renders.
