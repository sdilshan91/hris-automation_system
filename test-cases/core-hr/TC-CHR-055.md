---
id: TC-CHR-055
user_story: US-CHR-005
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-055: Tenant A cannot see or modify Tenant B's job titles

## 1. Test Objective
Verify that a user authenticated in Tenant A cannot view, edit, or deactivate job titles belonging to Tenant B, and vice versa. This validates multi-tenant data isolation for the job titles feature at the API level.

## 2. Related Requirements
- User Story: US-CHR-005
- Non-Functional Requirements: NFR-2
- Business Rules: BR-1, BR-4

## 3. Preconditions
- Tenant "acme" exists with status `active` and subdomain `acme.yourhrm.com`.
- Tenant "globex" exists with status `active` and subdomain `globex.yourhrm.com`.
- A Tenant Admin user is authenticated in each tenant.
- Tenant "acme" has a job title "Software Engineer" (known UUID: `acme_jt_id`).
- Tenant "globex" has a job title "Network Admin" (known UUID: `globex_jt_id`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme.yourhrm.com | Has "Software Engineer" |
| Tenant B | globex.yourhrm.com | Has "Network Admin" |
| Acme title ID | {acme_jt_id} | UUID of acme's title |
| Globex title ID | {globex_jt_id} | UUID of globex's title |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Tenant Admin in "acme" tenant. Call `GET /api/v1/job-titles` | Response returns only acme's job titles. "Network Admin" (globex) is NOT in the results. |
| 2 | As acme user, call `GET /api/v1/job-titles/{globex_jt_id}` (using globex's title ID) | Response status is 404 Not Found (tenant query filter prevents access). |
| 3 | As acme user, call `PUT /api/v1/job-titles/{globex_jt_id}` with body `{ title_name: "Hacked Title" }` | Response status is 404 Not Found. |
| 4 | As acme user, call `PATCH /api/v1/job-titles/{globex_jt_id}/deactivate` | Response status is 404 Not Found. |
| 5 | Authenticate as Tenant Admin in "globex" tenant. Call `GET /api/v1/job-titles` | Response returns only globex's job titles. "Software Engineer" (acme) is NOT in the results. |
| 6 | As globex user, call `GET /api/v1/job-titles/{acme_jt_id}` | Response status is 404 Not Found. |
| 7 | Verify neither tenant's job titles were modified during the cross-tenant attempts | Both "Software Engineer" (acme) and "Network Admin" (globex) remain unchanged. |

## 6. Postconditions
- No cross-tenant data leakage occurred.
- No cross-tenant modifications were performed.
- Both tenants' job titles remain intact.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
