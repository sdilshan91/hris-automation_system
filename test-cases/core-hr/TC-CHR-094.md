---
id: TC-CHR-094
user_story: US-CHR-001
module: Core HR
priority: high
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-094: CSRF protection on employee creation endpoint

## 1. Test Objective
Verify that the employee creation API endpoint is protected against Cross-Site Request Forgery (CSRF) attacks. Requests from unauthorized origins or without proper anti-forgery tokens should be rejected.

## 2. Related Requirements
- User Story: US-CHR-001
- Non-Functional Requirements: Security (CSRF protection)

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- The API uses JWT Bearer authentication (inherently CSRF-resistant for API calls, but additional headers may be required).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Origin header | evil-site.com | Unauthorized origin |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/tenant/employees` from a legitimate origin with valid JWT | 201 Created (normal operation). |
| 2 | Send `POST /api/v1/tenant/employees` with a valid JWT but Origin header = "evil-site.com" | Request is rejected (CORS policy blocks cross-origin requests) or the API validates the origin. |
| 3 | Verify the CORS policy only allows the configured tenant subdomains | Access-Control-Allow-Origin does not include "evil-site.com". |
| 4 | Verify that cookie-based session tokens (if any) require SameSite=Strict or Lax | Cookies are not sent from cross-origin requests. |

## 6. Postconditions
- Cross-origin requests from unauthorized domains cannot create employee records.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
