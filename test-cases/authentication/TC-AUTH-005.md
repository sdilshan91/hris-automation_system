---
id: TC-AUTH-005
user_story: US-AUTH-002
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-05-11
---

# TC-AUTH-005: JWT issued on successful login

## 1. Test Objective
Verify that upon successful authentication, the system issues a correctly structured JWT access token (RS256, ~15 min expiry) with all required claims and a refresh token as an httpOnly cookie.

## 2. Related Requirements
- User Story: US-AUTH-002
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-2, FR-3, FR-4, FR-5

## 3. Preconditions
- User `john@acme.com` has valid credentials and an active membership in tenant "acme" with the "Manager" role.
- The JWT signing key (RS256) is configured in the secrets vault.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Email | john@acme.com | Valid user |
| Password | S3cure!Pass2026 | Correct password |
| Expected tenant_id | (acme tenant UUID) | From tenant table |
| Expected roles | ["Manager"] | Assigned role |
| Expected permissions | ["Leave.Approve.Team", "Employee.View.Team", ...] | Union of Manager role permissions |
| Token algorithm | RS256 | Asymmetric signing |
| Token expiry | ~15 minutes | From iat |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/auth/login` to `acme.yourhrm.com` with valid credentials | HTTP 200 response with access token in body. |
| 2 | Decode the JWT header | Algorithm is `RS256`; `typ` is `JWT`. |
| 3 | Decode the JWT payload and verify `sub` claim | Contains the user's UUID. |
| 4 | Verify `email` claim | Contains `john@acme.com`. |
| 5 | Verify `tenant_id` claim | Matches the "acme" tenant's UUID. |
| 6 | Verify `user_tenant_id` claim | Contains the user_tenant record UUID. |
| 7 | Verify `roles` claim | Contains `["Manager"]`. |
| 8 | Verify `permissions` claim | Contains the expected permissions array for the Manager role. |
| 9 | Verify `is_impersonation` claim | Is `false`. |
| 10 | Verify `iat` and `exp` claims | `exp - iat` is approximately 900 seconds (15 minutes). |
| 11 | Verify `iss` and `aud` claims | Present and match expected issuer/audience values. |
| 12 | Verify JWT signature using the public key | Signature is valid with the current RS256 public key. |
| 13 | Verify the refresh token cookie in `Set-Cookie` header | Cookie attributes: `httpOnly`, `Secure`, `SameSite=Strict`, expiry approximately 7 days. |
| 14 | Query the `refresh_token` table | A record exists with `user_id`, `tenant_id`, `token_hash` (SHA-256), `issued_at`, `expires_at`, `user_agent`, `ip_address`; `revoked_at` is null. |

## 6. Postconditions
- A valid JWT access token is available in memory on the client.
- A refresh token cookie is set in the browser.
- A `refresh_token` record exists in the database with the token hash.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
