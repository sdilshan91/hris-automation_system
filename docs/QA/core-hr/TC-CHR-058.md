---
id: TC-CHR-058
user_story: US-CHR-005
module: Core HR
priority: high
type: performance
status: blocked
created: 2026-06-12
exec_note: "P3b k6 2026-06-30: NOT MEASURABLE BY k6 — kept blocked. Target is FE page-load (DOMContentLoaded<=2.0s / total<=2.5s, browser render of the Job Titles page), not a server API. Needs a Chrome DevTools FE perf trace, not k6. (For reference, the job-titles LIST API read measured 57–70ms p95.)"

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

> **Execution 2026-06-30 (FE, acme):** STILL BLOCKED — performance/SLA TC. Job Titles renders promptly in-browser, but this needs instrumented page-load/scale timing the shared Playwright browser cannot measure. Not a functional defect.
> **Re-exec 2026-06-30 (deep FE-render pass, CDP):** STILL BLOCKED. Steps 1–3 are explicitly **cold-load** ("Clear browser cache and navigate", DOMContentLoaded / total-load from navigation start) — a true cold load needs a full navigation/reload which logs you out (**BUG-097**); not cleanly measurable until BUG-097 is fixed. The page renders fine via soft-nav (no crash, no jank), but soft-nav render ≠ this TC's cold-load DOMContentLoaded/total-load metric. No pass arm available.
