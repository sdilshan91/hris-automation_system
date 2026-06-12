---
id: TC-CHR-058
user_story: US-CHR-005
module: Core HR
priority: high
type: performance
status: draft
created: 2026-06-12
---

# TC-CHR-058: Job titles page load within 2.5 seconds

## 1. Test Objective
Verify that the Job Titles management page loads completely within 2.5 seconds, including the initial API call to fetch job titles and the rendering of the card-based table.

## 2. Related Requirements
- User Story: US-CHR-005
- Non-Functional Requirements: NFR-1, NFR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated.
- The tenant has a representative number of job titles (50+).
- Browser cache is cleared (cold start scenario).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Job titles count | 50+ | Realistic data volume |
| Browser | Chrome (latest) | Primary test browser |
| Network | No throttling | Standard conditions |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Clear browser cache and navigate to `https://acme.yourhrm.com/job-titles` | Page begins loading. |
| 2 | Measure time from navigation start to DOMContentLoaded | DOMContentLoaded <= 2.0 seconds. |
| 3 | Measure time from navigation start to full page interactive (all data rendered) | Total load time <= 2.5 seconds. |
| 4 | Verify the job titles table is fully rendered with all rows visible | All 50+ job titles are displayed (or paginated with first page rendered). |
| 5 | Repeat the measurement 3 times and record the average | Average page load is <= 2.5 seconds. |

## 6. Postconditions
- Page load performance metrics are documented.
- Any threshold violations are flagged.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
