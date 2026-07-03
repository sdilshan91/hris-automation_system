---
id: TC-CHR-123
user_story: US-CHR-002
module: Core HR
priority: high
type: functional
status: pass
exec_note: "2026-07-03 (API, BUG-119 verify-rerun, PR#124): PASS — ownership now enforced. As employee@acme.test, PATCH /tenant/employees/{OWN}/profile with contactInfo.phone=555-2000 -> HTTP 200, persisted (self-edit AC-4 works). Cross-check (BUG-119): PATCH of ANOTHER employee's /profile -> HTTP 403 'You can only edit your own profile.' (was 200 pre-fix). Prior note 2026-07-01: self-edit worked but Edit.Own had no owner check — now closed. Audit-log arm still not deep-dived."
created: 2026-06-12
---

# TC-CHR-123: Employee edits permitted fields (phone, email, address, emergency contacts) -- self-service happy path

## 1. Test Objective
Verify that an Employee can edit their own permitted fields (phone, personal email, address, emergency contacts) via the self-service portal and that the changes are saved successfully with audit logging. This validates AC-4 (edit permitted fields), FR-2, FR-5.

## 2. Related Requirements
- User Story: US-CHR-002
- Acceptance Criteria: AC-4
- Functional Requirements: FR-2, FR-5
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- User "John Smith" is authenticated with Employee role in "acme".
- John Smith's employee record exists with phone "555-1000" and one emergency contact.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Employee | Self-service |
| Employee ID | {john_smith_id} | Own profile |
| Original Phone | 555-1000 | Before edit |
| New Phone | 555-2000 | After edit |
| New Emergency Contact Name | Sarah Smith | Added contact |
| New Emergency Contact Phone | 555-3000 | Added contact |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to self-service profile page | Profile loads with John Smith's data. |
| 2 | Click Edit on the Contact section | Card transitions to edit mode with phone, personal email, and address fields editable. |
| 3 | Change phone from "555-1000" to "555-2000" | Input accepts the new value. |
| 4 | Click Save | PATCH request sent. Response is 200 OK. Success toast displayed. Phone updated to "555-2000". |
| 5 | Verify audit log | Entry exists with before/after snapshot showing phone change. |
| 6 | Click Edit on Emergency Contacts section | Card transitions to edit mode. |
| 7 | Add a new emergency contact: "Sarah Smith", "555-3000" | Form fields accept the input. |
| 8 | Click Save | Request succeeds. Emergency contacts list now shows the new contact. |
| 9 | Verify audit log for emergency contact change | Entry exists with before/after snapshot. |

## 6. Postconditions
- Phone updated to "555-2000".
- New emergency contact added.
- Audit entries for both changes exist.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

> **Execution 2026-06-30 (FE, acme):** STILL BLOCKED — the employee profile/detail page is reached in-app by clicking a row in the Employee Directory, which is crashed (**BUG-099**: list never renders), so no profile can be opened by navigation. (TC-106 also needs the employee@acme self-service persona / portal route.) Not runnable until the directory list renders.
