---
id: TC-AUTH-108
user_story: US-AUTH-010
module: Authentication
priority: medium
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-108: Lockout UI displays correct error banner and admin UI shows locked badge

## 1. Test Objective
Verify the frontend lockout UI behavior: (a) the login form displays the lockout error banner with the correct message (no countdown timer), (b) the banner disappears after lockout expiry on the next attempt, (c) the Tenant Admin user management page shows a "Locked" badge on locked accounts, and (d) the admin can click an "Unlock" action button.

## 2. Related Requirements
- User Story: US-AUTH-010
- UI/UX Notes: Section 8

## 3. Preconditions
- User `alice@acme.com` is locked (`locked_until` approximately 15 minutes in the future).
- Tenant admin `admin@acme.com` is authenticated and can access the User Management page.
- The Angular frontend is running.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Locked user | alice@acme.com | For login form test |
| Tenant admin | admin@acme.com | For admin UI test |
| Lockout duration | 15 minutes | Display in message |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the tenant login page and attempt to log in as `alice@acme.com` with any password. | Login form displays a subtle but clear error banner: "Your account has been temporarily locked due to too many failed login attempts. Please try again in 15 minutes or contact your administrator." |
| 2 | Verify the banner does NOT display a countdown timer. | No real-time countdown is shown (to avoid timing information leakage per UI/UX Notes). |
| 3 | Wait for lockout to expire. Submit login again with correct credentials. | Login succeeds; the error banner disappears. The normal post-login view is shown. |
| 4 | As `admin@acme.com`, navigate to Tenant Admin > User Management. | User list page loads. |
| 5 | Locate `alice@acme.com` (re-lock the account if needed for this step). | A "Locked" badge with a red indicator is displayed next to alice's name/row. |
| 6 | Click the "Unlock" action button next to the locked user. | The unlock request is sent; the "Locked" badge disappears and the user's status returns to normal. |
| 7 | Navigate to Tenant Admin > Security Settings. | Lockout policy configuration form is displayed with fields for "Max Failed Attempts" and "Lockout Duration (minutes)." |
| 8 | Verify the form fields show the current policy values and accept changes within bounds. | Fields display current values and allow editing within the 3-10 and 5-60 ranges. |

## 6. Postconditions
- Lockout UI messaging is consistent with the design specifications.
- Admin UI correctly displays locked status and provides unlock functionality.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
