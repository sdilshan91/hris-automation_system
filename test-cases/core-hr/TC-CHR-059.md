---
id: TC-CHR-059
user_story: US-CHR-005
module: Core HR
priority: medium
type: performance
status: draft
created: 2026-06-12
---

# TC-CHR-059: Support large number of job titles per tenant without degradation

## 1. Test Objective
Verify that the Job Titles management page and API remain performant when a tenant has a large number of job titles (e.g., 200+), without significant degradation in response times or rendering.

## 2. Related Requirements
- User Story: US-CHR-005
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated.
- The tenant has been seeded with 200+ job titles (mix of active and inactive).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Job titles count | 200+ | Seeded bulk data |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/job-titles` and measure response time | Response time is <= 400ms (P95). Response includes pagination or all 200+ records. |
| 2 | Navigate to the Job Titles management page in the browser | Page renders within 2.5 seconds. Table or paginated view is usable. |
| 3 | Use the search bar to filter job titles by name | Search results return within 500ms. |
| 4 | Create a new job title (title #201) | Create operation completes within 800ms. |
| 5 | Verify scrolling and pagination (if implemented) are smooth | No UI jank or freezing when scrolling through 200+ titles. |

## 6. Postconditions
- Performance is verified at scale for the job titles feature.
- Any degradation patterns are documented.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
