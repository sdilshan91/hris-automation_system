---
id: TC-CHR-ISO-005
user_story: US-CHR-005
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-005: Tenant A cannot see Tenant B's job titles

## 1. Test Objective
Verify that the EF Core global query filter on the `job_title` entity prevents Tenant A from seeing any job titles belonging to Tenant B when querying through the API. This is a mandatory multi-tenant isolation test.

## 2. Related Requirements
- User Story: US-CHR-005
- Non-Functional Requirements: NFR-2
- Business Rules: BR-1, BR-4

## 3. Preconditions
- Tenant "acme" (Tenant A) exists with status `active`.
- Tenant "globex" (Tenant B) exists with status `active`.
- Tenant A has job titles: "Developer", "Designer".
- Tenant B has job titles: "Accountant", "Auditor".
- A Tenant Admin user is authenticated in each tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Has Developer, Designer |
| Tenant B | globex | Has Accountant, Auditor |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Tenant Admin in Tenant A (acme). Call `GET /api/v1/job-titles` | Response contains "Developer" and "Designer" only. "Accountant" and "Auditor" do NOT appear. |
| 2 | Authenticate as Tenant Admin in Tenant B (globex). Call `GET /api/v1/job-titles` | Response contains "Accountant" and "Auditor" only. "Developer" and "Designer" do NOT appear. |
| 3 | As Tenant A user, attempt to search for "Accountant" via `GET /api/v1/job-titles?search=Accountant` | Response returns zero results. |
| 4 | Verify the total count returned for each tenant matches only their own job titles | Tenant A count = 2, Tenant B count = 2. |

## 6. Postconditions
- No cross-tenant data leakage occurred.
- Each tenant sees only their own job titles.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
