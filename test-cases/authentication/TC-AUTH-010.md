---
id: TC-AUTH-010
user_story: US-AUTH-004
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-05-11
---

# TC-AUTH-010: Forgot password sends reset email

## 1. Test Objective
Verify that the forgot-password endpoint accepts an email address and always returns a 200 response with a generic message, regardless of whether the email exists, to prevent user enumeration. When the email matches an active user, a reset email is sent.

## 2. Related Requirements
- User Story: US-AUTH-004
- Acceptance Criteria: AC-1, AC-2
- Functional Requirements: FR-1, FR-3, FR-7, FR-8, FR-9
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- User `john@acme.com` has an active membership in tenant "acme".
- SMTP/transactional email service is configured and operational.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Email (existing) | john@acme.com | Registered user |
| Email (non-existing) | nobody@acme.com | Not registered |
| Forgot-password endpoint | POST /api/v1/auth/forgot-password | Accepts { email } |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/auth/forgot-password` to `acme.yourhrm.com` with `{ email: "john@acme.com" }` | HTTP 200 OK with message "If an account exists, a reset link has been sent." |
| 2 | Verify the reset email is received at `john@acme.com` | Email arrives within 60 seconds. |
| 3 | Verify the email is tenant-branded | Email contains tenant logo, colors, and tenant name "Acme". |
| 4 | Verify the reset link in the email | Link points to `https://acme.yourhrm.com/reset-password?token={token}&email=john@acme.com`. |
| 5 | Verify the reset token has been generated and stored securely | A token record exists (ASP.NET Core Identity token provider), with approximately 1-hour expiry. |
| 6 | Verify a `password_reset_requested` audit event is logged | Audit record contains email, IP address, tenant_id. |
| 7 | Send `POST /api/v1/auth/forgot-password` with `{ email: "nobody@acme.com" }` (non-existent) | HTTP 200 OK with the same generic message "If an account exists, a reset link has been sent." |
| 8 | Verify no email is sent for the non-existent address | No outbound email for `nobody@acme.com`. |
| 9 | Compare response times between step 1 and step 7 | Response times are comparable (no timing side-channel). |
| 10 | Send 6 requests for the same email within 1 hour | The 6th request is rate-limited (HTTP 429 Too Many Requests). |

## 6. Postconditions
- For valid email: a reset token exists and a branded email was sent.
- For invalid email: no token created, no email sent, same response returned.
- Audit events are recorded for all requests.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
