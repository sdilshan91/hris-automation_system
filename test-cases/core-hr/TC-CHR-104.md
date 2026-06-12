---
id: TC-CHR-104
user_story: US-CHR-002
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-104: HR Officer views full employee profile -- all sections render (happy path)

## 1. Test Objective
Verify that when an HR Officer navigates to an employee's profile page, all card-based sections render with correct data: Summary Header, Personal Info, Contact, Emergency Contacts, Employment Details, Education, Work History, Dependents, Documents, and Custom Fields. This validates AC-1, FR-1, and FR-7.

## 2. Related Requirements
- User Story: US-CHR-002
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-7
- Non-Functional Requirements: NFR-4 (audit of PII access)
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" exists with status `active` and subdomain `acme.yourhrm.com`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- An employee record "Jane Doe" (EMP-0042) exists in the "acme" tenant with data populated across all profile sections: personal info, contact details, two emergency contacts, employment details (department: Engineering, job title: Senior Developer, status: Active), one education record, one work history entry, one dependent, one uploaded document, and two custom fields.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Full read/write access |
| Employee ID | {jane_doe_id} | UUID of "Jane Doe" |
| Employee No | EMP-0042 | Auto-generated |
| Department | Engineering | Active department |
| Job Title | Senior Developer | Active job title |
| Status | Active | Green badge expected |
| Custom Fields | {"shirt_size": "M", "parking_spot": "A-12"} | JSONB |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `https://acme.yourhrm.com/employees/{jane_doe_id}` | Profile page begins loading; skeleton shimmer placeholders are displayed (Notion-style). |
| 2 | Wait for page load to complete | All skeleton placeholders are replaced with populated cards. |
| 3 | Verify Summary Header card | Displays: circular avatar (96px), "Jane Doe", employee_no badge "EMP-0042", department tag "Engineering", status badge (green, "Active"). |
| 4 | Verify Personal Info card | Contains: first name, last name, date of birth, gender, marital status. Card has a pencil edit icon in the top-right corner. Card is collapsible. |
| 5 | Verify Contact card | Contains: phone, personal email, address fields. Edit icon present. |
| 6 | Verify Emergency Contacts card | Lists two emergency contacts with name, relationship, phone. Edit icon present. |
| 7 | Verify Employment Details card | Contains: department, job title, employment type, date of joining, reporting manager, status. Edit icon present. |
| 8 | Verify Education card | Lists one education record with institution, degree, year. Edit icon present. |
| 9 | Verify Work History card | Lists one work history entry as a vertical timeline with date markers. Edit icon present. |
| 10 | Verify Dependents card | Lists one dependent with name, relationship, date of birth. Edit icon present. |
| 11 | Verify Documents card | Lists one document with name, type, upload date. |
| 12 | Verify Custom Fields card | Displays "shirt_size: M" and "parking_spot: A-12". |
| 13 | Verify all sections use Angular Material `MatTabGroup` for navigation on desktop | Tab indicator animation is visible when switching between sections. |
| 14 | Verify the API call `GET /api/v1/tenant/employees/{jane_doe_id}` was made | Response status is 200 OK; response body contains all section data including joined department name, job title name, emergency contacts, education, work history, dependents, documents, and custom_fields JSONB. |

## 6. Postconditions
- No data was modified.
- PII access is recorded in the audit log (NFR-4).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
