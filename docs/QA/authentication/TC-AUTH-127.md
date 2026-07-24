---
id: TC-AUTH-127
user_story: US-AUTH-016
module: Authentication
priority: critical
type: security
status: draft
created: 2026-07-24
---

# TC-AUTH-127: Designated break-glass admin logs in locally under `sso_only` -> permitted + high-severity `break_glass_login` audit + admin notification within 60s

## 1. Test Objective
Verify AC-2 / FR-2 / FR-4 / NFR-2: with `enforcement_mode = sso_only` in effect, a designated break-glass administrator can still authenticate with local email/password via the break-glass path (bypassing SSO-only enforcement). The event is recorded as a high-visibility `break_glass_login` audit record and triggers an admin notification (email) delivered within 60 seconds via Hangfire, carrying tenant, admin user, timestamp, and source IP.

## 2. Related Requirements
- User Story: US-AUTH-016
- Acceptance Criteria: AC-2
- Functional Requirements: FR-2, FR-4, FR-7
- Non-Functional Requirements: NFR-2
- Business Rules: BR-2, BR-4

## 3. Preconditions
- Tenant "acme" is on `sso_only` with a valid SSO config (as in TC-AUTH-126).
- `admin-a@acme.com` is a designated break-glass admin (its user id is in `break_glass_admin_user_ids`) with known local credentials.
- A notification recipient for security alerts is configured for acme; Hangfire is running.
- The audit trail (US-NTF-004) and audit viewer (US-NTF-005) are available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Break-glass login | POST /api/v1/auth/login (via the "Administrator sign-in" path) | Reuses US-AUTH-001 local login |
| Break-glass admin | admin-a@acme.com / correct password | In `break_glass_admin_user_ids` |
| Audit action | `break_glass_login` | High severity, distinct badge in US-NTF-005 |
| Notification SLA | <= 60 seconds | NFR-2 (consistent with US-AUTH-010 NFR-3) |
| Source IP | 203.0.113.10 (example) | Captured into the alert |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | On the acme `sso_only` login page, follow the discreet "Administrator sign-in" break-glass link and `POST /api/v1/auth/login` as `admin-a@acme.com` with the correct password. | Login PERMITTED despite `sso_only` (break-glass bypass). A normal app JWT + refresh token are issued for admin-a in acme (HTTP 200). |
| 2 | Immediately inspect the tenant audit log (US-NTF-004/005). | Exactly one `break_glass_login` record for acme + admin-a, flagged high-severity with a distinct badge, containing tenant, admin user, timestamp, and source IP `203.0.113.10`. |
| 3 | Wait up to 60 seconds and check the configured security-alert recipient. | A break-glass admin-notification email is delivered within 60s (Hangfire), naming the tenant, admin user, time, and source IP (FR-4, NFR-2). |
| 4 | Confirm the record is treated as security-sensitive (BR-4). | The event surfaces prominently in the audit viewer -- it is not a routine/low-severity login entry. |

## 6. Postconditions
- The break-glass admin holds a valid acme session.
- One high-severity `break_glass_login` audit record exists; one admin notification was sent within 60s.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
