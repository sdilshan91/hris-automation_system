---
id: TC-AUTH-135
user_story: US-AUTH-016
module: Authentication
priority: high
type: security
status: pass
created: 2026-07-24
---

# TC-AUTH-135: Every enforcement change is audited as `sso_enforcement_changed` with before/after and actor

## 1. Test Objective
Verify FR-7: all enforcement-mode changes are recorded in the audit log (US-NTF-004) as `sso_enforcement_changed`, capturing the before/after `enforcement_mode`, the acting admin, tenant, and timestamp -- for both directions (`optional -> sso_only` and `sso_only -> optional`). No secret material is written. This complements the break-glass (`break_glass_login`) and consent (`sso_admin_consent_completed`/`_failed`) audit events into full enforcement/onboarding audit coverage.

## 2. Related Requirements
- User Story: US-AUTH-016
- Acceptance Criteria: AC-2 (auditability of enforcement)
- Functional Requirements: FR-7
- Data Requirements: `sso_enforcement_changed` audit record

## 3. Preconditions
- Tenant "acme" has a designated break-glass admin and a complete SSO config (so both directions are permitted).
- `admin-a@acme.com` is a tenant admin permitted to change enforcement; the audit trail (US-NTF-004/005) is available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Write endpoint | PUT /api/v1/tenant/auth-settings | Enforcement change |
| Audit action | `sso_enforcement_changed` | US-NTF-004 |
| Transition A | optional -> sso_only | With before/after |
| Transition B | sso_only -> optional | With before/after |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `admin-a@acme.com`, `PUT /api/v1/tenant/auth-settings` changing `enforcement_mode` from `optional` to `sso_only`. | HTTP 200. Exactly one `sso_enforcement_changed` audit record for acme with before=`optional`, after=`sso_only`, actor=admin-a, tenant, timestamp. |
| 2 | Change it back: `PUT` `enforcement_mode = optional`. | HTTP 200. A second `sso_enforcement_changed` record with before=`sso_only`, after=`optional` and the actor/timestamp. |
| 3 | Submit a `PUT` that does NOT change `enforcement_mode` (e.g. edits only the allow-list). | No spurious `sso_enforcement_changed` record is written for an unchanged enforcement value (the SSO-config change may audit separately as `sso_config_updated`). |
| 4 | Inspect the recorded records. | No client secret / cert / token material appears in any audit payload (FR-7). |

## 6. Postconditions
- Two `sso_enforcement_changed` records exist (one per real transition); no record for the no-op change.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
