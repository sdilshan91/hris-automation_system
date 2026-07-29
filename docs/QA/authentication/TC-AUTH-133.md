---
id: TC-AUTH-133
user_story: US-AUTH-016
module: Authentication
priority: high
type: functional
status: pass
created: 2026-07-24
---

# TC-AUTH-133: Declined/failed admin consent leaves SSO disabled, keeps the prior login mode, and audits `sso_admin_consent_failed` with a remediation message

## 1. Test Objective
Verify AC-6: when the customer admin's Microsoft admin consent is declined or fails, HRM does NOT enable SSO, shows a clear remediation message, audits `sso_admin_consent_failed`, and leaves the tenant on its prior login mode (`enforcement_mode` and `sso_enabled` unchanged). No `tid` is captured from a failed consent.

## 2. Related Requirements
- User Story: US-AUTH-016
- Acceptance Criteria: AC-6
- Functional Requirements: FR-7
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" is mid-onboarding (`sso_onboarding_status = consent_pending`), a valid signed `state` outstanding.
- Prior state: `sso_enabled = false`, `enforcement_mode = optional`, allow-list without any customer `tid`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Consent return (declined) | GET {fixed-redirect}?error=access_denied&error_description=...&state={signed} | Admin declined |
| Audit action | `sso_admin_consent_failed` | US-NTF-004 |
| Expected UI | Remediation message + retry | AC-6 |
| sso_enabled (after) | false | Unchanged |
| enforcement_mode (after) | optional | Prior mode intact |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Simulate the Microsoft return with `error=access_denied` (declined) and the valid signed `state`. | HRM handles the failure gracefully; the wizard shows a clear remediation message (what went wrong + how to retry consent). No unhandled error. |
| 2 | Re-read `GET /api/v1/tenant/auth-settings` for acme. | `sso_enabled` is false and `enforcement_mode` is still `optional` -- prior login mode intact. No customer `tid` was added to the allow-list. |
| 3 | Check `sso_onboarding_status`. | Status is NOT advanced to `consented`/`enabled` (remains `consent_pending` or reverts to `not_started`); the tenant is safe to keep using local login. |
| 4 | Inspect the audit log. | Exactly one `sso_admin_consent_failed` record for acme with the failure reason (no secret material). |
| 5 | (Interrupted-flow variant, NFR-3) Abandon consent mid-session, then reopen the wizard. | The flow is resumable from onboarding status; no partial/dirty enablement occurred. |

## 6. Postconditions
- SSO remains disabled; tenant stays on local login; a failure was audited; no `tid` captured.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
