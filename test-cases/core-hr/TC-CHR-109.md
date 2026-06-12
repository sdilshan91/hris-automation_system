---
id: TC-CHR-109
user_story: US-CHR-002
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-109: Employment history timeline records sequential department and job title changes

## 1. Test Objective
Verify that when an HR Officer changes an employee's department and then changes their job title, each change creates a separate entry in the employment history timeline with correct dates and descriptions. Also test that the timeline renders as a vertical timeline with date markers. This validates AC-6, FR-6, and BR-4.

## 2. Related Requirements
- User Story: US-CHR-002
- Acceptance Criteria: AC-6
- Functional Requirements: FR-6
- Business Rules: BR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme" tenant.
- Employee "Jane Doe" exists in department "Engineering" with job title "Junior Developer".
- Departments "Engineering" and "Product" both exist and are active.
- Job titles "Junior Developer" and "Senior Developer" both exist and are active.
- The employment history timeline for Jane Doe currently has one entry (the original assignment).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Full access |
| Employee ID | {jane_doe_id} | UUID |
| Original Department | Engineering | Current assignment |
| New Department | Product | First change |
| Original Job Title | Junior Developer | Current assignment |
| New Job Title | Senior Developer | Second change |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Jane Doe's profile | Profile loads showing current department "Engineering" and job title "Junior Developer". |
| 2 | Click Edit on the Employment Details section | Card transitions to edit mode. |
| 3 | Change department from "Engineering" to "Product" | Dropdown shows available departments; select "Product". |
| 4 | Click Save | PATCH succeeds (200 OK). Success toast displayed. Department now shows "Product". |
| 5 | Verify employment history timeline | A new entry appears: "Department changed from Engineering to Product" with today's effective date and date marker. |
| 6 | Verify reporting structure updated | If "Product" has a different manager, the reporting manager field is updated accordingly. |
| 7 | Click Edit on Employment Details again | Card transitions to edit mode. |
| 8 | Change job title from "Junior Developer" to "Senior Developer" | Dropdown shows available titles; select "Senior Developer". |
| 9 | Click Save | PATCH succeeds (200 OK). Success toast displayed. Job title now shows "Senior Developer". |
| 10 | Verify employment history timeline | A second new entry appears: "Job title changed from Junior Developer to Senior Developer" with today's effective date. Timeline now has 3 entries total (original + 2 changes), rendered as a vertical timeline with date markers. |
| 11 | Verify the timeline entries are in reverse chronological order | Most recent change appears at the top. |
| 12 | Verify audit log has two separate entries | One for the department change, one for the job title change. Each with before/after JSONB snapshots. |

## 6. Postconditions
- Employee is now in "Product" department with "Senior Developer" job title.
- Employment history has two new timeline entries.
- Audit log has two corresponding entries.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
