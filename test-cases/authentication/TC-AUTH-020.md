---
id: TC-AUTH-020
user_story: US-AUTH-007
module: Authentication
priority: critical
type: security
status: draft
created: 2026-05-11
---

# TC-AUTH-020: Unknown subdomain returns 404

## 1. Test Objective
Verify that navigating to an unprovisioned subdomain returns a 404 Not Found response with a static error page, and no SPA shell, login form, or API endpoints are exposed.

## 2. Related Requirements
- User Story: US-AUTH-007
- Acceptance Criteria: AC-2
- User Story: US-AUTH-001
- Acceptance Criteria: AC-6
- Functional Requirements: FR-7
- Non-Functional Requirements: NFR-5

## 3. Preconditions
- No tenant exists with subdomain "unknown" in the `tenant` table.
- The subdomain "unknown" is not in the reserved subdomain list.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | unknown.yourhrm.com | Not provisioned |
| Subdomain 2 | nonexistent.yourhrm.com | Not provisioned |
| Expected HTTP Status | 404 | Not Found |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send a request to `https://unknown.yourhrm.com/` | HTTP 404 Not Found. |
| 2 | Verify the response is a static error page | Page contains "This workspace does not exist" message with the platform logo and a link to the main platform site. |
| 3 | Verify no SPA bundle (Angular app shell) is served | No JavaScript bundle, no `<app-root>` element, no SPA routing. |
| 4 | Verify no login form is rendered | No email/password fields are present. |
| 5 | Send `POST /api/v1/auth/login` to `unknown.yourhrm.com` | HTTP 404 Not Found; the login API is not accessible. |
| 6 | Send `GET /api/v1/tenant/users` to `unknown.yourhrm.com` | HTTP 404 Not Found; no API endpoints are exposed. |
| 7 | Verify the 404 page does not leak platform information | No version numbers, no stack traces, no internal service names. |
| 8 | Verify the middleware short-circuits the pipeline | No downstream middleware or controllers are invoked. |
| 9 | Test with an invalid subdomain format (e.g., `a.yourhrm.com` - too short, `UPPER.yourhrm.com` - uppercase) | HTTP 404 or appropriate error; subdomain validation rejects invalid formats. |
| 10 | Verify Redis negative cache behavior | Failed lookups may be cached briefly to prevent DB hammering. |

## 6. Postconditions
- No tenant context is established.
- No data is exposed to the requester.
- The request pipeline is short-circuited at the middleware level.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
