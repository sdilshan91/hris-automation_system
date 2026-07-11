---
id: US-AUTH-016
module: Authentication & Authorization
priority: Should Have
persona: Tenant Admin
status: draft
created: 2026-06-21
sprint: backlog
acceptance_criteria_count: 7
---

# US-AUTH-016: SSO enforcement, break-glass & admin-consent onboarding

## 1. Description
**As a** tenant admin,
**I want to** require my users to sign in only with Microsoft (SSO-only), while keeping a guaranteed break-glass path for designated administrators,
**So that** I can enforce my corporate identity policy without ever risking locking my organization out of HRM.

**As a** customer Microsoft 365 admin onboarding to SSO,
**I want** a guided admin-consent flow that registers my organization's Entra directory with my HRM workspace,
**So that** my employees can begin signing in with Microsoft safely and with correct tenant isolation.

## 2. Preconditions
- Per-tenant SSO config (US-AUTH-012), isolation (US-AUTH-013), and matching/JIT (US-AUTH-014) exist.
- The FE login supports `sso_only` rendering (US-AUTH-015).
- At least one designated break-glass administrator exists with local credentials.
- The vendor multi-tenant Entra app (CR-AUTH-001 §4) supports the admin-consent URL.

## 3. Acceptance Criteria
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A tenant admin sets `enforcement_mode = sso_only` | They save settings | New non-break-glass logins via email/password are refused with "Your organization requires sign-in with Microsoft," and only the SSO path (or break-glass) is accepted. |
| AC-2 | A tenant has `sso_only` enforced | A designated **break-glass** admin signs in with local credentials via the break-glass path | The login is permitted (bypassing SSO-only enforcement), and a high-visibility `break_glass_login` audit event is logged. |
| AC-3 | A tenant admin attempts to enable `sso_only` | There is **no** designated break-glass admin (or the SSO config is incomplete/untested) | The system blocks the change with a clear explanation and requires designating a break-glass admin (and recommends a successful test login) first. |
| AC-4 | A customer Microsoft 365 admin starts onboarding | They open the SSO onboarding flow in HRM | The system generates the correct **admin-consent URL** for the vendor app and guides them to grant tenant-wide consent in Entra. |
| AC-5 | The customer admin completes Microsoft admin consent and is returned to HRM | They finish onboarding | The system records the customer's Entra Directory ID (`tid`) into the tenant's allow-list (US-AUTH-012), marks SSO as ready to enable, and audits `sso_admin_consent_completed`. |
| AC-6 | A customer admin's consent fails or is declined | They are returned to HRM with an error | The system does not enable SSO, shows a remediation message, and audits `sso_admin_consent_failed`; the tenant remains on its prior login mode. |
| AC-7 | `sso_only` is enforced and a regular (non-break-glass) user has only local credentials and no SSO membership | They attempt to log in | They are refused via SSO-only and directed to contact their administrator; they cannot use the break-glass path (it is restricted to designated admins). |

## 4. Functional Requirements
- FR-1: The system SHALL support `enforcement_mode = sso_only`, refusing standard local logins for the tenant while permitting SSO and the break-glass path.
- FR-2: The system SHALL maintain a **break-glass** capability: at least one designated administrator can always authenticate with local credentials regardless of `sso_only`.
- FR-3: The system SHALL prevent enabling `sso_only` unless a break-glass admin is designated (and SHOULD recommend a verified successful SSO test login first).
- FR-4: Break-glass logins SHALL be logged as high-severity audit events and SHOULD trigger an admin notification.
- FR-5: The system SHALL generate the customer **admin-consent URL** for the vendor multi-tenant app and handle the consent return.
- FR-6: On successful admin consent, the system SHALL capture the customer's Entra Directory ID (`tid`) into the tenant allow-list and mark SSO ready.
- FR-7: All enforcement changes, consent outcomes, and break-glass logins SHALL be audited (US-NTF-004).
- FR-8: Disabling SSO or reverting to `optional` SHALL be possible at any time by a tenant admin without data loss, re-enabling local login for all users.

## 5. Non-Functional Requirements
- NFR-1: The break-glass path SHALL remain functional even if Entra, the vendor app, or the allow-list is misconfigured or unreachable (no external dependency on the break-glass login).
- NFR-2: Break-glass audit + notification SHALL be emitted within 60 seconds of the event (Hangfire), consistent with US-AUTH-010 NFR-3.
- NFR-3: The admin-consent flow SHALL complete within the customer admin's single browser session and be resumable if interrupted.
- NFR-4: Enforcement evaluation SHALL add negligible overhead to the login path (cached tenant setting).

## 6. Business Rules
- BR-1: A tenant SHALL never be enforceable into a state where no one can log in — the break-glass admin path is mandatory before `sso_only`.
- BR-2: Break-glass is restricted to explicitly designated admin accounts; it is not a general escape hatch for ordinary users.
- BR-3: Admin consent records the customer `tid` for isolation (US-AUTH-013); consent alone does not enable SSO — the admin still explicitly enables it (US-AUTH-012).
- BR-4: Every break-glass login is treated as a security-sensitive event (audited + notified) to discourage routine use.
- BR-5: Reverting enforcement to `optional` restores normal local login for everyone immediately.
- BR-6: Enforcement and onboarding are tenant-scoped; one tenant's enforcement never affects another.

## 7. Data Requirements
- **`TenantAuthSettings`:** `enforcement_mode` (`optional` | `sso_only`), `break_glass_admin_user_ids` (list), `sso_onboarding_status` (`not_started` | `consent_pending` | `consented` | `enabled`).
- **Admin-consent URL inputs:** vendor `ClientId`, customer `tid` (or `organizations`), fixed redirect.
- **Audit records:** `sso_enforcement_changed`, `break_glass_login`, `sso_admin_consent_completed`, `sso_admin_consent_failed`.
- **Notification data:** tenant, admin user, timestamp, source IP for break-glass alerts.

## 8. UI/UX Notes
- Tenant Admin > Security > SSO: an "Enforcement" sub-section with Optional / SSO-only, a designated-break-glass-admin picker, and a guarded confirmation dialog explaining the lockout risk before enabling `sso_only`.
- An onboarding wizard: (1) "Grant admin consent" button → opens the Microsoft admin-consent URL; (2) on return, confirm captured Directory ID; (3) review allow-list; (4) optional test login; (5) enable SSO.
- `sso_only` login page (US-AUTH-015): primary Microsoft button + discreet "Administrator sign-in" break-glass link.
- Break-glass logins surface prominently in the audit viewer (US-NTF-005) with a distinct badge.
- Clear, reassuring copy throughout about the break-glass safety net.

## 9. Dependencies
- US-AUTH-012 (config) — stores enforcement mode, break-glass list, onboarding status, and the consent-captured `tid`.
- US-AUTH-013 (isolation) — consumes the consent-captured `tid`.
- US-AUTH-014 (matching/JIT) — governs which users can actually log in once enforced.
- US-AUTH-015 (FE) — renders `sso_only` + break-glass entry.
- US-AUTH-001 (local login) — the mechanism the break-glass path reuses.
- US-NTF-004/005 (audit) and the notification service (Hangfire + SMTP).

## 10. Assumptions & Constraints
- Break-glass deliberately preserves a local-credential path even under SSO-only; customers accept this as the anti-lockout trade-off (analogous to a cloud "emergency access account").
- Admin consent is a customer-side Entra action; HRM can only generate the URL and react to the return, not force consent.
- v1 captures a single `tid` per consent; additional directories can be added manually (US-AUTH-012, CR-AUTH-001 OQ-4).
- Group-based restriction of who may use SSO is out of scope; enforcement is all-or-(break-glass).

## 11. Test Hints
- **Enforce sso_only:** Enable with a break-glass admin; assert ordinary local login refused, SSO accepted.
- **Break-glass:** As designated admin under sso_only, log in locally; assert success + `break_glass_login` audit + notification.
- **Block without break-glass:** Attempt sso_only with no designated admin; assert blocked with explanation.
- **Non-admin under enforcement:** Ordinary user with only local creds; assert refused and cannot use break-glass.
- **Admin consent success:** Drive the consent flow; assert `tid` captured into allow-list + `sso_admin_consent_completed`.
- **Admin consent failure:** Simulate declined consent; assert SSO not enabled + `sso_admin_consent_failed`, prior mode intact.
- **Revert:** Switch sso_only → optional; assert all users can log in locally again immediately.
- **Resilience:** With Entra unreachable, assert the break-glass path still works.
- **Isolation:** Assert one tenant's enforcement/onboarding never affects another tenant's login.
