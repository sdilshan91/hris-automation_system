---
id: TC-AUTH-151
user_story: US-AUTH-011
module: Authentication
priority: high
type: integration
status: draft
created: 2026-07-24
---

# TC-AUTH-151: Registering the OIDC/SSO scheme leaves the default bearer-JWT API protection unchanged

## 1. Test Objective
Verify FR-7 / BR-1: the SSO endpoints are added IN ADDITION to the existing `AddJwtBearer` scheme, and bearer-JWT validation for normal API requests remains the default and is unaffected. A standard authenticated API call with a valid app JWT still authenticates; an invalid/missing bearer is still rejected — the presence of the SSO controller/handler changes neither.

## 2. Related Requirements
- User Story: US-AUTH-011
- Functional Requirements: FR-7
- Business Rules: BR-1 (SSO terminates in the same app JWT; downstream unchanged)

## 3. Preconditions
- Backend running with Entra SSO registered. A valid app JWT for `user-a@acme.com` (obtained via local login OR via SSO — both mint identical tokens).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Protected endpoint | GET /api/v1/auth/me | Requires bearer JWT |
| Valid bearer | Authorization: Bearer {app-jwt} | From local login or SSO |
| SSO endpoints | /api/v1/auth/sso/challenge, /callback | `[AllowAnonymous]` |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `GET /api/v1/auth/me` with a valid app JWT. | HTTP 200 — bearer-JWT validation still the default; SSO registration did not disturb it. |
| 2 | `GET /api/v1/auth/me` with a missing/invalid/expired bearer. | HTTP 401 — bearer scheme still rejects as before. |
| 3 | `GET /api/v1/auth/me` presenting NO bearer but relying on any SSO/OIDC cookie. | HTTP 401 — the OIDC scheme does not become an ambient authentication path for API endpoints; only the bearer scheme authorizes API calls. |
| 4 | Obtain a JWT via the SSO happy path (TC-AUTH-138), then call `GET /api/v1/auth/me` with it. | HTTP 200 — an SSO-minted JWT is identical in shape to a local one and authenticates through the same bearer scheme (BR-1). |
| 5 | Confirm the SSO endpoints are anonymous. | `/api/v1/auth/sso/challenge` and `/callback` are reachable without a bearer (`[AllowAnonymous]`), as required to start/finish the browser flow. |

## 6. Postconditions
- Bearer-JWT API protection is unchanged by SSO registration; SSO endpoints are anonymous entry points only.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
