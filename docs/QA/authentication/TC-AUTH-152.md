---
id: TC-AUTH-152
user_story: US-AUTH-011
module: Authentication
priority: high
type: functional
status: pass
created: 2026-07-24
---

# TC-AUTH-152: Challenge with no/unresolved tenant is rejected (`tenant_required`) and never redirects to Microsoft

## 1. Test Objective
Verify AC-1 boundary / FR-3: the challenge requires a resolved HRM tenant. When no tenant subdomain is supplied (or it resolves empty), the system does NOT build a Microsoft redirect; it returns the user to the login page with a `tenant_required` `sso_error`. This distinguishes "no tenant" from "SSO not configured" (ISSUE-220) and prevents a state-less redirect.

## 2. Related Requirements
- User Story: US-AUTH-011
- Acceptance Criteria: AC-1
- Functional Requirements: FR-3

## 3. Preconditions
- Entra SSO configured at the deployment level (`IsConfigured == true`), so a missing tenant is the ONLY reason for failure.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Challenge (no tenant) | GET /api/v1/auth/sso/challenge | `tenant` query param omitted |
| Challenge (blank tenant) | GET /api/v1/auth/sso/challenge?tenant= | Empty value |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `GET /api/v1/auth/sso/challenge` (no `tenant`). | Result `tenant_required`; HTTP 302 to `/auth/login?sso_error=tenant_required`. NO redirect to `login.microsoftonline.com`. |
| 2 | `GET /api/v1/auth/sso/challenge?tenant=` (blank). | Same `tenant_required` outcome (trimmed/lowercased to empty). |
| 3 | Distinguish from not-configured. | With SSO configured, the failure code is `tenant_required`, NOT `not_configured` (ISSUE-220 — the two are separable in logs/UX). |
| 4 | Positive control: `GET .../challenge?tenant=acme`. | 302 to Microsoft with a signed state (TC-AUTH-137) — confirms only the missing tenant caused the earlier rejection. |

## 6. Postconditions
- No Microsoft redirect (and no state) is produced without a resolved tenant.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
