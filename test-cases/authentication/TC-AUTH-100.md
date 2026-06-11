---
id: TC-AUTH-100
user_story: US-AUTH-010
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-100: Lockout notification email sent within 60 seconds via Hangfire

## 1. Test Objective
Verify that when an account is locked, a notification email is sent to the user within 60 seconds of the lockout event, dispatched via a Hangfire background job (FR-8, NFR-3). Verify the email content includes lockout instructions.

## 2. Related Requirements
- User Story: US-AUTH-010
- Functional Requirements: FR-8
- Non-Functional Requirements: NFR-3
- Data Requirements: Email notification data (user email, tenant name(s), lockout timestamp, lockout duration, support contact link)

## 3. Preconditions
- User `alice@acme.com` has `failed_login_count = 4` (one attempt away from lockout).
- SMTP/email service is configured and functional.
- Hangfire job processing is active.
- An email trap (e.g., Papercut, MailHog) is capturing outbound emails.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User email | alice@acme.com | Recipient of notification |
| Tenant name | Acme Corp | Included in email |
| Max failed attempts | 5 | Threshold |
| Lockout duration | 15 minutes | Included in email |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Record the current UTC time as `T_lockout`. | Timestamp captured. |
| 2 | Send `POST /api/v1/auth/login` with wrong password (the 5th failure, triggering lockout). | HTTP 401 with lockout message; account is locked. |
| 3 | Monitor the email trap for an email sent to `alice@acme.com`. | Email arrives within 60 seconds of `T_lockout`. |
| 4 | Record the email receipt time as `T_email`. Compute `T_email - T_lockout`. | Difference is <= 60 seconds (NFR-3). |
| 5 | Verify the email subject indicates an account lockout (e.g., "Your account has been temporarily locked"). | Subject is clear and appropriate. |
| 6 | Verify the email body includes: user's name or email, lockout duration (15 minutes), instructions to wait or contact their administrator, support contact link. | All required fields are present per Data Requirements. |
| 7 | Verify the email body does NOT include sensitive data like the password or the number of failed attempts. | No sensitive data exposed. |
| 8 | Verify a Hangfire job record exists for the lockout notification (check Hangfire dashboard or `Hangfire.Job` table). | Job was enqueued and processed. |

## 6. Postconditions
- Lockout notification email was sent within 60 seconds.
- Email contains all required lockout information.
- Hangfire job completed successfully.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
